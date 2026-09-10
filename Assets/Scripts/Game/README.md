# Party Game Prototype (Quiplash-style)

A minimal but complete party game loop built on the PurrNet template:

**Waiting → Answer → Reveal → Vote → Scores → (repeat) → Final**

- Server-authoritative: the host's `GameFlow` owns all state (phase, round, prompt, answers, votes, scores) and syncs it to clients.
- Clients send intents via `[ServerRpc]` and render the synced state.
- Uses the template's existing lobby flow (join code, ready-up, scene handoff) and `GameOverBroadcaster` to return to the menu.

## Files

| File | Purpose |
|------|---------|
| `GameFlow.cs` | Server-authoritative state machine + `[ServerRpc]` intents |
| `GameUI.cs` | Builds the UI at runtime (no editor UI assembly needed) |
| `PromptDeck.cs` | ScriptableObject for prompts (theme-agnostic, swappable) |

## Setup (in Unity)

1. Open `Assets/Scenes/MainGame.unity`.
2. In the Hierarchy, right-click → **Create Empty**, name it `GameFlow`.
3. Add Component → **GameFlow**.
4. Add Component → **GameUI** (it auto-finds the GameFlow on the same object).
5. *(Optional)* Create a prompt deck: right-click in Project → **Create → Jam → Prompt Deck**, add prompts, and drag it onto the GameFlow's `Deck` field. If you skip this, built-in prompts are used.
6. **Save the scene** (Ctrl+S).

## Test

1. Play in the Editor (host) + run the client build (Development Build).
2. Client joins the lobby by code → both load `MainGame`.
3. The game auto-starts when 2 players are connected (or the host clicks **Start Game** on the waiting screen).
4. **Answer**: type an answer, hit Submit. (30s)
5. **Reveal**: everyone sees all answers. (6s)
6. **Vote**: click Vote next to the funniest answer (not your own). (30s)
7. **Scores**: see the standings + best answer. (8s)
8. After 3 rounds → **Final** → winner → everyone returns to the menu.

## Tuning

All timing and round count are serialized fields on `GameFlow`:
- `Answer Time`, `Reveal Time`, `Vote Time`, `Scoreboard Time`, `Final Time`
- `Max Rounds`

## Known simplifications (fine for a prototype)

- Player names come from the lobby display names (each client sends its own on spawn); falls back to "Player 1/2" if there's no lobby (e.g., direct MainGame testing).
- Answers are shown with the author's index during reveal (not fully anonymous).
- No "no answer" penalty; players who don't answer just show "(no answer)".
- If the host disconnects, the game ends (host migration is disabled in the template).