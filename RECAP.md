# Project Recap — Jackbox Jam Party Game

**Date:** 2026-09-10 (pre-jam prep)
**Jam:** Jackbox Multiplayer Jam (jamzo.com/jams/jackbox) — kickoff Sep 12, submissions end Sep 16
**Goal:** A Quiplash-style party game (text answers + voting), with a Drawful-style drawing mechanic as a stretch goal.

---

## 1. What this project is

A Unity 6 + PurrNet multiplayer party game built on the official PurrNet jam template.
The template already provides: lobby flow (join codes, ready-up, scene handoff), pause menu,
game-over handling, and relay networking. We added the actual game on top.

**Current state:** A working text-based party game loop + a standalone drawing-mechanic prototype.

---

## 2. Environment & setup

- **Unity:** 6000.6.0f1 (must match — the template pins this version)
- **Render pipeline:** URP 17.6.0 · **Input:** new Input System (activeInputHandler: 1)
- **Networking packages** (git deps in `Packages/manifest.json`):
  - `dev.purrnet.purrnet` v1.23.0-beta.41
  - `dev.purrnet.services` v1.1.2 (PurrServices backend)
  - `dev.purrnet.ui` v1.6.0-beta.1
  - `dev.purrnet.purrlobby` (embedded, 1.1.0-beta.2)
  - `com.veriorpies.parrelsync` (multi-editor testing)
- **PurrServices:** linked (config in `ProjectSettings/ApplicationConstants.json`) — needed for the online lobby/relay flow.
- **WebGL module:** installed. WebGL is the jam's recommended build target.
- **.NET SDK 10.0.401** installed (for VS Code C# IntelliSense).

---

## 3. What's built

### A. Learning exercises (`Assets/Scripts/Exercises/`)
| File | Teaches |
|------|---------|
| `E1_SyncVarCounter.cs` | `SyncVar<T>` — server writes, clients receive |
| `E2_Spawner.cs` + `E2_MovableCube.cs` | `[ServerRpc]`, networked spawn, `NetworkTransform` |
| `E3_AnswerBoard.cs` | `SyncList<T>` + `[ObserversRpc]` reveal |
| `E4_GameFlow.cs` | `SyncTimer` + phase `SyncVar` |

### B. Party game prototype (`Assets/Scripts/Game/`)
- **`GameFlow.cs`** — server-authoritative state machine:
  `Waiting → Prompt → Reveal → Vote → Scoreboard → Final → back to menu`
  - `SyncVar` phase/round/prompt, `SyncList` names/scores/answers/votes/hasVoted, `SyncTimer`
  - `[ServerRpc] SubmitAnswer(string)` / `SubmitVote(int)` (server validates)
  - Real lobby display names (each client sends its own on spawn)
  - Auto-start at 2 players, or host clicks Start
  - Ends via `GameOverBroadcaster.EndGame()`
- **`GameUI.cs`** — builds the whole UI at runtime (legacy uGUI, no TMP dependency):
  top bar with phase + countdown, answer input (Enter submits), submissions list,
  reveal, vote buttons, scoreboard, final.
- **`PromptDeck.cs`** — ScriptableObject for prompts (theme-agnostic; swap at kickoff).
  Sample deck at `Assets/Content/Prompts/PromptDeck.asset`.

### C. Drawing mechanic prototype (`Assets/Scripts/Game/`)
- **`DrawingCanvas.cs`** — reusable draw UI: RawImage + 256×256 Texture2D, mouse brush,
  `Clear()`, `GetPngBytes()`, `LoadImage(byte[])` (draw on top of a loaded drawing),
  ignores clicks on other UI buttons.
- **`DrawingBoard.cs`** — NetworkBehaviour prototype: draw → Submit → server broadcasts
  to all clients → reveal row of thumbnails. Clicking a thumbnail loads it into your
  canvas to continue drawing on top.

---

## 4. Key files (template-provided, don't break)

- `Assets/Scenes/MainMenu.unity` — lobby/menu entry (`LobbyManager Jam`)
- `Assets/Scenes/MainGame.unity` — game scene (`SessionManager Jam` = NetworkManager + pause menu; `Game Flow` object = our game)
- `Assets/Scenes/NetworkPrefabs.asset` — auto-generating networked prefab registry
- `Assets/Externals/GameJam/Orchestrator/*.asset` — lobby/orchestrator config
- `Packages/dev.purrnet.purrlobby/Prefabs/Lobby.Rules.asset` — NetworkRules (auth config)
- `Packages/dev.purrnet.purrlobby/Providers/PurrNet/PurrTransportGameAllocator.cs` — relay swap reference

---

## 5. How to test

### Local multiplayer (no builds) — ParrelSync
1. Open the project in Unity (main editor).
2. `ParrelSync → Clones Manager → Create new clone` (folder `purrnet-jam-template_clone_0`, gitignored).
3. Open the clone in Unity Hub (same Unity version; first open is slow).
4. Main editor: open `MainGame`, press **Play** → host + client.
5. Clone: open `MainGame`, press **Play** → auto-connects as client to `127.0.0.1:5000`.
6. The game auto-starts at 2 players.

### Online (lobby + relay)
1. Play `MainMenu` → device login → Create Lobby → share join code.
2. Others join by code → ready up → countdown → all load `MainGame` over the relay.
3. WebGL build: File → Build Settings → WebGL → Build → publish via Unity Play.

### Drawing prototype
- Add `DrawingBoard` to an empty GO in `MainGame`; temporarily disable the `Game Flow`
  object so only the drawing UI shows. Draw → Submit → both windows show both drawings.

---

## 6. Key lessons (PurrNet gotchas — IMPORTANT)

1. **`GameOverBroadcaster.TryGet` is `internal`** — use `FindAnyObjectByType<GameOverBroadcaster>().EndGame()` instead.
2. **Parallel SyncLists sync as separate messages** — they can be momentarily different
   lengths on clients; always bounds-check before indexing.
3. **PurrNet codegen rejects `byte[]` and `ByteData` as RPC params** (and `SyncList<byte[]>`)
   — the NetworkBehaviour silently fails to register ("no monobehavior scripts in the file",
   re-import loop, no C# error). **Use base64 `string` for byte payloads.** A Unity restart
   may be needed to clear the stale re-import state.
4. **`SyncTextureFile` uses file I/O** — breaks in WebGL (no file system). Use PNG bytes via RPC.
5. **Use `isServer` not `asServer`** in player join/leave handlers — during server teardown
   `asServer` can be true while `isServer` is false (causes SyncList permission errors).
6. **Unity C# is 7.3** — no target-typed `new()`.
7. **`StartFlags`**: main editor = host (Editor flag), ParrelSync clone = client only (Clone flag).
8. **Don't index your own state off `networkManager.players`** (it mutates mid-event) — track your own mapping.

---

## 7. Git state & transferring to another computer

**Branches:**
- `baseline` — template as cloned (rollback point)
- `dev` — upstream template (don't touch)
- `game` — **current work** (HEAD = `de9cb03`)

**Commits:**
- `de9cb03` drawing game prototype added too
- `bb5593a` Party-game prototype + ParrelSync + exercises
- `efa6c8e` initial testing / learning proto
- `11262fc` baseline

**To transfer to a new computer:**
1. Commit any pending changes, then `git push origin game`.
2. On the new machine: `git clone <remote-url>` and `git checkout game`.
3. Open in Unity 6000.6.0f1 (first open resolves packages — needs Git + internet).
4. **Recreate the ParrelSync clone** on the new machine (it's gitignored, so it doesn't transfer).
5. PurrServices config transfers automatically (`ProjectSettings/ApplicationConstants.json` is in the repo).

**Note:** The chat/session context is NOT in git. This `RECAP.md` is the transferable summary.

---

## 8. Next steps (Phase F — polish + jam readiness)

1. **Sound (20% of score)** — game is silent. Add SFX (submit/vote/reveal/countdown) + music.
2. **Art (20%)** — reuse `JamTheme` palette + template art; nicer panels/title screen.
3. **Integrate drawing into the game flow** — swap the text input for `DrawingCanvas` during
   the answer phase; vote on drawings.
4. **WebGL release build** — smaller/faster (release + Brotli compression + managed stripping).
5. **Kickoff (Sep 12)** — theme revealed; create a theme-specific `PromptDeck` and swap it in.
6. **Submission** — list any pre-made assets + disclose AI usage in the description.