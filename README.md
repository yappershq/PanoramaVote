<div align="center">
  <h1><strong>PanoramaVote</strong></h1>
  <p>Server-side YES/NO votes on the native CS2 Panorama vote UI, for ModSharp plugins.</p>
</div>

<p align="center">
  <a href="https://github.com/Kxnrl/modsharp-public"><img src="https://img.shields.io/badge/framework-ModSharp-5865F2?logo=github" alt="ModSharp"></a>
  <img src="https://img.shields.io/badge/game-CS2-orange" alt="CS2">
  <img src="https://img.shields.io/github/stars/yappershq/PanoramaVote?style=flat&logo=github" alt="Stars">
</p>

---

PanoramaVote drives the built-in CS2 Panorama vote panel (top-left "F1/F2" yes/no UI) from server code — something the engine has no native API for. Other plugins call a small service to start a vote for all players or a subset, get a callback with the tally, and decide pass/fail. It's a ModSharp port of [SLAYER_PanoramaVote](https://github.com/zakriamansoor47/SLAYER_PanoramaVote).

## 🚀 Install

Copy the build output into your ModSharp install (`<sharp>` = your `sharp` directory):

| From | To |
|------|----|
| `.build/modules/PanoramaVote.Core/` | `<sharp>/modules/PanoramaVote.Core/` |
| `.build/shared/PanoramaVote.Shared/` | `<sharp>/shared/PanoramaVote.Shared/` |
| `.build/locales/panoramavote.json` | `<sharp>/locales/panoramavote.json` |

Restart the server (or change map) to load.

## 🧩 Dependencies

Uses the **ModSharp first-party modules** (ship with ModSharp): **CommandCenter** (the `cancelvote` admin command), **AdminManager** (the `panoramavote:admin:manage` permission), and **LocalizerManager** (localized command feedback). AdminManager/LocalizerManager degrade gracefully — without AdminManager the `cancelvote` command isn't registered; without LocalizerManager feedback falls back to plain English.

## ⌨️ Commands

| Command | Description | Permission |
|---------|-------------|------------|
| `vote <question>` | Start a freeform yes/no poll shown to everyone (e.g. `vote Can we slap Bob?`). Runs 20s, or ends early once everyone has voted. The tally is announced in chat; the admin acts on the result. | `panoramavote:admin:manage` |
| `revote` | Reopen the vote panel for yourself to change your vote while a vote is running. | — (any player) |
| `cancelvote` | Cancel the vote currently in progress. | `panoramavote:admin:manage` |

Admin commands are registered through **AdminManager** (permission-gated); type them in chat or console.

## 🔧 How it works

CS2's engine has **no native yes/no vote** — the string `"YES/NO votes aren't currently supported"` is literally compiled into `libserver`. So PanoramaVote drives the UI directly: it writes the `vote_controller` entity netvars (`m_iActiveIssueIndex`, `m_nVoteOptionCount`, `m_nPotentialVotes`, `m_bIsYesNoVote`, `m_nVotesCast`), sends the three vote user-messages (`VoteStart` / `VotePass` / `VoteFailed`), and listens to the `vote_cast` game event to tally as players vote. The engine still processes incoming votes server-side, so the counts stay correct. No gamedata signatures — it's entirely managed API, so it survives game updates.

## 🧩 Public API

Other plugins consume `IPanoramaVoteService` (resolve in `OnAllModulesLoaded`):

```csharp
var vote = sharpModuleManager
    .GetOptionalSharpModuleInterface<IPanoramaVoteService>(IPanoramaVoteService.Identity)?.Instance;

vote?.SendYesNoVoteToAll(
    duration: 20f,
    caller:   IPanoramaVoteService.VOTE_CALLER_SERVER,
    title:    "#SFUI_vote_changelevel",   // a translation token
    detail:   "de_dust2",
    result:   info => info.yes_votes > info.no_votes,   // return true = "vote passed"
    handler:  (action, p1, p2) => { /* optional: start / per-vote / end */ });
```

`SendYesNoVote(...)` takes a `RecipientFilter` to scope the vote to a team or specific players; `CancelVote()` ends the current one; `IsVoteInProgress` guards against overlap.

## 📦 Build

```bash
dotnet build -c Release
```

Outputs `.build/modules/PanoramaVote.Core/PanoramaVote.Core.dll` and `.build/shared/PanoramaVote.Shared/PanoramaVote.Shared.dll`.

## 🙏 Credits

Port of [zakriamansoor47/SLAYER_PanoramaVote](https://github.com/zakriamansoor47/SLAYER_PanoramaVote) by SLAYER.

---

<div align="center">
  <p>Made with ❤️ by <a href="https://github.com/yappershq">yappershq</a></p>
  <p>⭐ Star this repo if you find it useful!</p>
</div>
