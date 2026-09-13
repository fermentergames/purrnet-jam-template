using System;
using System.Collections.Generic;
using PurrNet;
using PurrNet.Lobby;
using UnityEngine;

namespace Jam
{
    public enum SplitPhase
    {
        Waiting,    // waiting for players
        Drawing,    // teams draw on shared canvases (Mode A or B)
        Guessing,   // reveal canvases, everyone guesses the ones they're not in
        Judging,    // reveal prompts, everyone votes full/half/wrong on each guess
        Scoreboard, // show scores
        Final       // game over, show winner
    }

    /// <summary>
    /// Split Canvas — a cooperative split-information drawing game.
    ///
    /// Players are arranged in a polygon; each player is paired with the player to
    /// their right. Every pair shares a canvas, and each player holds only HALF of
    /// that pair's combined prompt. They alternate short drawing bursts on the
    /// shared canvas (no talking, no words), then everyone guesses the combined
    /// prompt of the canvases they did NOT participate in.
    ///
    /// Scheduling:
    ///   Mode A (fast, no breaks): every canvas is active every tick; each player
    ///     alternates between their two canvases. Works for any N >= 3.
    ///   Mode B (rounds, with breaks): canvases are partitioned into disjoint
    ///     rounds so each player draws on exactly one canvas per round. Clean for
    ///     even N (2 rounds); for odd N it falls back to Mode A.
    ///
    /// The server owns ALL state and broadcasts it via SyncVars/SyncLists. Clients
    /// only send intents ([ServerRpc] SubmitDrawing / SubmitGuess / JudgeGuess) and
    /// render the synced state. The drawing schedule is deterministic, so clients
    /// compute their active canvas from the synced tick index.
    ///
    /// Setup:
    ///   1. Create an empty "SplitCanvasGame" in MainGame, add this component.
    ///   2. (Optional) assign a SplitPromptDeck; otherwise built-in prompts are used.
    ///   3. Add the SplitCanvasUI component to the same object (or a child).
    /// </summary>
    public class SplitCanvasGame : NetworkBehaviour
    {
        [Header("Timing (seconds)")]
        [SerializeField] private float _tickTime = 20f;      // one drawing burst
        [SerializeField] private float _guessTime = 60f;     // guessing phase
        [SerializeField] private float _scoreboardTime = 8f;
        [SerializeField] private float _finalTime = 12f;

        [Header("Rounds")]
        [SerializeField] private int _maxRounds = 3;
        [SerializeField] private int _ticksPerRound = 4;     // drawing bursts per round

        [Header("Scheduling")]
        [SerializeField] private bool _modeB = false;        // true = rounds w/ breaks, false = fast alternating

        [Header("Content")]
        [SerializeField] private SplitPromptDeck _deck;

        // --- Synced state (server-authoritative) ---
        private readonly SyncVar<SplitPhase> _phase = new SyncVar<SplitPhase>(SplitPhase.Waiting);
        private readonly SyncVar<int> _round = new SyncVar<int>(0);
        private readonly SyncVar<int> _tick = new SyncVar<int>(0);
        private readonly SyncVar<int> _roundReady = new SyncVar<int>(0); // set to _round when round data is fully synced
        private readonly SyncVar<int> _tickReady = new SyncVar<int>(-1); // set to _tick when clients should ack receipt
        private readonly SyncVar<string> _roundPrompts = new SyncVar<string>(""); // atomic: all subjects+modifiers for the round
        private readonly SyncList<string> _playerNames = new SyncList<string>();
        private readonly SyncList<int> _scores = new SyncList<int>();
        private readonly SyncList<PlayerID> _playerOrder = new SyncList<PlayerID>(); // index order, mirrors _playerIndex

        // Per-canvas state (canvas k = pair {k, (k+1)%N}).
        private readonly SyncList<string> _canvasSubject = new SyncList<string>();
        private readonly SyncList<string> _canvasModifier = new SyncList<string>();
        // Each entry is tagged with the round it belongs to: "<round>\u0003<base64>".
        // Tagging makes stale entries self-identifying: the client can tell that a
        // value belongs to a previous round and refuse to render it (SyncList updates
        // can lag behind the SyncVar that signals "round ready").
        private readonly SyncList<string> _canvasPng = new SyncList<string>();
        private readonly SyncList<int> _canvasScore = new SyncList<int>();

        // Archive of every finished canvas across all rounds, for the game-over screen.
        // Entry format: "<round>\u0004<subject>\u0004<modifier>\u0004<canvasIndex>\u0004<base64>".
        private readonly SyncList<string> _archiveEntry = new SyncList<string>();
        private const char PngRoundSep = '\u0003';
        private const char ArchiveFieldSep = '\u0004';

        // Guesses (flat parallel lists).
        private readonly SyncList<string> _guessText = new SyncList<string>();
        private readonly SyncList<int> _guessCanvas = new SyncList<int>();
        private readonly SyncList<int> _guessPlayer = new SyncList<int>();
        private readonly SyncList<int> _guessResult = new SyncList<int>(); // 0=unjudged,1=full,2=half,3=wrong

        // Votes (flat parallel lists) — everyone votes full/half/wrong on each guess.
        private readonly SyncList<int> _voteGuess = new SyncList<int>();
        private readonly SyncList<int> _votePlayer = new SyncList<int>();
        private readonly SyncList<int> _voteValue = new SyncList<int>(); // 1=full,2=half,3=wrong

        private readonly SyncTimer _timer = new SyncTimer();

        // Server-only: maps each player to their index in the parallel lists.
        private readonly Dictionary<PlayerID, int> _playerIndex = new Dictionary<PlayerID, int>();

        // Server-only: handshake state for the drawing tick.
        private readonly HashSet<int> _submittedCanvases = new HashSet<int>();
        private readonly HashSet<PlayerID> _ackedClients = new HashSet<PlayerID>();

        // Client-side: parsed prompts from _roundPrompts (atomic, avoids stale SyncList reads).
        private readonly List<string> _subjects = new List<string>();
        private readonly List<string> _modifiers = new List<string>();

        /// <summary>Fired whenever any synced state changes, so the UI can refresh.</summary>
        public event Action onStateChanged;

        /// <summary>Fired on the local client when the drawing tick timer ends (so the UI can submit).</summary>
        public event Action onLocalTimerEnd;

        // --- Read-only accessors for the UI ---
        public SplitPhase Phase => _phase.value;
        public int Round => _round.value;
        public int Tick => _tick.value;
        public int TimeRemaining => _timer.remainingInt;
        public int PlayerCount => _playerNames.Count;
        public int CanvasCount => _canvasSubject.Count;
        public bool ModeB => _modeB;

        /// <summary>True once the current round's canvas data has fully synced to this client.</summary>
        public bool IsRoundReady
        {
            get
            {
                if (_roundReady.value != _round.value)
                    return false;
                if (string.IsNullOrEmpty(_roundPrompts.value))
                    return false;

                var n = _playerNames.Count;
                if (n == 0)
                    return false;
                if (_canvasSubject.Count != n || _canvasModifier.Count != n || _canvasPng.Count != n)
                    return false;

                // A count match is NOT enough: on round 2 the previous round's list also
                // has n entries. Verify every PNG entry is tagged for the current round,
                // otherwise the client could render last round's artwork as this round's.
                for (var i = 0; i < n; i++)
                    if (PngEntryRound(_canvasPng[i]) != _round.value)
                        return false;

                return true;
            }
        }
        public IReadOnlyList<string> PlayerNames => _playerNames.list;
        public IReadOnlyList<int> Scores => _scores.list;
        public IReadOnlyList<string> CanvasSubject => _canvasSubject.list;
        public IReadOnlyList<string> CanvasModifier => _canvasModifier.list;
        public IReadOnlyList<string> CanvasPng => _canvasPng.list;
        public IReadOnlyList<int> CanvasScore => _canvasScore.list;

        /// <summary>Number of archived (finished) canvases kept for the game-over screen.</summary>
        public int ArchiveCount => _archiveEntry.Count;

        /// <summary>Base64 PNG of an archived canvas.</summary>
        public string ArchivePng(int i) => ArchiveField(i, 4);

        /// <summary>Round the archived canvas was drawn in.</summary>
        public int ArchiveRound(int i) => int.TryParse(ArchiveField(i, 0), out var r) ? r : 0;

        /// <summary>Full subject of the archived canvas's prompt.</summary>
        public string ArchiveSubject(int i) => ArchiveField(i, 1);

        /// <summary>Full modifier of the archived canvas's prompt.</summary>
        public string ArchiveModifier(int i) => ArchiveField(i, 2);

        /// <summary>Canvas index (0..N-1) of the archived canvas.</summary>
        public int ArchiveCanvas(int i) => int.TryParse(ArchiveField(i, 3), out var c) ? c : 0;

        private string ArchiveField(int i, int field)
        {
            if (i < 0 || i >= _archiveEntry.Count)
                return "";
            var parts = _archiveEntry[i].Split(ArchiveFieldSep);
            return field < parts.Length ? parts[field] : "";
        }
        public IReadOnlyList<string> GuessText => _guessText.list;
        public IReadOnlyList<int> GuessCanvas => _guessCanvas.list;
        public IReadOnlyList<int> GuessPlayer => _guessPlayer.list;
        public IReadOnlyList<int> GuessResult => _guessResult.list;
        public IReadOnlyList<int> VoteGuess => _voteGuess.list;
        public IReadOnlyList<int> VotePlayer => _votePlayer.list;
        public IReadOnlyList<int> VoteValue => _voteValue.list;
        public bool IsHost => isServer;

        /// <summary>Index of the local player in the parallel lists, or -1.</summary>
        public int LocalPlayerIndex
        {
            get
            {
                var local = networkManager.localPlayer;
                for (var i = 0; i < _playerOrder.Count; i++)
                    if (_playerOrder[i].id == local.id)
                        return i;
                return -1;
            }
        }

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
            _phase.onChanged += OnSyncVarChanged<SplitPhase>;
            _round.onChanged += OnSyncVarChanged<int>;
            _tick.onChanged += OnSyncVarChanged<int>;
            _roundReady.onChanged += OnSyncVarChanged<int>;
            _tickReady.onChanged += OnTickReadyChanged;
            _roundPrompts.onChanged += OnRoundPromptsChanged;
            _playerNames.onChanged += OnSyncListChanged<string>;
            _scores.onChanged += OnSyncListChanged<int>;
            _playerOrder.onChanged += OnSyncListChanged<PlayerID>;
            _canvasSubject.onChanged += OnSyncListChanged<string>;
            _canvasModifier.onChanged += OnSyncListChanged<string>;
            _canvasPng.onChanged += OnSyncListChanged<string>;
            _canvasScore.onChanged += OnSyncListChanged<int>;
            _archiveEntry.onChanged += OnSyncListChanged<string>;
            _guessText.onChanged += OnSyncListChanged<string>;
            _guessCanvas.onChanged += OnSyncListChanged<int>;
            _guessPlayer.onChanged += OnSyncListChanged<int>;
            _guessResult.onChanged += OnSyncListChanged<int>;
            _voteGuess.onChanged += OnSyncListChanged<int>;
            _votePlayer.onChanged += OnSyncListChanged<int>;
            _voteValue.onChanged += OnSyncListChanged<int>;
            _timer.onTimerSecondTick += OnTimerTick;
            _timer.onTimerEnd += OnTimerEnd;
            _timer.onTimerEnd += OnLocalTimerEnd;
        }

        private void OnDisable()
        {
            _phase.onChanged -= OnSyncVarChanged<SplitPhase>;
            _round.onChanged -= OnSyncVarChanged<int>;
            _tick.onChanged -= OnSyncVarChanged<int>;
            _roundReady.onChanged -= OnSyncVarChanged<int>;
            _tickReady.onChanged -= OnTickReadyChanged;
            _roundPrompts.onChanged -= OnRoundPromptsChanged;
            _playerNames.onChanged -= OnSyncListChanged<string>;
            _scores.onChanged -= OnSyncListChanged<int>;
            _playerOrder.onChanged -= OnSyncListChanged<PlayerID>;
            _canvasSubject.onChanged -= OnSyncListChanged<string>;
            _canvasModifier.onChanged -= OnSyncListChanged<string>;
            _canvasPng.onChanged -= OnSyncListChanged<string>;
            _canvasScore.onChanged -= OnSyncListChanged<int>;
            _archiveEntry.onChanged -= OnSyncListChanged<string>;
            _guessText.onChanged -= OnSyncListChanged<string>;
            _guessCanvas.onChanged -= OnSyncListChanged<int>;
            _guessPlayer.onChanged -= OnSyncListChanged<int>;
            _guessResult.onChanged -= OnSyncListChanged<int>;
            _voteGuess.onChanged -= OnSyncListChanged<int>;
            _votePlayer.onChanged -= OnSyncListChanged<int>;
            _voteValue.onChanged -= OnSyncListChanged<int>;
            _timer.onTimerSecondTick -= OnTimerTick;
            _timer.onTimerEnd -= OnTimerEnd;
            _timer.onTimerEnd -= OnLocalTimerEnd;
        }

        private void OnSyncVarChanged<T>(T _) => onStateChanged?.Invoke();
        private void OnSyncListChanged<T>(SyncListChange<T> _) => onStateChanged?.Invoke();
        private void OnTimerTick() => onStateChanged?.Invoke();
        private void OnLocalTimerEnd() => onLocalTimerEnd?.Invoke();

        /// <summary>Client-side: when the server signals a tick is ready, ack once we have the canvas data.</summary>
        private void OnTickReadyChanged(int _)
        {
            if (isServer)
                return; // the server already has the data
            if (_tickReady.value == _tick.value && _canvasPng.Count == _playerNames.Count)
                AckTickRpc();
        }

        /// <summary>Parse the atomic round-prompts string into subject/modifier lists.</summary>
        private void OnRoundPromptsChanged(string _)
        {
            _subjects.Clear();
            _modifiers.Clear();
            var data = _roundPrompts.value;
            if (string.IsNullOrEmpty(data))
                return;
            var records = data.Split('\u0002');
            foreach (var rec in records)
            {
                var parts = rec.Split('\u0001');
                _subjects.Add(parts.Length > 0 ? parts[0] : "");
                _modifiers.Add(parts.Length > 1 ? parts[1] : "");
            }
        }

        /// <summary>Subject of a canvas, from the atomically-synced round prompts.</summary>
        public string GetSubject(int canvas)
        {
            return (canvas >= 0 && canvas < _subjects.Count) ? _subjects[canvas] : "";
        }

        /// <summary>Modifier of a canvas, from the atomically-synced round prompts.</summary>
        public string GetModifier(int canvas)
        {
            return (canvas >= 0 && canvas < _modifiers.Count) ? _modifiers[canvas] : "";
        }

        // ---------------------------------------------------------------
        // Player tracking (server only)
        // ---------------------------------------------------------------

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (!isServer)
                return;

            AddPlayer(player);

            // Auto-start once we have enough players for a polygon (3+). Delay briefly
            // so clients have time to sync the full player list before the schedule is
            // computed (otherwise a client may compute the wrong canvas from a stale count).
            if (_phase.value == SplitPhase.Waiting && _playerNames.Count >= 3)
                StartCoroutine(StartAfterDelay());
        }

        private System.Collections.IEnumerator StartAfterDelay()
        {
            yield return new WaitForSeconds(2f);
            if (_phase.value == SplitPhase.Waiting && _playerNames.Count >= 3)
                StartGame();
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
            _playerOrder.RemoveAt(idx);
            _playerIndex.Remove(player);

            var keys = new List<PlayerID>(_playerIndex.Keys);
            foreach (var p in keys)
                if (_playerIndex[p] > idx)
                    _playerIndex[p]--;
        }

        private void AddPlayer(PlayerID player)
        {
            if (_playerIndex.ContainsKey(player))
                return;

            _playerIndex[player] = _playerNames.Count;
            _playerNames.Add($"Player {_playerNames.Count + 1}");
            _scores.Add(0);
            _playerOrder.Add(player);
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
                return;

            _playerNames[idx] = name.Trim();
        }

        // ---------------------------------------------------------------
        // Polygon pairing + scheduling (deterministic, server + client)
        // ---------------------------------------------------------------

        /// <summary>Canvas k is shared by player k (left) and player (k+1)%N (right).</summary>
        public int CanvasLeft(int canvas) => canvas;
        public int CanvasRight(int canvas) => (canvas + 1) % PlayerCount;

        // Preset brush colors (dark swatches). Player index -> palette color.
        private static readonly Color[] Palette =
        {
            new Color(0.78f, 0.18f, 0.18f), // red
            new Color(0.18f, 0.38f, 0.78f), // blue
            new Color(0.16f, 0.55f, 0.26f), // green
            new Color(0.68f, 0.48f, 0.06f), // amber
            new Color(0.48f, 0.18f, 0.62f), // purple
            new Color(0.10f, 0.55f, 0.60f), // teal
            new Color(0.72f, 0.36f, 0.10f), // orange
            new Color(0.58f, 0.16f, 0.42f), // magenta
        };

        /// <summary>Dark brush color for a player index.</summary>
        public Color GetPlayerColor(int index)
        {
            if (index < 0)
                return Color.black;
            return Palette[index % Palette.Length];
        }

        /// <summary>Brighter version of a player's color (for tinting prompt text).</summary>
        public Color GetPlayerBrightColor(int index)
        {
            return Color.Lerp(GetPlayerColor(index), Color.white, 0.45f);
        }

        /// <summary>Single-letter label for a player index (A, B, C, ...).</summary>
        public static string PlayerLetter(int index)
        {
            if (index < 0 || index >= 26)
                return "?";
            return ((char)('A' + index)).ToString();
        }

        /// <summary>Pair label for a canvas, e.g. "A/B", "A/C", "B/C".</summary>
        public string GetCanvasLabel(int canvas)
        {
            if (canvas < 0 || canvas >= PlayerCount)
                return "?/?";
            return $"{PlayerLetter(CanvasLeft(canvas))}/{PlayerLetter(CanvasRight(canvas))}";
        }

        /// <summary>True if this canvas has an active drawer on the given tick.</summary>
        public bool IsCanvasActive(int canvas, int tick)
        {
            if (PlayerCount < 2)
                return false;
            if (!_modeB)
                return true; // Mode A: all canvases active every tick
            // Mode B: even N only — active canvases alternate by round.
            if (PlayerCount % 2 != 0)
                return true; // odd N falls back to Mode A
            var round = tick / 2;
            return (canvas % 2) == (round % 2);
        }

        /// <summary>Who draws on this canvas at this tick (left on even ticks, right on odd).</summary>
        public int GetActiveDrawer(int canvas, int tick)
        {
            return (tick % 2 == 0) ? CanvasLeft(canvas) : CanvasRight(canvas);
        }

        /// <summary>The canvas the local player draws on at this tick, or -1 if resting.</summary>
        public int GetLocalActiveCanvas(int tick)
        {
            var p = LocalPlayerIndex;
            if (p < 0 || PlayerCount < 2)
                return -1;

            // Player p is in canvas p (left) and canvas (p-1+N)%N (right).
            // Even tick -> draws canvas p (as left). Odd tick -> draws canvas (p-1+N)%N (as right).
            var canvas = (tick % 2 == 0) ? p : ((p - 1 + PlayerCount) % PlayerCount);
            return IsCanvasActive(canvas, tick) ? canvas : -1;
        }

        /// <summary>Which half of a canvas's prompt the local player holds, or "" if not a participant.</summary>
        public string GetLocalHalf(int canvas)
        {
            var p = LocalPlayerIndex;
            if (p < 0 || canvas < 0 || canvas >= CanvasCount)
                return "";
            if (CanvasLeft(canvas) == p)
                return SafeGet(_canvasSubject, canvas);
            if (CanvasRight(canvas) == p)
                return SafeGet(_canvasModifier, canvas);
            return "";
        }

        /// <summary>True if the local player participated in (drew on) this canvas.</summary>
        public bool LocalParticipated(int canvas)
        {
            var p = LocalPlayerIndex;
            if (p < 0)
                return false;
            return CanvasLeft(canvas) == p || CanvasRight(canvas) == p;
        }

        private static string SafeGet(SyncList<string> list, int i)
        {
            return (i >= 0 && i < list.Count) ? list[i] : "";
        }

        // ---------------------------------------------------------------
        // Game flow (server only)
        // ---------------------------------------------------------------

        /// <summary>Host-only: start the game from the waiting room.</summary>
        public void HostStartGame()
        {
            if (!isServer || _phase.value != SplitPhase.Waiting)
                return;
            if (_playerNames.Count < 3)
            {
                Debug.LogWarning("[SplitCanvasGame] Need at least 3 players to start (polygon pairing).");
                return;
            }
            StartGame();
        }

        /// <summary>Host-only: advance the guessing/judging phases.</summary>
        public void HostNextPhase()
        {
            if (!isServer)
                return;
            switch (_phase.value)
            {
                case SplitPhase.Guessing:
                    SetPhase(SplitPhase.Judging);
                    break;
                case SplitPhase.Judging:
                    StartCoroutine(ResolveAfterVotes());
                    break;
            }
        }

        /// <summary>Wait for all players to vote on all guesses (or timeout), then resolve and move on.</summary>
        private System.Collections.IEnumerator ResolveAfterVotes()
        {
            var t = 0f;
            while (!AllVotesIn() && t < 5f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            ResolveJudging();
            SetPhase(SplitPhase.Scoreboard);
        }

        private bool AllVotesIn()
        {
            if (_guessResult.Count == 0 || _playerOrder.Count == 0)
                return false;
            var expected = _guessResult.Count * _playerOrder.Count;
            return _voteGuess.Count >= expected;
        }

        private void StartGame()
        {
            _round.value = 1;
            SetPhase(SplitPhase.Drawing);
        }

        private void SetPhase(SplitPhase newPhase)
        {
            if (isServer)
                SplitLog.Log($"[SplitCanvasGame] Phase -> {newPhase} (round {_round.value}, players {_playerNames.Count}, modeB={_modeB})");

            _phase.value = newPhase;

            switch (newPhase)
            {
                case SplitPhase.Drawing:
                    SetupRound();
                    _tick.value = 0;
                    _timer.StartTimer(_tickTime);
                    break;
                case SplitPhase.Guessing:
                    if (isServer)
                    {
                        var lens = new System.Text.StringBuilder();
                        for (var i = 0; i < _canvasPng.Count; i++)
                        {
                            if (i > 0) lens.Append(',');
                            lens.Append(_canvasPng[i]?.Length ?? 0);
                        }
                        SplitLog.Log($"[SplitCanvasGame] Entering Guessing: pngLens=[{lens}]");
                    }
                    _timer.StartTimer(_guessTime);
                    break;
                case SplitPhase.Judging:
                    _timer.StartTimer(_guessTime);
                    break;
                case SplitPhase.Scoreboard:
                    _timer.StartTimer(_scoreboardTime);
                    break;
                case SplitPhase.Final:
                    _timer.StartTimer(_finalTime);
                    break;
            }
        }

        private void SetupRound()
        {
            var n = _playerNames.Count;
            var blank = BlankPng();
            var prompts = GetRoundPrompts(n);

            // Invalidate readiness until the canvas lists are fully repopulated.
            _roundReady.value = 0;

            _canvasSubject.Clear();
            _canvasModifier.Clear();
            _canvasPng.Clear();
            _canvasScore.Clear();

            for (var k = 0; k < n; k++)
            {
                _canvasSubject.Add(prompts[k].subject);
                _canvasModifier.Add(prompts[k].modifier);
                _canvasPng.Add(MakePngEntry(_round.value, blank));
                _canvasScore.Add(0);
            }

            // Clear guesses and votes from the previous round.
            _guessText.Clear();
            _guessCanvas.Clear();
            _guessPlayer.Clear();
            _guessResult.Clear();
            _voteGuess.Clear();
            _votePlayer.Clear();
            _voteValue.Clear();

            // Signal that the round data is fully populated. The prompts are packed
            // into a single atomic SyncVar so clients get all new prompts at once
            // (avoids reading stale SyncList values on round 2).
            _roundPrompts.value = EncodePrompts(prompts);
            _roundReady.value = _round.value;

            if (isServer)
                SplitLog.Log($"[SplitCanvasGame] SetupRound n={n}: " +
                          string.Join(" | ", prompts.ConvertAll(p => $"\"{p.subject}\"+\"{p.modifier}\"")));
        }

        private static string EncodePrompts(List<SplitPrompt> prompts)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < prompts.Count; i++)
            {
                if (i > 0)
                    sb.Append('\u0002');
                sb.Append(prompts[i].subject).Append('\u0001').Append(prompts[i].modifier);
            }
            return sb.ToString();
        }

        /// <summary>Pick distinct prompts for each canvas this round (no repeats if the pool allows).</summary>
        private List<SplitPrompt> GetRoundPrompts(int count)
        {
            var pool = new List<SplitPrompt>();
            if (_deck != null && _deck.prompts.Count > 0)
                pool.AddRange(_deck.prompts);
            else
                pool.AddRange(SplitPromptDeck.Fallback);

            var result = new List<SplitPrompt>();
            while (pool.Count > 0 && result.Count < count)
            {
                var idx = UnityEngine.Random.Range(0, pool.Count);
                result.Add(pool[idx]);
                pool.RemoveAt(idx);
            }

            // Top up if the pool was smaller than the number of canvases.
            while (result.Count < count)
                result.Add(GetPrompt());

            return result;
        }

        private void OnTimerEnd()
        {
            if (!isServer)
                return;

            switch (_phase.value)
            {
                case SplitPhase.Drawing:
                    // Handshake: wait for all active drawers' submissions to reach the
                    // server, then wait for every client (including host) to confirm it
                    // received the freshly broadcast canvases, before advancing the tick.
                    StartCoroutine(CollectAndAdvanceTick());
                    break;
                case SplitPhase.Guessing:
                    // Fallback: advance to judging if the host hasn't. Wait a short
                    // grace period so auto-submitted guesses (sent on timer end) arrive.
                    StartCoroutine(AdvanceToJudgingAfterGrace());
                    break;
                case SplitPhase.Judging:
                    // Fallback: resolve and move on if the host hasn't.
                    StartCoroutine(ResolveAfterVotes());
                    break;
                case SplitPhase.Scoreboard:
                    if (_round.value < _maxRounds)
                    {
                        _round.value++;
                        SetPhase(SplitPhase.Drawing);
                    }
                    else
                    {
                        SetPhase(SplitPhase.Final);
                    }
                    break;
                case SplitPhase.Final:
                    EndGame();
                    break;
            }
        }

        private System.Collections.IEnumerator CollectAndAdvanceTick()
        {
            // Phase 1: wait for all active canvases to be submitted to the server.
            _submittedCanvases.Clear();
            var t = 0f;
            while (!AllActiveCanvasesSubmitted() && t < 3f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            // Phase 2: signal clients to ack once they have the freshly broadcast canvases.
            _tickReady.value = _tick.value;
            _ackedClients.Clear();

            // The host (server) already has the data, so count it as acked.
            _ackedClients.Add(networkManager.localPlayer);

            t = 0f;
            while (!AllClientsAcked() && t < 3f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            // Phase 3: advance the tick.
            _tick.value++;
            if (_tick.value >= _ticksPerRound)
            {
                // Drawing for this round is finished — keep a copy for the game-over
                // screen BEFORE the canvases get reset by the next round.
                ArchiveRound();
                SetPhase(SplitPhase.Guessing);
            }
            else
                _timer.StartTimer(_tickTime);
        }

        /// <summary>Server-only: store a copy of every finished canvas for the game-over screen.</summary>
        private void ArchiveRound()
        {
            for (var c = 0; c < _canvasPng.Count; c++)
            {
                var png = GetCanvasPng(c);
                if (string.IsNullOrEmpty(png))
                    continue; // blank canvas, nothing worth archiving

                _archiveEntry.Add(
                    _round.value + ArchiveFieldSep.ToString() +
                    GetSubject(c) + ArchiveFieldSep +
                    GetModifier(c) + ArchiveFieldSep +
                    c + ArchiveFieldSep +
                    png);
            }

            if (isServer)
                SplitLog.Log($"[SplitCanvasGame] Archived round {_round.value}: {_archiveEntry.Count} entries total");
        }

        // ---------------------------------------------------------------
        // Round-tagged canvas PNGs
        // ---------------------------------------------------------------

        private static string MakePngEntry(int round, string base64)
        {
            return round + PngRoundSep.ToString() + (base64 ?? "");
        }

        private static int PngEntryRound(string entry)
        {
            if (string.IsNullOrEmpty(entry))
                return -1;
            var sep = entry.IndexOf(PngRoundSep);
            if (sep <= 0)
                return -1;
            return int.TryParse(entry.Substring(0, sep), out var r) ? r : -1;
        }

        /// <summary>
        /// Base64 PNG of a canvas, but ONLY if the synced entry belongs to the current
        /// round. Returns "" for a stale (previous-round) entry so the UI can never
        /// paint last round's artwork onto this round's canvases.
        /// </summary>
        public string GetCanvasPng(int canvas)
        {
            if (canvas < 0 || canvas >= _canvasPng.Count)
                return "";
            var entry = _canvasPng[canvas];
            if (PngEntryRound(entry) != _round.value)
                return "";
            var sep = entry.IndexOf(PngRoundSep);
            return sep >= 0 && sep + 1 < entry.Length ? entry.Substring(sep + 1) : "";
        }

        private System.Collections.IEnumerator AdvanceToJudgingAfterGrace()
        {
            yield return new WaitForSeconds(0.5f);
            SetPhase(SplitPhase.Judging);
        }

        private bool AllActiveCanvasesSubmitted()
        {
            for (var c = 0; c < _canvasPng.Count; c++)
                if (IsCanvasActive(c, _tick.value) && !_submittedCanvases.Contains(c))
                    return false;
            return true;
        }

        private bool AllClientsAcked()
        {
            for (var i = 0; i < _playerOrder.Count; i++)
                if (!_ackedClients.Contains(_playerOrder[i]))
                    return false;
            return true;
        }

        private SplitPrompt GetPrompt()
        {
            if (_deck != null && _deck.prompts.Count > 0)
                return _deck.GetRandom();
            return SplitPromptDeck.Fallback[UnityEngine.Random.Range(0, SplitPromptDeck.Fallback.Length)];
        }

        private void EndGame()
        {
            var broadcaster = FindAnyObjectByType<GameOverBroadcaster>();
            if (broadcaster != null)
                broadcaster.EndGame();
        }

        private static string BlankPng()
        {
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var px = new Color[256 * 256];
            for (var i = 0; i < px.Length; i++)
                px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            var bytes = tex.EncodeToPNG();
            UnityEngine.Object.Destroy(tex);
            return Convert.ToBase64String(bytes);
        }

        // ---------------------------------------------------------------
        // Player intents (client -> server)
        // ---------------------------------------------------------------

        /// <summary>UI calls this when the local player finishes a drawing tick on a canvas.</summary>
        public void SubmitDrawing(int canvas, byte[] png)
        {
            if (png == null)
                return;
            SubmitDrawingRpc(canvas, Convert.ToBase64String(png));
        }

        /// <summary>UI calls this to guess the combined prompt of a canvas the player is not in.</summary>
        public void SubmitGuess(int canvas, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            SubmitGuessRpc(canvas, text.Trim());
        }

        /// <summary>UI calls this to vote full/half/wrong on a guess. value: 1=full, 2=half, 3=wrong.</summary>
        public void SubmitVote(int guessIndex, int value)
        {
            SubmitVoteRpc(guessIndex, value);
        }

        [ServerRpc(requireOwnership: false)]
        private void SubmitDrawingRpc(int canvas, string base64, RPCInfo info = default)
        {
            if (_phase.value != SplitPhase.Drawing)
                return;
            if (canvas < 0 || canvas >= _canvasPng.Count)
                return;

            var sender = IndexOfPlayer(info.sender);
            if (sender < 0)
                return;

            // Accept from either member of the canvas. We can't validate against the
            // current tick's active drawer here: the client submits the PREVIOUS
            // canvas's PNG after the server has already advanced the tick, so the
            // active drawer has already changed. The server stores the latest
            // submission, and the next tick's drawer reloads from it, so it stays
            // eventually consistent.
            if (CanvasLeft(canvas) != sender && CanvasRight(canvas) != sender)
            {
                SplitLog.Log($"[SplitCanvasGame] Rejected drawing from player {sender} for canvas {canvas} (not a participant).");
                return;
            }

            _canvasPng[canvas] = MakePngEntry(_round.value, base64);
            _submittedCanvases.Add(canvas);
            SplitLog.Log($"[SplitCanvasGame] Drawing stored: canvas={canvas}, sender={sender}, len={base64.Length}");
        }

        [ServerRpc(requireOwnership: false)]
        private void AckTickRpc(RPCInfo info = default)
        {
            if (_phase.value != SplitPhase.Drawing)
                return;
            _ackedClients.Add(info.sender);
        }

        [ServerRpc(requireOwnership: false)]
        private void SubmitGuessRpc(int canvas, string text, RPCInfo info = default)
        {
            if (_phase.value != SplitPhase.Guessing)
                return;
            if (canvas < 0 || canvas >= _canvasSubject.Count)
                return;

            var guesser = IndexOfPlayer(info.sender);
            if (guesser < 0)
                return;
            if (CanvasLeft(canvas) == guesser || CanvasRight(canvas) == guesser)
                return; // participants can't guess their own canvas

            // One guess per (canvas, player).
            for (var i = 0; i < _guessCanvas.Count; i++)
                if (_guessCanvas[i] == canvas && _guessPlayer[i] == guesser)
                    return;

            _guessText.Add(text);
            _guessCanvas.Add(canvas);
            _guessPlayer.Add(guesser);
            _guessResult.Add(0);
            SplitLog.Log($"[SplitCanvasGame] Guess submitted: canvas={canvas}, guesser={guesser}, text=\"{text}\"");
        }

        [ServerRpc(requireOwnership: false)]
        private void SubmitVoteRpc(int guessIndex, int value, RPCInfo info = default)
        {
            if (_phase.value != SplitPhase.Judging)
                return;
            if (guessIndex < 0 || guessIndex >= _guessResult.Count)
                return;
            if (value < 1 || value > 3)
                return;

            var voter = IndexOfPlayer(info.sender);
            if (voter < 0)
                return;

            // One vote per (guess, player).
            for (var i = 0; i < _voteGuess.Count; i++)
                if (_voteGuess[i] == guessIndex && _votePlayer[i] == voter)
                    return;

            _voteGuess.Add(guessIndex);
            _votePlayer.Add(voter);
            _voteValue.Add(value);
            SplitLog.Log($"[SplitCanvasGame] Vote submitted: guess={guessIndex}, voter={voter}, value={value}");
        }

        /// <summary>Server-only: sum each guess's votes and apply points.</summary>
        private void ResolveJudging()
        {
            SplitLog.Log($"[SplitCanvasGame] ResolveJudging: guesses={_guessResult.Count}, votes={_voteGuess.Count}");
            for (var g = 0; g < _guessResult.Count; g++)
            {
                if (_guessResult[g] != 0)
                    continue;
                ApplyGuessVotes(g);
            }
        }

        private void ApplyGuessVotes(int guessIndex)
        {
            if (guessIndex < 0 || guessIndex >= _guessResult.Count)
                return;
            if (_guessResult[guessIndex] != 0)
                return;

            var canvas = _guessCanvas[guessIndex];
            var guesser = _guessPlayer[guessIndex];

            var fullCount = 0;
            var halfCount = 0;
            for (var v = 0; v < _voteGuess.Count; v++)
            {
                if (_voteGuess[v] != guessIndex)
                    continue;
                if (_voteValue[v] == 1) fullCount++;
                else if (_voteValue[v] == 2) halfCount++;
            }

            // Each vote contributes: Full = +2 to guesser, +1 to each artist; Half = +1 to guesser.
            _scores[guesser] += fullCount * 2 + halfCount * 1;
            AddScore(CanvasLeft(canvas), fullCount);
            AddScore(CanvasRight(canvas), fullCount);
            _canvasScore[canvas] += fullCount * 2 + halfCount * 1;

            _guessResult[guessIndex] = 1; // mark resolved
            SplitLog.Log($"[SplitCanvasGame] Guess {guessIndex}: canvas={canvas}, guesser={guesser}, full={fullCount}, half={halfCount}");
        }

        private void AddScore(int player, int amount)
        {
            if (player >= 0 && player < _scores.Count)
                _scores[player] += amount;
        }
    }
}