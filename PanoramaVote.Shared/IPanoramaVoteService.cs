namespace PanoramaVote.Shared;

using System;
using Sharp.Shared.Types;

/// <summary>Raised on menu actions: start / per-vote / end. See <see cref="YesNoVoteAction" />.</summary>
public delegate void YesNoVoteHandler(YesNoVoteAction action, int param1, int param2);

/// <summary>Called when a vote finishes; return <c>true</c> to broadcast "vote passed".</summary>
public delegate bool YesNoVoteResult(YesNoVoteInfo info);

/// <summary>
///     Drives the CS2 Panorama (top-left) YES/NO vote UI. Published as a ModSharp module
///     interface so other plugins can start server-side votes.
/// </summary>
public interface IPanoramaVoteService
{
    static string Identity => typeof(IPanoramaVoteService).FullName ?? nameof(IPanoramaVoteService);

    /// <summary>Pass this as the caller slot to show the vote as coming from "Server".</summary>
    const int VOTE_CALLER_SERVER = 99;

    /// <summary>Maximum number of player slots.</summary>
    const int MAXPLAYERS = 64;

    /// <summary>Whether a vote is currently running.</summary>
    bool IsVoteInProgress { get; }

    /// <summary>
    ///     Start a YES/NO vote for the players described by <paramref name="filter" />.
    /// </summary>
    /// <param name="duration">Maximum time to leave the vote active, in seconds.</param>
    /// <param name="caller">Player slot of the caller, or <see cref="VOTE_CALLER_SERVER" /> for 'Server'.</param>
    /// <param name="title">Translation token used as the vote message.</param>
    /// <param name="detail">Extra string used by some vote translation strings.</param>
    /// <param name="filter">Recipients allowed to participate in the vote.</param>
    /// <param name="result">Called when the vote finishes; return value drives pass/fail UI.</param>
    /// <param name="handler">Optional per-action callback (start / vote / end).</param>
    /// <returns><c>true</c> if the vote was successfully started.</returns>
    bool SendYesNoVote(float           duration,
        int                            caller,
        string                         title,
        string                         detail,
        RecipientFilter                filter,
        YesNoVoteResult                result,
        YesNoVoteHandler?              handler = null);

    /// <summary>Start a YES/NO vote for all valid (non-bot, non-HLTV) in-game players.</summary>
    bool SendYesNoVoteToAll(float      duration,
        int                            caller,
        string                         title,
        string                         detail,
        YesNoVoteResult                result,
        YesNoVoteHandler?              handler = null);

    /// <summary>Cancel the current vote, if any.</summary>
    void CancelVote();
}
