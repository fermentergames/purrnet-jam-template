using System;
using System.Collections.Generic;
using PurrNet;
using PurrNet.Lobby;
using UnityEngine;

namespace Jam
{
    public enum MovingPhase
    {
        Waiting,    // waiting for players
        Tutorial,   // quick how-to-play screen (host advances)
        Drawing,    // all players draw simultaneously on their own canvas (~30s)
        Guessing,   // canvases revealed one-by-one; others guess
        Scoreboard, // show scores
        Final       // game over, show winner + gallery
    }

    /// <summary>
    /// Moving Canvas — a motion-themed Pictionary game.
    ///
    /// Each player draws on their OWN canvas simultaneously while the canvas
    /// physically moves (sway/spin/shake/zoom). Then canvases are revealed one-by-one
    /// and the other players guess the prompt word. Guesses are shown live (the
    /// correct one is redacted so players can't copy it). Scoring is speed-tiered for
    /// guessers, and the artist earns points per correct guesser.
    ///
    /// The server owns ALL state and broadcasts it via SyncVars/SyncLists. Clients
    /// only send intents ([ServerRpc] SubmitDrawing / SubmitGuess) and render the
    /// synced state. Movement is purely local (each player draws on their own canvas).
    ///
    /// Setup:
    ///   1. Create an empty "MovingCanvasGame" in the game scene, add this component.
    ///   2. (Optional) assign a MovingPromptDeck; otherwise built-in prompts are used.
    ///   3. Add the MovingCanvasUI component to the same object (or a child).
    /// </summary>
    public class MovingCanvasGame : NetworkBehaviour
    {
        [Header("Timing (seconds)")]
        [SerializeField] private float _drawTime = 30f;
        [SerializeField] private float _guessTime = 30f;   // per revealed canvas
        [SerializeField] private float _scoreboardTime = 8f;
        [SerializeField] private float _finalTime = 12f;
        [Tooltip("Delay before the draw timer starts, so the intro (prompt + countdown) can play first.")]
        [SerializeField] private float _introTime = 4f;

        [Header("Rounds")]
        [SerializeField] private int _maxRounds = 3;
        [SerializeField] private MovementMode[] _roundModes =
        {
            MovementMode.Sway,
            MovementMode.Spin,
            MovementMode.Shake
        };

        [Header("Testing")]
        [Tooltip("Allow starting with a single player so you can tune drawing/movement in isolation.")]
        [SerializeField] private bool _allowSinglePlayer = false;

        [Header("Content")]
        [SerializeField] private MovingPromptDeck _deck;

        [Header("Movement (tuning — applied to the canvas mover at runtime)")]
        [Tooltip("Optional ScriptableObject config. If assigned, its values are used (and persist live edits in play mode). Leave empty to use the inline fields below.")]
        [SerializeField] private MovementConfig _movementConfig;
        public MovementParams sway = new MovementParams { baseAmplitude = 120f, baseFrequency = 1.5f };
        public MovementParams wobble = new MovementParams { baseAmplitude = 18f, baseFrequency = 1.2f };
        public MovementParams shake = new MovementParams { baseAmplitude = 20f, baseFrequency = 40f };
        [Tooltip("baseAmplitude = total degrees to rotate clockwise. amplitudeCurve controls rotation speed.")]
        public MovementParams spin = new MovementParams { baseAmplitude = 1080f, baseFrequency = 1f };
        [Tooltip("Zoom back and forth. baseAmplitude = how much to shrink (1 - minScale). baseFrequency = oscillation speed. amplitudeCurve scales the zoom amount over the round.")]
        public MovementParams zoom = new MovementParams { baseAmplitude = 0.65f, baseFrequency = 1f };

        // --- Synced state (server-authoritative) ---
        private readonly SyncVar<MovingPhase> _phase = new SyncVar<MovingPhase>(MovingPhase.Waiting);
        private readonly SyncVar<int> _round = new SyncVar<int>(0);
        private readonly SyncVar<int> _roundReady = new SyncVar<int>(0);
        private readonly SyncVar<int> _movementMode = new SyncVar<int>((int)MovementMode.None);
        private readonly SyncVar<int> _guessCanvasIndex = new SyncVar<int>(0);
        private readonly SyncVar<string> _roundPrompts = new SyncVar<string>(""); // atomic: all prompts for the round
        private readonly SyncVar<int> _drawReady = new SyncVar<int>(-1); // incremented to signal clients to ack drawing receipt
        private readonly SyncVar<int> _drawRestart = new SyncVar<int>(0); // incremented when the drawing phase is restarted (tuning)
        private readonly SyncVar<string> _revealPrompt = new SyncVar<string>(""); // correct prompt shown at end of a canvas's guess phase
        private readonly SyncVar<int> _roundReadySignal = new SyncVar<int>(0); // incremented to ask clients to ack round readiness
        private readonly SyncVar<string> _finalScoresSync = new SyncVar<string>(""); // snapshot of final scores for the game-over screen

        // Player state is kept in ONE atomic SyncVar<string> (encoded) instead of
        // several SyncLists. SyncLists can be left empty on a client if the server
        // broadcasts changes before that client has spawned/observed the object; an
        // atomic SyncVar is delivered reliably as initial state, so the player list
        // always reaches every client.
        private readonly SyncVar<string> _playerListSync = new SyncVar<string>("");
        private readonly List<string> _playerNames = new List<string>();
        private readonly List<int> _scores = new List<int>();
        private readonly List<int> _playerColor = new List<int>();
        private readonly List<int> _playerIcon = new List<int>();
        private int _localIndex = -1; // client-side: this client's index (set by the server)
        private string _lastParsedPlayerList = ""; // last encoded player list we parsed into the mirrors

        // Per-canvas state (canvas k = player k's drawing).
        private readonly SyncList<string> _canvasStrokes = new SyncList<string>(); // round-tagged stroke JSON
        private readonly SyncList<bool> _canvasSubmitted = new SyncList<bool>();

        // Guesses (flat parallel lists).
        private readonly SyncList<int> _guessCanvas = new SyncList<int>();
        private readonly SyncList<int> _guessPlayer = new SyncList<int>();
        private readonly SyncList<string> _guessText = new SyncList<string>();
        private readonly SyncList<bool> _guessCorrect = new SyncList<bool>();
        private readonly SyncList<int> _guessOrder = new SyncList<int>(); // 0 = not correct, else 1st/2nd/3rd...

        // Archive of finished canvases for the game-over gallery.
        private readonly SyncList<string> _archiveEntry = new SyncList<string>();

        private readonly SyncTimer _timer = new SyncTimer();

        // Server-only: maps each player to their index in the parallel lists.
        private readonly Dictionary<PlayerID, int> _playerIndex = new Dictionary<PlayerID, int>();

        // Server-only: names that arrived before the player was registered.
        private readonly Dictionary<PlayerID, string> _pendingNames = new Dictionary<PlayerID, string>();

        // Server-only: handshake state for the drawing -> guessing transition.
        private readonly HashSet<PlayerID> _ackedClients = new HashSet<PlayerID>();
        private int _lastAckedDrawReady = -1; // client-side: last handshake value we acked

        // Server-only: clients that have confirmed they spawned and are ready to receive state.
        private readonly HashSet<PlayerID> _readyClients = new HashSet<PlayerID>();

        // Server-only: clients that have acked round readiness.
        private readonly HashSet<PlayerID> _roundAckedClients = new HashSet<PlayerID>();
        // Client-side: last round-ready signal we acked, and the round of the parsed prompts.
        private int _lastAckedRoundReady = -1;
        private int _roundPromptsRound = 0;

        // Server-only: canvases that have a delayed auto-advance scheduled.
        private readonly HashSet<int> _advanceScheduled = new HashSet<int>();

        // Client-side: parsed prompts from _roundPrompts.
        private readonly List<string> _prompts = new List<string>();

        // Server-only: prompts already used this game (no repeats across rounds).
        private readonly HashSet<string> _usedPrompts = new HashSet<string>();

        private const char StrokeRoundSep = '\u0003';
        private const char ArchiveFieldSep = '\u0004';
        private const char PlayerFieldSep = '\u0005';
        private const char PlayerSep = '\u0006';

        /// <summary>Fired whenever any synced state changes, so the UI can refresh.</summary>
        public event Action onStateChanged;

        /// <summary>Fired on the local client when a timer ends (so the UI can auto-submit).</summary>
        public event Action onLocalTimerEnd;

        // --- Read-only accessors for the UI ---
        public MovingPhase Phase => _phase.value;
        public int Round => _round.value;
        public int TimeRemaining => _timer.remainingInt;
        public float TimeRemainingFloat => _timer.remaining;
        public float DrawTime => _drawTime;
        public float IntroTime => _introTime;
        public int PlayerCount => _playerNames.Count;
        public int CanvasCount => _canvasStrokes.Count;
        public int CurrentGuessCanvas => _guessCanvasIndex.value;
        public MovementMode CurrentMovementMode => (MovementMode)_movementMode.value;
        public bool IsHost => isServer;
        public bool AllowSinglePlayer => _allowSinglePlayer;
        public int DrawRestart => _drawRestart.value;
        public string RevealPrompt => _revealPrompt.value;
        public string FinalScores => _finalScoresSync.value;
        public bool IsTimerRunning => _timer.isRunning;
        public float PhaseTotalTime
        {
            get
            {
                switch (_phase.value)
                {
                    case MovingPhase.Drawing: return _drawTime;
                    case MovingPhase.Guessing: return _guessTime;
                    case MovingPhase.Scoreboard: return _scoreboardTime;
                    case MovingPhase.Final: return _finalTime;
                    default: return 1f;
                }
            }
        }

        /// <summary>Copy the editable movement config onto a CanvasMover (called by the UI at setup).</summary>
        public void ApplyMovementConfig(CanvasMover mover)
        {
            if (mover == null)
                return;
            if (_movementConfig != null)
            {
                mover.sway = _movementConfig.sway;
                mover.wobble = _movementConfig.wobble;
                mover.shake = _movementConfig.shake;
                mover.spin = _movementConfig.spin;
                mover.zoom = _movementConfig.zoom;
            }
            else
            {
                mover.sway = sway;
                mover.wobble = wobble;
                mover.shake = shake;
                mover.spin = spin;
                mover.zoom = zoom;
            }
        }

        public IReadOnlyList<string> PlayerNames => _playerNames;
        public IReadOnlyList<int> Scores => _scores;
        public IReadOnlyList<int> PlayerColor => _playerColor;
        public IReadOnlyList<int> PlayerIcon => _playerIcon;
        public IReadOnlyList<string> GuessText => _guessText.list;
        public IReadOnlyList<int> GuessCanvas => _guessCanvas.list;
        public IReadOnlyList<int> GuessPlayer => _guessPlayer.list;
        public IReadOnlyList<bool> GuessCorrect => _guessCorrect.list;
        public IReadOnlyList<int> GuessOrder => _guessOrder.list;

        public int ArchiveCount => _archiveEntry.Count;
        public string ArchivePrompt(int i) => ArchiveField(i, 1);
        public int ArchiveRound(int i) => int.TryParse(ArchiveField(i, 0), out var r) ? r : 0;
        public int ArchiveCanvas(int i) => int.TryParse(ArchiveField(i, 2), out var c) ? c : 0;
        public string ArchiveStrokes(int i) => ArchiveField(i, 3);

        private string ArchiveField(int i, int field)
        {
            if (i < 0 || i >= _archiveEntry.Count)
                return "";
            var parts = _archiveEntry[i].Split(ArchiveFieldSep);
            return field < parts.Length ? parts[field] : "";
        }

        /// <summary>Index of the local player in the parallel lists, or -1.</summary>
        public int LocalPlayerIndex
        {
            get
            {
                if (_localIndex >= 0)
                    return _localIndex;
                // Server/host: derive from the player index map.
                var local = networkManager.localPlayer;
                return _playerIndex.TryGetValue(local, out var idx) ? idx : -1;
            }
        }

        /// <summary>True once the current round's canvas data has fully synced to this client.</summary>
        public bool IsRoundReady
        {
            get
            {
                if (_roundReady.value != _round.value)
                    return false;
                if (_roundPromptsRound != _round.value)
                    return false;
                var n = _playerNames.Count;
                if (n == 0)
                    return false;
                if (_canvasStrokes.Count != n || _canvasSubmitted.Count != n)
                    return false;
                // Verify every stroke entry is tagged for the current round (avoids stale data).
                for (var i = 0; i < n; i++)
                    if (StrokeEntryRound(_canvasStrokes[i]) != _round.value)
                        return false;
                return true;
            }
        }

        /// <summary>Prompt word for a canvas, from the atomically-synced round prompts.</summary>
        public string GetPrompt(int canvas)
        {
            return (canvas >= 0 && canvas < _prompts.Count) ? _prompts[canvas] : "";
        }

        /// <summary>Stroke JSON for a canvas, but ONLY if it belongs to the current round.</summary>
        public string GetCanvasStrokes(int canvas)
        {
            if (canvas < 0 || canvas >= _canvasStrokes.Count)
                return "";
            var entry = _canvasStrokes[canvas];
            if (StrokeEntryRound(entry) != _round.value)
                return "";
            var sep = entry.IndexOf(StrokeRoundSep);
            return sep >= 0 && sep + 1 < entry.Length ? entry.Substring(sep + 1) : "";
        }

        /// <summary>True if the local player is the artist of the given canvas.</summary>
        public bool IsArtist(int canvas) => LocalPlayerIndex == canvas;

        // ---------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------

        protected override void OnSpawned(bool asServer)
        {
            if (networkManager)
            {
                networkManager.onPlayerJoined += OnPlayerJoined;
                networkManager.onPlayerLeft += OnPlayerLeft;
            }

            if (asServer)
            {
                var players = networkManager.players;
                for (var i = 0; i < players.Count; i++)
                    AddPlayer(players[i]);
            }
            else
            {
                // Tell the server we've spawned and are ready to receive state, so it
                // won't start the game (and broadcast the phase change) before we're
                // listening. Without this, a phase change sent during the spawn race
                // can be lost, leaving a client stuck on the waiting screen.
                ClientReadyRpc();

                // Seed the player-list mirrors from the SyncVar's current value. PurrNet
                // delivers the initial SyncVar state before OnSpawned but may not fire
                // onChanged for it, so without this a client would show an empty list.
                EnsurePlayerStateParsed();
            }

            var name = GetLocalDisplayName();
            if (!string.IsNullOrWhiteSpace(name))
                RegisterNameRpc(name);
        }

        protected override void OnDespawned(bool asServer)
        {
            if (networkManager)
            {
                networkManager.onPlayerJoined -= OnPlayerJoined;
                networkManager.onPlayerLeft -= OnPlayerLeft;
            }
        }

        private void OnEnable()
        {
            _phase.onChanged += OnSyncVarChanged<MovingPhase>;
            _round.onChanged += OnSyncVarChanged<int>;
            _roundReady.onChanged += OnSyncVarChanged<int>;
            _movementMode.onChanged += OnSyncVarChanged<int>;
            _guessCanvasIndex.onChanged += OnSyncVarChanged<int>;
            _roundPrompts.onChanged += OnRoundPromptsChanged;
            _drawReady.onChanged += OnDrawReadyChanged;
            _drawRestart.onChanged += OnSyncVarChanged<int>;
            _revealPrompt.onChanged += OnSyncVarChanged<string>;
            _roundReadySignal.onChanged += OnRoundReadySignalChanged;
            _finalScoresSync.onChanged += OnSyncVarChanged<string>;
            _playerListSync.onChanged += OnPlayerListChanged;
            _canvasStrokes.onChanged += OnSyncListChanged<string>;
            _canvasSubmitted.onChanged += OnSyncListChanged<bool>;
            _guessCanvas.onChanged += OnSyncListChanged<int>;
            _guessPlayer.onChanged += OnSyncListChanged<int>;
            _guessText.onChanged += OnSyncListChanged<string>;
            _guessCorrect.onChanged += OnSyncListChanged<bool>;
            _guessOrder.onChanged += OnSyncListChanged<int>;
            _archiveEntry.onChanged += OnSyncListChanged<string>;
            _timer.onTimerSecondTick += OnTimerTick;
            _timer.onTimerEnd += OnLocalTimerEnd; // submit first...
            _timer.onTimerEnd += OnTimerEnd;      // ...then advance phase

            // Seed the player-list mirrors in case the SyncVar value already arrived
            // (e.g. on a re-enable) without firing onChanged.
            EnsurePlayerStateParsed();
        }

        private void OnDisable()
        {
            _phase.onChanged -= OnSyncVarChanged<MovingPhase>;
            _round.onChanged -= OnSyncVarChanged<int>;
            _roundReady.onChanged -= OnSyncVarChanged<int>;
            _movementMode.onChanged -= OnSyncVarChanged<int>;
            _guessCanvasIndex.onChanged -= OnSyncVarChanged<int>;
            _roundPrompts.onChanged -= OnRoundPromptsChanged;
            _drawReady.onChanged -= OnDrawReadyChanged;
            _drawRestart.onChanged -= OnSyncVarChanged<int>;
            _revealPrompt.onChanged -= OnSyncVarChanged<string>;
            _roundReadySignal.onChanged -= OnRoundReadySignalChanged;
            _finalScoresSync.onChanged -= OnSyncVarChanged<string>;
            _playerListSync.onChanged -= OnPlayerListChanged;
            _canvasStrokes.onChanged -= OnSyncListChanged<string>;
            _canvasSubmitted.onChanged -= OnSyncListChanged<bool>;
            _guessCanvas.onChanged -= OnSyncListChanged<int>;
            _guessPlayer.onChanged -= OnSyncListChanged<int>;
            _guessText.onChanged -= OnSyncListChanged<string>;
            _guessCorrect.onChanged -= OnSyncListChanged<bool>;
            _guessOrder.onChanged -= OnSyncListChanged<int>;
            _archiveEntry.onChanged -= OnSyncListChanged<string>;
            _timer.onTimerSecondTick -= OnTimerTick;
            _timer.onTimerEnd -= OnLocalTimerEnd;
            _timer.onTimerEnd -= OnTimerEnd;
        }

        private void OnSyncVarChanged<T>(T _) => onStateChanged?.Invoke();
        private void OnSyncListChanged<T>(SyncListChange<T> _) => onStateChanged?.Invoke();
        private void OnTimerTick() => onStateChanged?.Invoke();
        private void OnLocalTimerEnd()
        {
            SplitLog.Log($"[MovingCanvasGame] onLocalTimerEnd fired, phase={_phase.value}");
            onLocalTimerEnd?.Invoke();
        }

        private void OnRoundPromptsChanged(string _)
        {
            _prompts.Clear();
            var data = _roundPrompts.value;
            if (string.IsNullOrEmpty(data))
                return;
            var parts = data.Split('\u0002');
            if (parts.Length < 2)
                return;
            _roundPromptsRound = int.TryParse(parts[0], out var r) ? r : 0;
            for (var i = 1; i < parts.Length; i++)
                _prompts.Add(parts[i]);
        }

        private void OnPlayerListChanged(string _)
        {
            ParsePlayerList(_playerListSync.value);
            onStateChanged?.Invoke();
        }

        /// <summary>
        /// Re-parse the authoritative player list SyncVar into the local mirror lists.
        /// PurrNet delivers a SyncVar's initial value to a newly-spawned client WITHOUT
        /// firing onChanged (Packer.Transform no-ops when the value equals the client's
        /// default), so we must seed the mirrors from the current value rather than rely
        /// solely on the change event. Idempotent; fires onStateChanged only when the
        /// encoded value actually changed.
        /// </summary>
        public void EnsurePlayerStateParsed()
        {
            var data = _playerListSync.value;
            if (data == _lastParsedPlayerList)
                return;
            _lastParsedPlayerList = data;
            ParsePlayerList(data);
            onStateChanged?.Invoke();
        }

        /// <summary>Client-side: when the server signals the drawing is ready, ack once we have the strokes.</summary>
        private void OnDrawReadyChanged(int _)
        {
            if (isServer)
                return; // the server already has the data
            if (_drawReady.value == _lastAckedDrawReady)
                return; // already acked this handshake
            if (AllStrokesForCurrentRound())
            {
                _lastAckedDrawReady = _drawReady.value;
                AckDrawRpc();
            }
        }

        /// <summary>Client-side: when the server asks for round readiness, ack once we have the round data.</summary>
        private void OnRoundReadySignalChanged(int _)
        {
            if (isServer)
                return;
            if (_roundReadySignal.value == _lastAckedRoundReady)
                return;
            if (IsRoundReady)
            {
                _lastAckedRoundReady = _roundReadySignal.value;
                AckRoundReadyRpc();
            }
        }

        // ---------------------------------------------------------------
        // Player tracking (server only)
        // ---------------------------------------------------------------

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (!isServer)
                return;

            AddPlayer(player);
            // Auto-start is deferred until each client confirms it has spawned and is
            // ready (see ClientReadyRpc / TryAutoStart). This avoids starting the game
            // before a client is listening for the phase change, which could otherwise
            // leave it stuck on the waiting screen.
        }

        private System.Collections.IEnumerator StartAfterDelay()
        {
            yield return new WaitForSeconds(2f);
            if (_phase.value == MovingPhase.Waiting && _playerNames.Count >= 2 && AllClientsReady())
                StartGame();
        }

        /// <summary>True once every remote player has confirmed it spawned and is ready.</summary>
        private bool AllClientsReady()
        {
            if (_playerIndex.Count == 0)
                return false;
            var hasServer = networkManager != null;
            var serverId = hasServer ? networkManager.localPlayer.id : default;
            foreach (var p in _playerIndex.Keys)
            {
                if (hasServer && p.id == serverId)
                    continue; // the host/server is always ready
                if (!_readyClients.Contains(p))
                    return false;
            }
            return true;
        }

        private void TryAutoStart()
        {
            if (_phase.value != MovingPhase.Waiting)
                return;
            if (_playerNames.Count < 2)
                return;
            if (!AllClientsReady())
                return;
            StartCoroutine(StartAfterDelay());
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (!isServer)
                return;

            if (!_playerIndex.TryGetValue(player, out var idx))
                return;
            if (idx < 0 || idx >= _playerNames.Count)
            {
                _playerIndex.Remove(player);
                return;
            }

            _playerNames.RemoveAt(idx);
            _scores.RemoveAt(idx);
            _playerColor.RemoveAt(idx);
            _playerIcon.RemoveAt(idx);
            _playerIndex.Remove(player);
            _readyClients.Remove(player);

            var keys = new List<PlayerID>(_playerIndex.Keys);
            foreach (var p in keys)
                if (_playerIndex[p] > idx)
                    _playerIndex[p]--;
            _playerListSync.value = EncodePlayerList();
        }

        private void AddPlayer(PlayerID player)
        {
            if (_playerIndex.ContainsKey(player))
                return;

            var idx = _playerNames.Count;
            _playerIndex[player] = idx;
            _playerNames.Add($"Player {idx + 1}");
            _scores.Add(0);
            // Auto-assign identity: color + icon by index (lobby customization comes later).
            _playerColor.Add(idx);
            _playerIcon.Add(idx);
            if (_pendingNames.TryGetValue(player, out var name))
            {
                _playerNames[idx] = name;
                _pendingNames.Remove(player);
            }
            _playerListSync.value = EncodePlayerList();
        }

        private string EncodePlayerList()
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < _playerNames.Count; i++)
            {
                if (i > 0)
                    sb.Append(PlayerSep);
                sb.Append(_playerNames[i]).Append(PlayerFieldSep)
                  .Append(_playerColor[i]).Append(PlayerFieldSep)
                  .Append(_playerIcon[i]).Append(PlayerFieldSep)
                  .Append(_scores[i]);
            }
            return sb.ToString();
        }

        private string EncodeFinalScores()
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < _playerNames.Count; i++)
            {
                if (i > 0)
                    sb.Append('\u0006');
                sb.Append(_playerNames[i]).Append('\u0005')
                  .Append(_playerColor[i]).Append('\u0005')
                  .Append(_playerIcon[i]).Append('\u0005')
                  .Append(_scores[i]);
            }
            return sb.ToString();
        }

        private void ParsePlayerList(string data)
        {
            _playerNames.Clear();
            _scores.Clear();
            _playerColor.Clear();
            _playerIcon.Clear();
            if (string.IsNullOrEmpty(data))
                return;
            foreach (var entry in data.Split(PlayerSep))
            {
                var parts = entry.Split(PlayerFieldSep);
                if (parts.Length < 4)
                    continue;
                _playerNames.Add(parts[0]);
                _playerColor.Add(int.TryParse(parts[1], out var c) ? c : 0);
                _playerIcon.Add(int.TryParse(parts[2], out var ic) ? ic : 0);
                _scores.Add(int.TryParse(parts[3], out var s) ? s : 0);
            }
        }

        private int IndexOfPlayer(PlayerID player)
        {
            return _playerIndex.TryGetValue(player, out var idx) ? idx : -1;
        }

        private string GetLocalDisplayName()
        {
            var lobby = GameOrchestrator.active?.activeLobby;
            return lobby?.localPlayer?.displayName;
        }

        [ServerRpc(requireOwnership: false)]
        private void RegisterNameRpc(string name, RPCInfo info = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            var idx = IndexOfPlayer(info.sender);
            if (idx < 0)
            {
                // Player not registered yet — remember the name and apply it on AddPlayer.
                _pendingNames[info.sender] = name.Trim();
                return;
            }
            _playerNames[idx] = name.Trim();
            _playerListSync.value = EncodePlayerList();
        }

        /// <summary>Client -> server: "I've spawned and am ready to receive state."</summary>
        [ServerRpc(requireOwnership: false)]
        private void ClientReadyRpc(RPCInfo info = default)
        {
            if (!isServer)
                return;
            _readyClients.Add(info.sender);
            // Re-send the authoritative player list + this client's index, in case the
            // initial SyncVar state was missed during the spawn race. Only send when we
            // actually have players, so an empty list can't wipe a populated mirror.
            if (_playerNames.Count > 0)
                SendPlayerStateTo(info.sender, _playerListSync.value, IndexOfPlayer(info.sender));
            TryAutoStart();
        }

        [TargetRpc]
        private void SendPlayerStateTo(PlayerID target, string encodedList, int localIndex)
        {
            ParsePlayerList(encodedList);
            _localIndex = localIndex;
            onStateChanged?.Invoke();
        }

        // ---------------------------------------------------------------
        // Game flow (server only)
        // ---------------------------------------------------------------

        /// <summary>Host-only: start the game from the waiting room.</summary>
        public void HostStartGame()
        {
            if (!isServer || _phase.value != MovingPhase.Waiting)
                return;
            if (_playerNames.Count < 2 && !(_allowSinglePlayer && _playerNames.Count >= 1))
            {
                Debug.LogWarning("[MovingCanvasGame] Need at least 2 players to start (or 1 in single-player test mode).");
                return;
            }
            if (!AllClientsReady())
            {
                Debug.LogWarning("[MovingCanvasGame] Waiting for all clients to be ready before starting...");
                return;
            }
            StartGame();
        }

        /// <summary>Host-only: end the game from the final screen.</summary>
        public void HostEndGame()
        {
            if (!isServer || _phase.value != MovingPhase.Final)
                return;
            EndGame();
        }

        /// <summary>
        /// Tuning helper: reset the draw timer back to full and clear the submitted
        /// state so you can keep drawing/refining. Right-click this component in the
        /// Inspector and choose "Restart Drawing Phase".
        /// </summary>
        [ContextMenu("Restart Drawing Phase")]
        public void RestartDrawingPhase()
        {
            if (!isServer || _phase.value != MovingPhase.Drawing)
                return;

            for (var i = 0; i < _canvasSubmitted.Count; i++)
                _canvasSubmitted[i] = false;
            _drawRestart.value++;
            StartCoroutine(StartDrawTimerAfterIntro());
            SplitLog.Log("[MovingCanvasGame] Drawing phase restarted (timer reset).");
        }

        private void StartGame()
        {
            _round.value = 1;
            SetPhase(MovingPhase.Tutorial);
        }

        /// <summary>Host-only: advance from the tutorial to the first drawing phase.</summary>
        public void HostStartDrawing()
        {
            if (!isServer || _phase.value != MovingPhase.Tutorial)
                return;
            SetPhase(MovingPhase.Drawing);
        }

        private void SetPhase(MovingPhase newPhase)
        {
            if (isServer)
                SplitLog.Log($"[MovingCanvasGame] Phase -> {newPhase} (round {_round.value}, players {_playerNames.Count})");

            _phase.value = newPhase;

            switch (newPhase)
            {
                case MovingPhase.Tutorial:
                    // No timer — stays until the host advances to Drawing.
                    break;
                case MovingPhase.Drawing:
                    SetupRound();
                    // Wait for the intro (prompt + countdown) before starting the draw timer.
                    StartCoroutine(StartDrawTimerAfterIntro());
                    break;
                case MovingPhase.Guessing:
                    _guessCanvasIndex.value = 0;
                    _timer.StartTimer(_guessTime);
                    break;
                case MovingPhase.Scoreboard:
                    _timer.StartTimer(_scoreboardTime);
                    break;
                case MovingPhase.Final:
                    // No timer — the final screen persists until the host ends the game.
                    break;
            }
        }

        private System.Collections.IEnumerator StartDrawTimerAfterIntro()
        {
            yield return new WaitForSeconds(_introTime);
            // Wait for all clients to confirm they have the round data before starting
            // the draw timer, so nobody is stuck without a prompt/movement mode.
            _roundReadySignal.value++;
            _roundAckedClients.Clear();
            _roundAckedClients.Add(networkManager.localPlayer); // host already has the data
            var t = 0f;
            while (!AllRoundAcked() && t < 3f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            _timer.StartTimer(_drawTime);
        }

        private void SetupRound()
        {
            var n = _playerNames.Count;

            _roundReady.value = 0;
            _canvasStrokes.Clear();
            _canvasSubmitted.Clear();

            var prompts = GetRoundPrompts(n);
            for (var k = 0; k < n; k++)
            {
                _canvasStrokes.Add(MakeStrokeEntry(_round.value, ""));
                _canvasSubmitted.Add(false);
            }

            _guessCanvas.Clear();
            _guessPlayer.Clear();
            _guessText.Clear();
            _guessCorrect.Clear();
            _guessOrder.Clear();

            _roundPrompts.value = EncodePrompts(_round.value, prompts);
            _movementMode.value = (int)GetRoundMode(_round.value);
            _roundReady.value = _round.value;

            if (isServer)
                SplitLog.Log($"[MovingCanvasGame] SetupRound n={n}, mode={GetRoundMode(_round.value)}: " +
                          string.Join(" | ", prompts));
        }

        private MovementMode GetRoundMode(int round)
        {
            if (_roundModes == null || _roundModes.Length == 0)
                return MovementMode.Sway;
            return _roundModes[(round - 1) % _roundModes.Length];
        }

        private List<string> GetRoundPrompts(int count)
        {
            var pool = new List<string>();
            if (_deck != null && _deck.prompts.Count > 0)
                pool.AddRange(_deck.prompts);
            else
                pool.AddRange(MovingPromptDeck.Fallback);

            // Exclude prompts already used earlier in this game.
            pool.RemoveAll(p => _usedPrompts.Contains(p));

            var result = new List<string>();
            while (pool.Count > 0 && result.Count < count)
            {
                var idx = UnityEngine.Random.Range(0, pool.Count);
                var prompt = pool[idx];
                result.Add(prompt);
                _usedPrompts.Add(prompt);
                pool.RemoveAt(idx);
            }
            while (result.Count < count)
                result.Add(GetPrompt());
            return result;
        }

        private string GetPrompt()
        {
            var source = _deck != null && _deck.prompts.Count > 0
                ? (IReadOnlyList<string>)_deck.prompts
                : MovingPromptDeck.Fallback;

            var available = new List<string>();
            for (var i = 0; i < source.Count; i++)
                if (!_usedPrompts.Contains(source[i]))
                    available.Add(source[i]);
            if (available.Count == 0)
                available.AddRange(source); // everything used; allow repeats as a last resort

            var prompt = available[UnityEngine.Random.Range(0, available.Count)];
            _usedPrompts.Add(prompt);
            return prompt;
        }

        private static string EncodePrompts(int round, List<string> prompts)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(round).Append('\u0002');
            for (var i = 0; i < prompts.Count; i++)
            {
                if (i > 0)
                    sb.Append('\u0002');
                sb.Append(prompts[i]);
            }
            return sb.ToString();
        }

        private void OnTimerEnd()
        {
            if (!isServer)
                return;

            switch (_phase.value)
            {
                case MovingPhase.Drawing:
                    // Handshake: wait for all drawings to reach the server, then wait for
                    // every client to confirm it received the freshly broadcast strokes,
                    // before advancing to Guessing.
                    StartCoroutine(CollectAndAdvanceToGuessing());
                    break;
                case MovingPhase.Guessing:
                    if (!_advanceScheduled.Contains(_guessCanvasIndex.value))
                    {
                        _advanceScheduled.Add(_guessCanvasIndex.value);
                        StartCoroutine(RevealAndAdvance(_guessCanvasIndex.value));
                    }
                    break;
                case MovingPhase.Scoreboard:
                    if (_round.value < _maxRounds)
                    {
                        ArchiveRound(); // archive the round that just finished
                        _round.value++;
                        SetPhase(MovingPhase.Drawing);
                    }
                    else
                    {
                        ArchiveRound();
                        _finalScoresSync.value = EncodeFinalScores();
                        SetPhase(MovingPhase.Final);
                    }
                    break;
                case MovingPhase.Final:
                    // Stay on the final screen until the host ends the game.
                    break;
            }
        }

        private System.Collections.IEnumerator CollectAndAdvanceToGuessing()
        {
            // Phase 1: wait for all canvases to be submitted to the server.
            var t = 0f;
            while (!AllSubmitted() && t < 3f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            // Phase 2: signal clients to ack once they have the freshly broadcast strokes.
            _drawReady.value++;
            _ackedClients.Clear();
            _ackedClients.Add(networkManager.localPlayer); // host already has the data

            t = 0f;
            while (!AllClientsAcked() && t < 3f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            // Phase 3: advance.
            SetPhase(MovingPhase.Guessing);
        }

        private bool AllClientsAcked()
        {
            foreach (var p in _playerIndex.Keys)
                if (!_ackedClients.Contains(p))
                    return false;
            return true;
        }

        private bool AllRoundAcked()
        {
            foreach (var p in _playerIndex.Keys)
                if (!_roundAckedClients.Contains(p))
                    return false;
            return true;
        }

        private bool AllStrokesForCurrentRound()
        {
            for (var i = 0; i < _canvasStrokes.Count; i++)
                if (StrokeEntryRound(_canvasStrokes[i]) != _round.value)
                    return false;
            return true;
        }

        private void AdvanceGuessCanvas()
        {
            _revealPrompt.value = ""; // clear the reveal for the next canvas
            _guessCanvasIndex.value++;
            if (_guessCanvasIndex.value >= _playerNames.Count)
            {
                if (_round.value >= _maxRounds)
                {
                    // Last round done — skip the scoreboard, go straight to game over.
                    ArchiveRound();
                    _finalScoresSync.value = EncodeFinalScores();
                    SetPhase(MovingPhase.Final);
                }
                else
                {
                    SetPhase(MovingPhase.Scoreboard);
                }
            }
            else
            {
                _timer.StartTimer(_guessTime);
            }
        }

        private void ArchiveRound()
        {
            if (_round.value <= 0)
                return;
            for (var c = 0; c < _canvasStrokes.Count; c++)
            {
                var strokes = GetCanvasStrokes(c);
                if (string.IsNullOrEmpty(strokes))
                    continue;
                _archiveEntry.Add(
                    _round.value + ArchiveFieldSep.ToString() +
                    GetPrompt(c) + ArchiveFieldSep +
                    c + ArchiveFieldSep +
                    strokes);
            }
            if (isServer)
                SplitLog.Log($"[MovingCanvasGame] Archived round {_round.value}: {_archiveEntry.Count} entries total");
        }

        private void EndGame()
        {
            var broadcaster = FindAnyObjectByType<GameOverBroadcaster>();
            if (broadcaster != null)
                broadcaster.EndGame();
        }

        // ---------------------------------------------------------------
        // Player intents (client -> server)
        // ---------------------------------------------------------------

        /// <summary>UI calls this when the local player finishes drawing (early or on timer).</summary>
        public void SubmitDrawing(int canvas, string strokeJson)
        {
            if (string.IsNullOrEmpty(strokeJson))
                return;
            SubmitDrawingRpc(canvas, strokeJson);
        }

        /// <summary>UI calls this to guess the prompt of the currently revealed canvas.</summary>
        public void SubmitGuess(int canvas, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            SubmitGuessRpc(canvas, text.Trim());
        }

        [ServerRpc(requireOwnership: false)]
        private void SubmitDrawingRpc(int canvas, string strokeJson, RPCInfo info = default)
        {
            if (_phase.value != MovingPhase.Drawing)
            {
                SplitLog.Log($"[MovingCanvasGame] REJECTED drawing (phase={_phase.value}, not Drawing) canvas={canvas}, len={strokeJson.Length}");
                return;
            }
            if (canvas < 0 || canvas >= _canvasStrokes.Count)
                return;

            var sender = IndexOfPlayer(info.sender);
            if (sender < 0 || sender != canvas)
                return; // each player draws on their own canvas

            _canvasStrokes[canvas] = MakeStrokeEntry(_round.value, strokeJson);
            _canvasSubmitted[canvas] = true;
            SplitLog.Log($"[MovingCanvasGame] Drawing stored: canvas={canvas}, len={strokeJson.Length}");

            // If all canvases submitted, run the handshake to advance early.
            if (AllSubmitted())
                StartCoroutine(CollectAndAdvanceToGuessing());
        }

        [ServerRpc(requireOwnership: false)]
        private void AckDrawRpc(RPCInfo info = default)
        {
            if (_phase.value != MovingPhase.Drawing)
                return;
            _ackedClients.Add(info.sender);
        }

        [ServerRpc(requireOwnership: false)]
        private void AckRoundReadyRpc(RPCInfo info = default)
        {
            if (_phase.value != MovingPhase.Drawing)
                return;
            _roundAckedClients.Add(info.sender);
        }

        [ServerRpc(requireOwnership: false)]
        private void SubmitGuessRpc(int canvas, string text, RPCInfo info = default)
        {
            if (_phase.value != MovingPhase.Guessing)
                return;
            if (canvas != _guessCanvasIndex.value)
                return; // only the currently revealed canvas
            if (canvas < 0 || canvas >= _playerNames.Count)
                return;

            var guesser = IndexOfPlayer(info.sender);
            if (guesser < 0 || guesser == canvas)
                return; // artist doesn't guess their own canvas

            // Allow multiple guesses, but only score the FIRST correct one per player
            // (prevents spamming correct answers for points).
            var alreadyCorrect = false;
            for (var i = 0; i < _guessCanvas.Count; i++)
                if (_guessCanvas[i] == canvas && _guessPlayer[i] == guesser && _guessCorrect[i])
                {
                    alreadyCorrect = true;
                    break;
                }

            var prompt = GetPrompt(canvas);
            var correct = !string.IsNullOrEmpty(prompt) &&
                          text.ToLowerInvariant().Contains(prompt.ToLowerInvariant());

            var order = 0;
            if (correct && !alreadyCorrect)
            {
                order = CorrectCountForCanvas(canvas) + 1;
                ScoreCorrectGuess(canvas, guesser, order);
            }

            _guessCanvas.Add(canvas);
            _guessPlayer.Add(guesser);
            _guessText.Add(text);
            _guessCorrect.Add(correct);
            _guessOrder.Add(order);
            SplitLog.Log($"[MovingCanvasGame] Guess: canvas={canvas}, guesser={guesser}, correct={correct}, order={order}, text=\"{text}\"");

            // If every non-artist player has now guessed correctly, wait a moment so
            // everyone sees the last correct guess, then advance to the next canvas
            // (or to the scoreboard if this was the last one).
            if (AllPlayersCorrectForCanvas(canvas) && !_advanceScheduled.Contains(canvas))
            {
                _advanceScheduled.Add(canvas);
                StartCoroutine(DelayedAdvance(canvas));
            }
        }

        private System.Collections.IEnumerator DelayedAdvance(int canvas)
        {
            yield return new WaitForSeconds(2f);
            yield return StartCoroutine(RevealAndAdvance(canvas));
        }

        private System.Collections.IEnumerator RevealAndAdvance(int canvas)
        {
            _revealPrompt.value = GetPrompt(canvas);
            yield return new WaitForSeconds(3f);
            _advanceScheduled.Remove(canvas);
            if (_phase.value != MovingPhase.Guessing)
                yield break;
            if (_guessCanvasIndex.value != canvas)
                yield break; // already advanced (timer or another path)
            AdvanceGuessCanvas();
        }

        private bool AllPlayersCorrectForCanvas(int canvas)
        {
            var correctPlayers = new HashSet<int>();
            for (var i = 0; i < _guessCanvas.Count; i++)
                if (_guessCanvas[i] == canvas && _guessCorrect[i])
                    correctPlayers.Add(_guessPlayer[i]);
            // Every player except the artist must have a correct guess.
            return correctPlayers.Count >= _playerNames.Count - 1;
        }

        private int CorrectCountForCanvas(int canvas)
        {
            var count = 0;
            for (var i = 0; i < _guessCanvas.Count; i++)
                if (_guessCanvas[i] == canvas && _guessCorrect[i])
                    count++;
            return count;
        }

        private void ScoreCorrectGuess(int canvas, int guesser, int order)
        {
            // Speed-tiered guesser points: first +3, second +2, rest +1.
            var guesserPoints = order == 1 ? 3 : order == 2 ? 2 : 1;
            _scores[guesser] += guesserPoints;

            // Artist earns +1 per correct guesser.
            _scores[canvas] += 1;

            _playerListSync.value = EncodePlayerList();
        }

        private bool AllSubmitted()
        {
            for (var i = 0; i < _canvasSubmitted.Count; i++)
                if (!_canvasSubmitted[i])
                    return false;
            return true;
        }

        // ---------------------------------------------------------------
        // Round-tagged stroke entries
        // ---------------------------------------------------------------

        private static string MakeStrokeEntry(int round, string json)
        {
            return round + StrokeRoundSep.ToString() + (json ?? "");
        }

        private static int StrokeEntryRound(string entry)
        {
            if (string.IsNullOrEmpty(entry))
                return -1;
            var sep = entry.IndexOf(StrokeRoundSep);
            if (sep <= 0)
                return -1;
            return int.TryParse(entry.Substring(0, sep), out var r) ? r : -1;
        }
    }
}