namespace PanoramaVote.Core.Managers;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Logging;
using PanoramaVote.Shared;
using Sharp.Modules.AdminManager.Shared;
using Sharp.Modules.LocalizerManager.Shared;
using Sharp.Shared;
using Sharp.Shared.Enums;
using Sharp.Shared.GameEntities;
using Sharp.Shared.Listeners;
using Sharp.Shared.Managers;
using Sharp.Shared.Objects;
using Sharp.Shared.Types;
using Sharp.Shared.Units;

/// <summary>
///     Port of SLAYER's <c>CPanoramaVote</c> engine. Drives the CS2 Panorama YES/NO vote UI by
///     writing <c>vote_controller</c> netvars, sending the vote user-messages, and listening for
///     the <c>vote_cast</c> game event. There is no native yes/no vote function in the engine.
/// </summary>
internal sealed class PanoramaVoteManager : IModule, IEventListener, IGameListener, IPanoramaVoteService
{
    private const string ModuleIdentity = "PanoramaVote.Core";
    private const string PermManage     = "panoramavote:admin:manage";

    private const int OptionCount = 5;

    // Fixed duration for the admin `vote` command.
    private const float VoteSeconds = 20f;

    private readonly ILogger<PanoramaVoteManager> _logger;
    private readonly IEntityManager               _entityManager;
    private readonly IClientManager               _clientManager;
    private readonly IEventManager                _eventManager;
    private readonly IModSharp                     _modSharp;
    private readonly ISharpModuleManager          _sharpModuleManager;

    private ILocalizerManager?                     _localizer;
    private IClientManager.DelegateClientCommand?  _revoteCallback;

    // Vote state (mirrors CPanoramaVote members).
    private int                _voteCount;
    private bool               _voteInProgress;
    private YesNoVoteHandler?  _voteHandler;
    private YesNoVoteResult?   _voteResult;
    private int                _voterCount;
    private readonly int[]     _voters = new int[IPanoramaVoteService.MAXPLAYERS];
    private int                _currentCaller;
    private string             _currentTitle  = string.Empty;
    private string             _currentDetail = string.Empty;

    // Never cache the entity pointer across callbacks — store the index and re-resolve.
    private EntityIndex? _controllerIndex;

    public PanoramaVoteManager(ILogger<PanoramaVoteManager> logger)
    {
        _logger             = logger;
        _entityManager      = InterfaceBridge.Instance.EntityManager;
        _clientManager      = InterfaceBridge.Instance.ClientManager;
        _eventManager       = InterfaceBridge.Instance.EventManager;
        _modSharp           = InterfaceBridge.Instance.ModSharp;
        _sharpModuleManager = InterfaceBridge.Instance.SharpModuleManager;
    }

    // ── IEventListener ────────────────────────────────────────────────────────

    int IEventListener.ListenerVersion  => IEventListener.ApiVersion;
    int IEventListener.ListenerPriority => 0;

    public void FireGameEvent(IGameEvent @event)
    {
        switch (@event.GetName())
        {
            case "vote_cast":
                OnVoteCast(@event);
                break;

            // A vote left running when the round ends would otherwise strand: the vote-end timer
            // is StopOnRoundEnd and would be silently discarded, leaving _voteInProgress true and
            // the panel stuck. Resolve it here with whatever's been tallied so far.
            case "round_end":
                if (_voteInProgress)
                {
                    EndVote(YesNoVoteEndReason.VoteEnd_TimeUp);
                }
                break;
        }
    }

    // ── IGameListener ─────────────────────────────────────────────────────────

    int IGameListener.ListenerVersion  => IGameListener.ApiVersion;
    int IGameListener.ListenerPriority => 0;

    // Map end: same stranding risk as round_end (the timer is StopOnMapEnd). Force-resolve so the
    // manager, which persists across maps, never carries a stuck vote into the next map.
    public void OnGameDeactivate()
    {
        if (_voteInProgress)
        {
            EndVote(YesNoVoteEndReason.VoteEnd_Cancelled);
        }
    }

    // ── IModule lifecycle ─────────────────────────────────────────────────────

    public bool Init() => true;

    public void OnPostInit() { }

    public void OnAllSharpModulesLoaded()
    {
        _localizer = _sharpModuleManager
            .GetOptionalSharpModuleInterface<ILocalizerManager>(ILocalizerManager.Identity)?.Instance;
        _localizer?.LoadLocaleFile("panoramavote");

        RegisterCommands();

        _eventManager.HookEvent("vote_cast");
        _eventManager.HookEvent("round_end");
        _eventManager.InstallEventListener(this);
        _modSharp.InstallGameListener(this);
    }

    public void Shutdown()
    {
        _eventManager.RemoveEventListener(this);
        _modSharp.RemoveGameListener(this);

        if (_revoteCallback is { } cb)
        {
            _clientManager.RemoveCommandCallback("revote", cb);
        }

        _revoteCallback = null;
        _localizer      = null;
    }

    // ── IPanoramaVoteService ──────────────────────────────────────────────────

    public bool IsVoteInProgress => _voteInProgress;

    public bool SendYesNoVote(float duration,
        int                         caller,
        string                      title,
        string                      detail,
        RecipientFilter             filter,
        YesNoVoteResult             result,
        YesNoVoteHandler?           handler = null)
    {
        if (result is null)
        {
            return false;
        }

        if (_voteInProgress)
        {
            _logger.LogWarning("[PanoramaVote] A vote is already in progress.");
            return false;
        }

        var voters = ResolveRecipients(filter);
        if (voters.Count <= 0)
        {
            return false;
        }

        if (GetController() is not { } controller)
        {
            return false;
        }

        ResetController(controller);

        _logger.LogInformation("[PanoramaVote] Starting vote [id:{Id}] Duration:{Duration} Caller:{Caller} NumClients:{Count}",
            _voteCount, duration, caller, voters.Count);

        _voteInProgress = true;
        InitVoters(voters);

        controller.SetNetVar("m_nPotentialVotes", _voterCount);
        controller.SetNetVar("m_bIsYesNoVote", true);
        controller.SetNetVar("m_iActiveIssueIndex", 2);

        _voteResult    = result;
        _voteHandler   = handler;
        _currentCaller = caller;
        _currentTitle  = title;
        _currentDetail = detail;

        UpdateVoteCounts(controller);
        SendVoteStartUM(filter);

        _voteHandler?.Invoke(YesNoVoteAction.VoteAction_Start, 0, 0);

        // Vote-id guard: a stale timer must not end a newer vote.
        var voteNum = _voteCount;
        _modSharp.PushTimer(() =>
            {
                if (voteNum == _voteCount)
                {
                    EndVote(YesNoVoteEndReason.VoteEnd_TimeUp);
                }
            },
            duration,
            GameTimerFlags.StopOnRoundEnd | GameTimerFlags.StopOnMapEnd);

        return true;
    }

    public bool SendYesNoVoteToAll(float duration,
        int                              caller,
        string                           title,
        string                           detail,
        YesNoVoteResult                  result,
        YesNoVoteHandler?                handler = null)
    {
        var clients = EnumerateEligible().ToList();

        return SendYesNoVote(duration, caller, title, detail, new RecipientFilter(clients), result, handler);
    }

    public void CancelVote()
    {
        if (!_voteInProgress)
        {
            return;
        }

        EndVote(YesNoVoteEndReason.VoteEnd_Cancelled);
    }

    // ── Vote flow ─────────────────────────────────────────────────────────────

    private void OnVoteCast(IGameEvent @event)
    {
        if (!_voteInProgress)
        {
            return;
        }

        if (GetController() is not { } controller)
        {
            return;
        }

        if (_voteHandler is { } handler && @event.GetPlayerController("userid") is { } voter)
        {
            handler(YesNoVoteAction.VoteAction_Vote, voter.PlayerSlot.AsPrimitive(), @event.GetInt("vote_option"));
        }

        UpdateVoteCounts(controller);
        CheckForEarlyVoteClose(controller);
    }

    private void CheckForEarlyVoteClose(IBaseEntity controller)
    {
        var votes = GetOptionCount(controller, (int) CastVote.VOTE_OPTION1)
                    + GetOptionCount(controller, (int) CastVote.VOTE_OPTION2);

        if (votes >= _voterCount)
        {
            _modSharp.InvokeFrameAction(() => EndVote(YesNoVoteEndReason.VoteEnd_AllVotes));
        }
    }

    private void EndVote(YesNoVoteEndReason reason)
    {
        if (!_voteInProgress)
        {
            return;
        }

        _voteInProgress = false;

        _logger.LogInformation("[PanoramaVote] Vote [id:{Id}] ended: {Reason}", _voteCount, reason);

        // Cycle global vote counter (matches CPanoramaVote: wraps at 99).
        _voteCount = _voteCount == 99 ? 0 : _voteCount + 1;

        _voteHandler?.Invoke(YesNoVoteAction.VoteAction_End, (int) reason, 0);

        var controller = GetController();
        if (controller is null)
        {
            SendVoteFailed(reason);
            _controllerIndex = null;
            return;
        }

        if (_voteResult is null || reason == YesNoVoteEndReason.VoteEnd_Cancelled)
        {
            SendVoteFailed(reason);
            controller.SetNetVar("m_iActiveIssueIndex", -1);
            _controllerIndex = null;
            return;
        }

        var info = new YesNoVoteInfo
        {
            num_clients = _voterCount,
            yes_votes   = GetOptionCount(controller, (int) CastVote.VOTE_OPTION1),
            no_votes    = GetOptionCount(controller, (int) CastVote.VOTE_OPTION2),
        };
        info.num_votes = info.yes_votes + info.no_votes;

        for (var i = 0; i < _voterCount; i++)
        {
            info.clientInfo[i] = (_voters[i], GetVotesCast(controller, _voters[i]));
        }

        var passed = _voteResult(info);
        if (passed)
        {
            SendVotePassed("#SFUI_vote_passed_panorama_vote", "Vote Passed!");
        }
        else
        {
            SendVoteFailed(reason);
        }

        _controllerIndex = null;
    }

    /// <summary>
    ///     Re-draws the vote for one client so they can re-vote: clears their prior cast and
    ///     re-sends the VoteStart UM to that client.
    /// </summary>
    private bool RedrawVoteToClient(int slot)
    {
        if (!_voteInProgress)
        {
            return false;
        }

        if (GetController() is not { } controller)
        {
            return false;
        }

        var myVote = GetVotesCast(controller, slot);
        if (myVote != (int) CastVote.VOTE_UNCAST)
        {
            SetOptionCount(controller, myVote, GetOptionCount(controller, myVote) - 1);
            SetVotesCast(controller, slot, (int) CastVote.VOTE_UNCAST);
            UpdateVoteCounts(controller);
        }

        SendVoteStartUM(new RecipientFilter(new PlayerSlot((byte) slot)));

        return true;
    }

    private void UpdateVoteCounts(IBaseEntity controller)
    {
        // Firing vote_changed refreshes the panorama tally UI. The netvars alone already drive the
        // counts, so this is a nice-to-have; guard on CreateEvent returning null.
        var @event = _eventManager.CreateEvent("vote_changed", true);
        if (@event is null)
        {
            return;
        }

        @event.SetInt("vote_option1", GetOptionCount(controller, 0));
        @event.SetInt("vote_option2", GetOptionCount(controller, 1));
        @event.SetInt("vote_option3", GetOptionCount(controller, 2));
        @event.SetInt("vote_option4", GetOptionCount(controller, 3));
        @event.SetInt("vote_option5", GetOptionCount(controller, 4));
        @event.SetInt("potentialVotes", _voterCount);

        @event.Fire(false);
    }

    private void ResetController(IBaseEntity controller)
    {
        _voteHandler   = null;
        _voteResult    = null;
        _currentTitle  = string.Empty;
        _currentDetail = string.Empty;

        for (var i = 0; i < IPanoramaVoteService.MAXPLAYERS; i++)
        {
            SetVotesCast(controller, i, (int) CastVote.VOTE_UNCAST);
        }

        for (var i = 0; i < OptionCount; i++)
        {
            SetOptionCount(controller, i, 0);
        }
    }

    private void InitVoters(IReadOnlyList<int> voters)
    {
        Array.Fill(_voters, -1);

        var count = Math.Min(voters.Count, IPanoramaVoteService.MAXPLAYERS);
        for (var i = 0; i < count; i++)
        {
            _voters[i] = voters[i];
        }

        _voterCount = count;
    }

    // ── User messages ─────────────────────────────────────────────────────────

    private void SendVoteStartUM(RecipientFilter filter)
    {
        var msg = new CCSUsrMsg_VoteStart
        {
            Team        = -1,
            PlayerSlot  = _currentCaller,
            VoteType    = -1,
            DispStr     = _currentTitle,
            DetailsStr  = _currentDetail,
            IsYesNoVote = true,
        };

        _modSharp.SendNetMessage(filter, msg);
    }

    private void SendVotePassed(string dispStr, string detailsStr)
    {
        var msg = new CCSUsrMsg_VotePass
        {
            Team       = -1,
            VoteType   = 2,
            DispStr    = dispStr,
            DetailsStr = detailsStr,
        };

        _modSharp.SendNetMessage(new RecipientFilter(), msg);
    }

    private void SendVoteFailed(YesNoVoteEndReason reason)
    {
        var msg = new CCSUsrMsg_VoteFailed
        {
            Team   = -1,
            Reason = (int) reason,
        };

        _modSharp.SendNetMessage(new RecipientFilter(), msg);
    }

    // ── Recipient resolution ──────────────────────────────────────────────────

    private List<int> ResolveRecipients(RecipientFilter filter)
    {
        var slots = new List<int>();

        switch (filter.Type)
        {
            case RecipientFilterType.All:
                slots.AddRange(EnumerateEligible().Select(c => (int) c.Slot.AsPrimitive()));
                break;

            case RecipientFilterType.Team:
                slots.AddRange(EnumerateEligible()
                    .Where(c => c.GetPlayerController() is { } ctrl && ctrl.Team == filter.Team)
                    .Select(c => (int) c.Slot.AsPrimitive()));
                break;

            case RecipientFilterType.Players:
                slots.AddRange(filter.Receivers.GetClients().Select(s => (int) s.AsPrimitive()));
                break;

            case RecipientFilterType.Single:
                slots.Add((int) filter.ReceiverSlot);
                break;
        }

        return slots;
    }

    private IEnumerable<IGameClient> EnumerateEligible()
        => _clientManager.GetGameClients(inGame: true)
            .Where(c => c is { IsFakeClient: false, IsHltv: false });

    // ── Entity access (never cache the pointer) ───────────────────────────────

    private IBaseEntity? GetController()
    {
        // Index is resolved without a serial, so a recycled index could point at an unrelated
        // entity (e.g. after a stranded vote survives a map change) — verify the classname before
        // trusting the fast path, else fall through to a fresh classname search.
        if (_controllerIndex is { } idx
            && _entityManager.FindEntityByIndex(idx) is { } byIndex
            && byIndex.Classname == "vote_controller")
        {
            return byIndex;
        }

        var found = _entityManager.FindEntityByClassname(null, "vote_controller");
        _controllerIndex = found?.Index;

        return found;
    }

    private static int GetOptionCount(IBaseEntity controller, int index)
        => controller.GetNetVar<int>("m_nVoteOptionCount", (ushort) (index * 4));

    private static void SetOptionCount(IBaseEntity controller, int index, int value)
        => controller.SetNetVar("m_nVoteOptionCount", value, extraOffset: (ushort) (index * 4));

    private static int GetVotesCast(IBaseEntity controller, int slot)
        => controller.GetNetVar<int>("m_nVotesCast", (ushort) (slot * 4));

    private static void SetVotesCast(IBaseEntity controller, int slot, int value)
        => controller.SetNetVar("m_nVotesCast", value, extraOffset: (ushort) (slot * 4));

    // ── Commands ──────────────────────────────────────────────────────────────

    private void RegisterCommands()
    {
        // Open, self-service re-vote.
        _revoteCallback = (client, _) =>
        {
            if (client is not { IsInGame: true })
            {
                return ECommandAction.Skipped;
            }

            if (!_voteInProgress)
            {
                PrintNoVote(client);
                return ECommandAction.Skipped;
            }

            RedrawVoteToClient(client.Slot.AsPrimitive());

            return ECommandAction.Skipped;
        };
        _clientManager.InstallCommandCallback("revote", _revoteCallback);

        // Admin-gated cancel. Requires the AdminManager module + CommandCenter.
        var adminInterface = _sharpModuleManager
            .GetOptionalSharpModuleInterface<IAdminManager>(IAdminManager.Identity);

        if (adminInterface is not { Instance: { } adminManager })
        {
            _logger.LogWarning("[PanoramaVote] AdminManager not available — 'cancelvote' will not be registered");
            return;
        }

        adminManager.MountAdminManifest(
            ModuleIdentity,
            () => new AdminTableManifest(
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["panoramavote:admin"] = [PermManage],
                },
                [],
                []));

        var registry = adminManager.GetCommandRegistry(ModuleIdentity);
        registry.RegisterAdminCommand("cancelvote", OnCancelVoteCommand, ImmutableArray.Create(PermManage));
        registry.RegisterAdminCommand("vote",       OnVoteCommand,       ImmutableArray.Create(PermManage));
    }

    /// <summary>
    ///     Admin `vote &lt;question&gt;` — start a freeform 20-second yes/no poll shown to everyone
    ///     (e.g. "Can we slap Bob?"). The result is announced in chat; the admin acts on it. This is
    ///     the built-in driver so the plugin is usable standalone — other plugins still call the
    ///     service directly for votes wired to an automatic action.
    /// </summary>
    private void OnVoteCommand(IGameClient? issuer, StringCommand command)
    {
        var question = command.ArgString.Trim();
        if (question.Length == 0)
        {
            ReplyKey(issuer, "PanoramaVote_UsageVote", "Usage: vote <question>");
            return;
        }

        var caller = issuer is { IsInGame: true }
            ? (int) issuer.Slot.AsPrimitive()
            : IPanoramaVoteService.VOTE_CALLER_SERVER;

        var started = SendYesNoVoteToAll(
            VoteSeconds,
            caller,
            question,
            string.Empty,
            info =>
            {
                var passed = info.yes_votes > info.no_votes;
                BroadcastResult(question, passed, info.yes_votes, info.no_votes);
                return passed;
            });

        if (!started)
        {
            ReplyKey(issuer, "PanoramaVote_StartFailed", "Could not start the vote (one may already be running).");
        }
    }

    private void BroadcastResult(string question, bool passed, int yes, int no)
    {
        var key      = passed ? "PanoramaVote_Passed" : "PanoramaVote_Failed";
        var fallback = passed
            ? $"Vote PASSED: {question}  (yes {yes} / no {no})"
            : $"Vote FAILED: {question}  (yes {yes} / no {no})";

        foreach (var client in EnumerateEligible())
        {
            if (_localizer is { } localizer && !client.IsFakeClient)
            {
                localizer.For(client).Localized(key, question, yes, no).Prefix(null).Print();
            }
            else
            {
                client.Print(HudPrintChannel.Chat, fallback);
            }
        }
    }

    private void ReplyKey(IGameClient? client, string key, string fallback)
    {
        if (client is not { IsInGame: true })
        {
            _logger.LogInformation("[PanoramaVote] {Message}", fallback);
            return;
        }

        if (_localizer is { } localizer && !client.IsFakeClient)
        {
            localizer.For(client).Localized(key).Prefix(null).Print();
            return;
        }

        client.Print(HudPrintChannel.Chat, fallback);
    }

    private void OnCancelVoteCommand(IGameClient? issuer, StringCommand command)
    {
        if (!_voteInProgress)
        {
            if (issuer is { IsInGame: true })
            {
                PrintNoVote(issuer);
            }

            return;
        }

        _logger.LogInformation("[PanoramaVote] Vote cancelled by {Admin}", issuer?.Name ?? "Console");
        CancelVote();
    }

    private void PrintNoVote(IGameClient client)
    {
        if (_localizer is { } localizer && !client.IsFakeClient)
        {
            localizer.For(client).Localized("PanoramaVote_NoVoteInProgress").Prefix(null).Print();
            return;
        }

        client.Print(HudPrintChannel.Chat, "No vote is currently in progress.");
    }
}
