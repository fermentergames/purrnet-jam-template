using System;
using System.Collections.Generic;
using PurrNet;
using PurrNet.Lobby;
using UnityEngine;

namespace Jam
{
    public enum GamePhase
    {
        Waiting,    // waiting for players
        Prompt,     // players type answers
        Reveal,     // show all answers (anonymous)
        Vote,       // players vote for the best
        Scoreboard, // show scores + winning answer
        Final       // game over, show winner
    }

    /// <summary>
    /// The server-authoritative party game state machine.
    ///
    /// The server owns ALL state (phase, round, prompt, answers, votes, scores)
    /// and broadcasts it via SyncVars/SyncLists. Clients only send intents
    /// ([ServerRpc] SubmitAnswer / SubmitVote) and render the synced state.
    ///
    /// Setup:
    ///   1. Create an empty "GameFlow" in MainGame, add this component.
    ///   2. (Optional) assign a PromptDeck; otherwise built-in prompts are used.
    ///   3. Add the GameUI component to the same object (or a child).
    /// </summary>
    public class GameFlow : NetworkBehaviour
    {
        [Header("Timing (seconds)")]
        [SerializeField] private float _answerTime = 30f;
        [SerializeField] private float _revealTime = 6f;
        [SerializeField] private float _voteTime = 30f;
        [SerializeField] private float _scoreboardTime = 8f;
        [SerializeField] private float _finalTime = 12f;

        [Header("Rounds")]
        [SerializeField] private int _maxRounds = 3;

        [Header("Content")]
        [SerializeField] private PromptDeck _deck;

        // --- Synced state (server-authoritative) ---
        private readonly SyncVar<GamePhase> _phase = new SyncVar<GamePhase>(GamePhase.Waiting);
        private readonly SyncVar<int> _round = new SyncVar<int>(0);
        private readonly SyncVar<string> _currentPrompt = new SyncVar<string>("");
        private readonly SyncList<string> _playerNames = new SyncList<string>();
        private readonly SyncList<int> _scores = new SyncList<int>();
        private readonly SyncList<string> _answers = new SyncList<string>();
        private readonly SyncList<int> _votes = new SyncList<int>();
        private readonly SyncList<bool> _hasVoted = new SyncList<bool>();
        private readonly SyncTimer _timer = new SyncTimer();

        // Server-only: maps each player to their index in the parallel lists.
        // We track this ourselves instead of relying on networkManager.players,
        // because that list removes the player BEFORE onPlayerLeft fires, which
        // can leave the parallel lists out of sync.
        private readonly Dictionary<PlayerID, int> _playerIndex = new Dictionary<PlayerID, int>();

        // --- Fallback prompts (used when no deck is assigned) ---
        private static readonly string[] FallbackPrompts =
        {
            "What's the worst thing to say at a wedding?",
            "What's the best name for a pet rock?",
            "What's the most overrated food?",
            "What should you never do on a first date?",
            "What's the worst superpower to have?",
            "What's the best excuse for being late to work?",
            "What's the most useless invention?",
            "What would you name a band that only plays elevator music?"
        };

        /// <summary>Fired whenever any synced state changes, so the UI can refresh.</summary>
        public event Action onStateChanged;

        // --- Read-only accessors for the UI ---
        public GamePhase Phase => _phase.value;
        public int Round => _round.value;
        public string CurrentPrompt => _currentPrompt.value;
        public int TimeRemaining => _timer.remainingInt;
        public int PlayerCount => _playerNames.Count;
        public IReadOnlyList<string> PlayerNames => _playerNames.list;
        public IReadOnlyList<int> Scores => _scores.list;
        public IReadOnlyList<string> Answers => _answers.list;
        public IReadOnlyList<int> Votes => _votes.list;
        public IReadOnlyList<bool> HasVoted => _hasVoted.list;
        public bool IsHost => isServer;

        /// <summary>Index of the local player in the parallel lists, or -1.</summary>
        public int LocalPlayerIndex
        {
            get
            {
                var local = networkManager.localPlayer;
                var players = networkManager.players;
                for (var i = 0; i < players.Count; i++)
                    if (players[i].id == local.id)
                        return i;
                return -1;
            }
        }

        protected override void OnSpawned(bool asServer)
        {
            // Subscribe to player events once the network manager is available.
            if (networkManager)
            {
                networkManager.onPlayerJoined += OnPlayerJoined;
                networkManager.onPlayerLeft += OnPlayerLeft;
            }

            if (asServer)
            {
                // Register existing players (in case we spawned after they connected).
                var players = networkManager.players;
                for (var i = 0; i < players.Count; i++)
                    AddPlayer(players[i]);
            }

            // Send our display name so the server can show real lobby names.
            // On the host this runs locally on the server; on a client it goes
            // to the server. Falls back to "Player N" if there's no lobby.
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
            _phase.onChanged += OnSyncVarChanged<GamePhase>;
            _round.onChanged += OnSyncVarChanged<int>;
            _currentPrompt.onChanged += OnSyncVarChanged<string>;
            _playerNames.onChanged += OnSyncListChanged<string>;
            _scores.onChanged += OnSyncListChanged<int>;
            _answers.onChanged += OnSyncListChanged<string>;
            _votes.onChanged += OnSyncListChanged<int>;
            _hasVoted.onChanged += OnSyncListChanged<bool>;
            _timer.onTimerSecondTick += OnTimerTick;
            _timer.onTimerEnd += OnTimerEnd;
        }

        private void OnDisable()
        {
            _phase.onChanged -= OnSyncVarChanged<GamePhase>;
            _round.onChanged -= OnSyncVarChanged<int>;
            _currentPrompt.onChanged -= OnSyncVarChanged<string>;
            _playerNames.onChanged -= OnSyncListChanged<string>;
            _scores.onChanged -= OnSyncListChanged<int>;
            _answers.onChanged -= OnSyncListChanged<string>;
            _votes.onChanged -= OnSyncListChanged<int>;
            _hasVoted.onChanged -= OnSyncListChanged<bool>;
            _timer.onTimerSecondTick -= OnTimerTick;
            _timer.onTimerEnd -= OnTimerEnd;
        }

        private void OnSyncVarChanged<T>(T _) => onStateChanged?.Invoke();
        private void OnSyncListChanged<T>(SyncListChange<T> _) => onStateChanged?.Invoke();
        private void OnTimerTick() => onStateChanged?.Invoke();

        // --- Player tracking (server only) ---

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            // Use isServer (actual current state) rather than asServer (event flag):
            // during server teardown asServer can be true while isServer is false,
            // which would make SyncList modifications fail.
            if (!isServer)
                return;

            AddPlayer(player);

            // Auto-start once we have enough players.
            if (_phase.value == GamePhase.Waiting && _playerNames.Count >= 2)
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
            _answers.RemoveAt(idx);
            _votes.RemoveAt(idx);
            _hasVoted.RemoveAt(idx);
            _playerIndex.Remove(player);

            // Reindex everyone who was after the removed player.
            var keys = new List<PlayerID>(_playerIndex.Keys);
            foreach (var p in keys)
                if (_playerIndex[p] > idx)
                    _playerIndex[p]--;
        }

        private void AddPlayer(PlayerID player)
        {
            if (_playerIndex.ContainsKey(player))
                return; // already tracked (e.g. registered in OnSpawned)

            _playerIndex[player] = _playerNames.Count;
            _playerNames.Add($"Player {_playerNames.Count + 1}");
            _scores.Add(0);
            _answers.Add("");
            _votes.Add(0);
            _hasVoted.Add(false);
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

        // --- Game flow (server only) ---

        /// <summary>Host-only: start the game from the waiting room.</summary>
        public void HostStartGame()
        {
            if (!isServer || _phase.value != GamePhase.Waiting)
                return;
            StartGame();
        }

        private void StartGame()
        {
            _round.value = 1;
            SetPhase(GamePhase.Prompt);
        }

        private void SetPhase(GamePhase newPhase)
        {
            if (isServer)
                Debug.Log($"[GameFlow] Phase -> {newPhase} (round {_round.value}, players {_playerNames.Count})");

            // Clear the previous round's answers/votes before a new prompt,
            // so no stale answers carry over between rounds.
            if (newPhase == GamePhase.Prompt)
                ClearRoundState();

            _phase.value = newPhase;

            switch (newPhase)
            {
                case GamePhase.Prompt:
                    _currentPrompt.value = GetPrompt();
                    _timer.StartTimer(_answerTime);
                    break;
                case GamePhase.Reveal:
                    _timer.StartTimer(_revealTime);
                    break;
                case GamePhase.Vote:
                    _timer.StartTimer(_voteTime);
                    break;
                case GamePhase.Scoreboard:
                    _timer.StartTimer(_scoreboardTime);
                    break;
                case GamePhase.Final:
                    _timer.StartTimer(_finalTime);
                    break;
            }
        }

        private void ClearRoundState()
        {
            // Reset in place so the parallel lists keep matching lengths.
            for (var i = 0; i < _answers.Count; i++)
            {
                _answers[i] = "";
                _votes[i] = 0;
                _hasVoted[i] = false;
            }
        }

        private void OnTimerEnd()
        {
            if (!isServer)
                return;

            switch (_phase.value)
            {
                case GamePhase.Prompt:
                    SetPhase(GamePhase.Reveal);
                    break;
                case GamePhase.Reveal:
                    // Skip voting if there aren't at least 2 answers to choose from.
                    if (CountValidAnswers() < 2)
                        SetPhase(GamePhase.Scoreboard);
                    else
                        SetPhase(GamePhase.Vote);
                    break;
                case GamePhase.Vote:
                    SetPhase(GamePhase.Scoreboard);
                    break;
                case GamePhase.Scoreboard:
                    if (_round.value < _maxRounds)
                    {
                        _round.value++;
                        SetPhase(GamePhase.Prompt);
                    }
                    else
                    {
                        SetPhase(GamePhase.Final);
                    }
                    break;
                case GamePhase.Final:
                    EndGame();
                    break;
            }
        }

        private string GetPrompt()
        {
            if (_deck != null && _deck.prompts.Count > 0)
                return _deck.prompts[UnityEngine.Random.Range(0, _deck.prompts.Count)];

            if (_deck != null)
                Debug.LogWarning("[GameFlow] PromptDeck is assigned but has no prompts — using built-in fallback prompts.", this);

            return FallbackPrompts[UnityEngine.Random.Range(0, FallbackPrompts.Length)];
        }

        private int CountValidAnswers()
        {
            var count = 0;
            for (var i = 0; i < _answers.Count; i++)
                if (!string.IsNullOrEmpty(_answers[i]))
                    count++;
            return count;
        }

        private void EndGame()
        {
            // GameOverBroadcaster.TryGet is internal to the purrlobby package, so
            // find the scene broadcaster directly and call its public EndGame().
            var broadcaster = FindAnyObjectByType<GameOverBroadcaster>();
            if (broadcaster != null)
                broadcaster.EndGame();
        }

        // --- Player intents (client -> server) ---

        /// <summary>UI calls this to submit an answer for the current round.</summary>
        public void SubmitAnswer(string answer)
        {
            SubmitAnswerRpc(answer);
        }

        /// <summary>UI calls this to vote for an answer (by index).</summary>
        public void SubmitVote(int answerIndex)
        {
            SubmitVoteRpc(answerIndex);
        }

        [ServerRpc(requireOwnership: false)]
        private void SubmitAnswerRpc(string answer, RPCInfo info = default)
        {
            if (_phase.value != GamePhase.Prompt)
                return;
            if (string.IsNullOrWhiteSpace(answer))
                return;

            var idx = IndexOfPlayer(info.sender);
            if (idx < 0)
                return;
            if (!string.IsNullOrEmpty(_answers[idx]))
                return; // already submitted

            _answers[idx] = answer.Trim();

            // If everyone has answered, skip the remaining time.
            if (AllSubmitted())
                SetPhase(GamePhase.Reveal);
        }

        [ServerRpc(requireOwnership: false)]
        private void SubmitVoteRpc(int answerIndex, RPCInfo info = default)
        {
            if (_phase.value != GamePhase.Vote)
                return;

            var voterIdx = IndexOfPlayer(info.sender);
            if (voterIdx < 0)
                return;
            if (_hasVoted[voterIdx])
                return; // already voted
            if (answerIndex < 0 || answerIndex >= _answers.Count)
                return;
            if (answerIndex == voterIdx)
                return; // can't vote for your own answer
            if (string.IsNullOrEmpty(_answers[answerIndex]))
                return; // nothing to vote for

            _votes[answerIndex]++;
            _scores[answerIndex]++; // the author earns a point per vote
            _hasVoted[voterIdx] = true;
        }

        private bool AllSubmitted()
        {
            for (var i = 0; i < _answers.Count; i++)
                if (string.IsNullOrEmpty(_answers[i]))
                    return false;
            return true;
        }
    }
}