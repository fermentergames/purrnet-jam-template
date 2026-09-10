# PurrNet Exercises

Small, verifiable exercises to learn PurrNet before the jam. Each one builds on the last.

| # | File | Teaches |
|---|------|---------|
| E1 | `E1_SyncVarCounter.cs` | `SyncVar<T>` — server writes, clients receive automatically |
| E2 | `E2_Spawner.cs` + `E2_MovableCube.cs` | `[ServerRpc]` + spawning a networked prefab + `NetworkTransform` |
| E3 | `E3_AnswerBoard.cs` | `SyncList<T>` + `[ObserversRpc]` reveal |
| E4 | `E4_GameFlow.cs` | `SyncTimer` round countdown + phase `SyncVar` |

## How to run E1

1. Open `Assets/Scenes/MainGame.unity`.
2. In the Hierarchy, right-click → Create Empty, name it `E1 Counter`.
3. Add Component → `E1_SyncVarCounter`.
4. Press Play. The Editor auto-starts host + client (server + local client), so you'll see the counter increment in the Console.
5. To see client-side sync: File → Build Settings → Windows → Build. Run the build next to the Editor. The client connects to `127.0.0.1:5000` and logs the same counter values.

## Why this works without any setup

`Assets/Externals/GameJam/Prefabs/SessionManager Jam.prefab` contains the `NetworkManager` with:
- `_startServerFlags: 9` = `Editor(1) + ServerBuild(8)`
- `_startClientFlags: 7` = `Editor(1) + Clone(2) + ClientBuild(4)`

So: Editor Play = host + local client. A client build (or a ParrelSync clone) = client only, connecting to `127.0.0.1:5000` over `UDPTransport`. No PurrNet account needed for this path — the lobby/relay flow is only needed for online play.

## How to run E2

1. In `MainGame`, create a cube: GameObject → 3D Object → Cube.
2. Select it and add: `NetworkIdentity`, `NetworkTransform` (Owner Auth is on by default), `E2_MovableCube`.
3. Drag it from the Hierarchy into `Assets/Prefabs/` to create a prefab, then delete the scene cube.
4. Select `Assets/Scenes/NetworkPrefabs.asset` and confirm the cube is listed (it auto-generates).
5. Create an empty `E2 Spawner` in `MainGame`, add `E2_Spawner`, and assign the cube prefab to its `Cube Prefab` field.
6. Play in the Editor (host) + run the client build. Press **Space** on either — a cube spawns, owned by whoever pressed. Move it with **WASD**; the other window sees it move.

## How to run E3

1. Create an empty `E3 Answer Board` in `MainGame`, add `E3_AnswerBoard`.
2. Play in the Editor (host) + run the client build.
3. Press **1-4** on each window to submit canned answers.
4. Watch the Console: each answer appears on **both** windows (the `SyncList` syncs), then a `REVEAL` line fires on both once 2 answers are in.

## How to run E4

1. Create an empty `E4 Game Flow` in `MainGame`, add `E4_GameFlow`.
2. Play in the Editor (host) + run the client build.
3. Watch both Consoles: the phase cycles `Prompt -> Answering -> Reveal -> Prompt` with a countdown, all driven by the server and synced to the client.