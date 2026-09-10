using System;
using UnityEngine;
using UnityEngine.UI;

namespace Jam
{
    /// <summary>
    /// Builds and updates the party game UI at runtime (no editor UI assembly
    /// needed). Reads the synced state from GameFlow and shows the right panel
    /// for the current phase. Uses legacy uGUI (Text/InputField/Button) so there
    /// is no TextMeshPro dependency.
    ///
    /// Setup: add to the same GameObject as GameFlow (it auto-finds it), or
    /// assign the GameFlow reference manually.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        [SerializeField] private GameFlow _flow;

        // Root
        private Canvas _canvas;
        private Text _header;
        private Text _countdown;

        // Panels
        private GameObject _waitingPanel;
        private GameObject _promptPanel;
        private GameObject _revealPanel;
        private GameObject _votePanel;
        private GameObject _scoreboardPanel;
        private GameObject _finalPanel;

        // Vote rebuild guard (avoids rebuilding the list every timer tick)
        private string _lastVoteSignature = "";

        // Tracks the round so the answer input clears once per new prompt.
        private int _lastPromptRound = -1;

        // One-time diagnostic log on first refresh.
        private bool _loggedFirstRefresh;

        // Waiting
        private Text _waitingText;
        private Button _startButton;

        // Prompt
        private Text _promptText;
        private InputField _answerInput;
        private Button _submitButton;
        private Text _promptStatus;
        private Text _promptPlayers;

        // Reveal
        private Text _revealText;

        // Vote
        private Transform _voteList;
        private Text _voteStatus;

        // Scoreboard
        private Text _scoreboardText;

        // Final
        private Text _finalText;

        private void Awake()
        {
            if (_flow == null)
                _flow = GetComponent<GameFlow>();
            if (_flow == null)
                _flow = FindAnyObjectByType<GameFlow>();

            Debug.Log($"[GameUI] Awake: flow={( _flow != null ? _flow.name : "NULL")}");

            BuildUI();
        }

        private void OnEnable()
        {
            if (_flow != null)
                _flow.onStateChanged += Refresh;
        }

        private void OnDisable()
        {
            if (_flow != null)
                _flow.onStateChanged -= Refresh;
        }

        // ---------------------------------------------------------------
        // UI construction
        // ---------------------------------------------------------------

        private void BuildUI()
        {
            var canvasGo = new GameObject("GameUI Canvas");
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            // Root panel (dark background)
            var root = CreatePanel("Root", canvasGo.transform, new Color(0.08f, 0.08f, 0.1f, 1f));

            // Top bar (visible background) with phase indicator + countdown
            var topBar = CreatePanel("TopBar", root.transform, new Color(0.16f, 0.16f, 0.22f, 0.95f));
            SetAnchors(topBar.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -90), new Vector2(0, 0));

            _header = CreateText("Header", topBar.transform, 40);
            _header.alignment = TextAnchor.MiddleLeft;
            SetAnchors(_header.rectTransform, new Vector2(0, 0.5f), new Vector2(0.8f, 0.5f),
                new Vector2(20, -25), new Vector2(0, 25));

            _countdown = CreateText("Countdown", topBar.transform, 72);
            _countdown.alignment = TextAnchor.MiddleRight;
            SetAnchors(_countdown.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-180, -40), new Vector2(-20, 40));

            // --- Waiting ---
            _waitingPanel = CreatePanel("Waiting", root.transform, new Color(0, 0, 0, 0));
            _waitingText = CreateText("WaitingText", _waitingPanel.transform, 40);
            SetAnchors(_waitingText.rectTransform, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.6f),
                new Vector2(-600, -40), new Vector2(600, 40));
            _startButton = CreateButton("StartButton", _waitingPanel.transform, "Start Game", () => _flow.HostStartGame());
            SetAnchors(_startButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.4f), new Vector2(0.5f, 0.4f),
                new Vector2(-150, -30), new Vector2(150, 30));

            // --- Prompt ---
            _promptPanel = CreatePanel("Prompt", root.transform, new Color(0, 0, 0, 0));
            _promptText = CreateText("PromptText", _promptPanel.transform, 44);
            SetAnchors(_promptText.rectTransform, new Vector2(0.5f, 0.75f), new Vector2(0.5f, 0.75f),
                new Vector2(-700, -60), new Vector2(700, 60));
            _answerInput = CreateInputField("AnswerInput", _promptPanel.transform);
            SetAnchors(_answerInput.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-400, -35), new Vector2(400, 35));
            // Enter key submits, same as the button.
            _answerInput.onSubmit.AddListener(text =>
            {
                if (_flow != null)
                    _flow.SubmitAnswer(text);
            });
            _submitButton = CreateButton("SubmitButton", _promptPanel.transform, "Submit", () =>
            {
                if (_flow != null)
                    _flow.SubmitAnswer(_answerInput.text);
            });
            SetAnchors(_submitButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.35f), new Vector2(0.5f, 0.35f),
                new Vector2(-150, -30), new Vector2(150, 30));
            _promptStatus = CreateText("PromptStatus", _promptPanel.transform, 28);
            SetAnchors(_promptStatus.rectTransform, new Vector2(0.5f, 0.25f), new Vector2(0.5f, 0.25f),
                new Vector2(-400, -20), new Vector2(400, 20));

            // Player submission status (right side of the prompt panel, with a visible background)
            var playersBar = CreatePanel("PlayersBar", _promptPanel.transform, new Color(0.16f, 0.16f, 0.22f, 0.95f));
            SetAnchors(playersBar.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-420, -260), new Vector2(-20, 260));
            _promptPlayers = CreateText("PromptPlayers", playersBar.transform, 28);
            _promptPlayers.alignment = TextAnchor.UpperLeft;
            SetAnchors(_promptPlayers.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(20, -20), new Vector2(-20, -20));

            // --- Reveal ---
            _revealPanel = CreatePanel("Reveal", root.transform, new Color(0, 0, 0, 0));
            _revealText = CreateText("RevealText", _revealPanel.transform, 34);
            SetAnchors(_revealText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-700, -300), new Vector2(700, 300));

            // --- Vote ---
            _votePanel = CreatePanel("Vote", root.transform, new Color(0, 0, 0, 0));
            var voteListGo = new GameObject("VoteList", typeof(RectTransform));
            voteListGo.transform.SetParent(_votePanel.transform, false);
            _voteList = voteListGo.transform;
            var vlg = voteListGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = false;
            SetAnchors(_voteList as RectTransform, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
                new Vector2(-600, -300), new Vector2(600, 300));
            _voteStatus = CreateText("VoteStatus", _votePanel.transform, 28);
            SetAnchors(_voteStatus.rectTransform, new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.1f),
                new Vector2(-400, -20), new Vector2(400, 20));

            // --- Scoreboard ---
            _scoreboardPanel = CreatePanel("Scoreboard", root.transform, new Color(0, 0, 0, 0));
            _scoreboardText = CreateText("ScoreboardText", _scoreboardPanel.transform, 34);
            SetAnchors(_scoreboardText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-600, -300), new Vector2(600, 300));

            // --- Final ---
            _finalPanel = CreatePanel("Final", root.transform, new Color(0, 0, 0, 0));
            _finalText = CreateText("FinalText", _finalPanel.transform, 40);
            SetAnchors(_finalText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-600, -300), new Vector2(600, 300));

            // Ensure the top bar renders above the (transparent) panels.
            topBar.transform.SetAsLastSibling();

            Refresh();
        }

        // ---------------------------------------------------------------
        // Refresh (called on every state change)
        // ---------------------------------------------------------------

        private void Refresh()
        {
            if (_flow == null)
                return;

            if (!_loggedFirstRefresh)
            {
                _loggedFirstRefresh = true;
                Debug.Log($"[GameUI] First refresh: phase={_flow.Phase}, round={_flow.Round}, players={_flow.PlayerCount}");
            }

            var phase = _flow.Phase;
            _header.text = $"ROUND {Mathf.Max(1, _flow.Round)}  |  {PhaseLabel(phase)}";
            _countdown.text = _flow.TimeRemaining.ToString();

            SetActive(_waitingPanel, phase == GamePhase.Waiting);
            SetActive(_promptPanel, phase == GamePhase.Prompt);
            SetActive(_revealPanel, phase == GamePhase.Reveal);
            SetActive(_votePanel, phase == GamePhase.Vote);
            SetActive(_scoreboardPanel, phase == GamePhase.Scoreboard);
            SetActive(_finalPanel, phase == GamePhase.Final);

            // Reset the vote rebuild guard whenever we leave the vote phase.
            if (phase != GamePhase.Vote)
                _lastVoteSignature = "";

            switch (phase)
            {
                case GamePhase.Waiting:
                    RefreshWaiting();
                    break;
                case GamePhase.Prompt:
                    RefreshPrompt();
                    break;
                case GamePhase.Reveal:
                    RefreshReveal();
                    break;
                case GamePhase.Vote:
                    RefreshVote();
                    break;
                case GamePhase.Scoreboard:
                    RefreshScoreboard();
                    break;
                case GamePhase.Final:
                    RefreshFinal();
                    break;
            }
        }

        private void RefreshWaiting()
        {
            var names = string.Join("\n", _flow.PlayerNames);
            _waitingText.text = $"Waiting for players...\n({_flow.PlayerCount} connected)\n\n{names}";
            _startButton.gameObject.SetActive(_flow.IsHost);
        }

        private void RefreshPrompt()
        {
            // Clear the input once per new round (not on every refresh, which
            // would wipe the player's typing).
            if (_flow.Round != _lastPromptRound)
            {
                _lastPromptRound = _flow.Round;
                _answerInput.text = "";
            }

            _promptText.text = _flow.CurrentPrompt;

            var localIdx = _flow.LocalPlayerIndex;
            var submitted = localIdx >= 0 && localIdx < _flow.Answers.Count &&
                            !string.IsNullOrEmpty(_flow.Answers[localIdx]);

            _answerInput.gameObject.SetActive(!submitted);
            _submitButton.gameObject.SetActive(!submitted);
            _promptStatus.text = submitted ? "Submitted! Waiting for others..." : "Type your answer and hit Submit.";

            // Show who has submitted and who is still pending.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SUBMISSIONS");
            var count = Mathf.Min(_flow.PlayerNames.Count, _flow.Answers.Count);
            for (var i = 0; i < count; i++)
            {
                var done = !string.IsNullOrEmpty(_flow.Answers[i]);
                sb.AppendLine($"{(done ? "[X]" : "[ ]")} {_flow.PlayerNames[i]}");
            }
            _promptPlayers.text = sb.ToString();
        }

        private void RefreshReveal()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("THE ANSWERS");
            sb.AppendLine();
            for (var i = 0; i < _flow.Answers.Count; i++)
            {
                var a = _flow.Answers[i];
                sb.AppendLine($"{i + 1}. {(string.IsNullOrEmpty(a) ? "(no answer)" : a)}");
            }
            _revealText.text = sb.ToString();
        }

        private void RefreshVote()
        {
            var localIdx = _flow.LocalPlayerIndex;
            var voted = localIdx >= 0 && localIdx < _flow.HasVoted.Count && _flow.HasVoted[localIdx];

            // Only rebuild when the vote-relevant state actually changes, so the
            // list doesn't flicker on every timer tick.
            var sig = $"{localIdx}|{voted}|{string.Join(",", _flow.Answers)}|{string.Join(",", _flow.HasVoted)}";
            if (sig == _lastVoteSignature)
                return;
            _lastVoteSignature = sig;

            // Rebuild the vote list each time the data changes.
            for (var i = _voteList.childCount - 1; i >= 0; i--)
                Destroy(_voteList.GetChild(i).gameObject);

            var validCount = 0;
            for (var i = 0; i < _flow.Answers.Count; i++)
                if (!string.IsNullOrEmpty(_flow.Answers[i]))
                    validCount++;

            if (validCount < 2)
            {
                _voteStatus.text = "Not enough answers to vote on.";
                return;
            }

            for (var i = 0; i < _flow.Answers.Count; i++)
            {
                var answer = _flow.Answers[i];
                if (string.IsNullOrEmpty(answer))
                    continue;

                var row = CreatePanel($"Row{i}", _voteList, new Color(0.15f, 0.15f, 0.18f, 1f));
                var rowRect = row.GetComponent<RectTransform>();
                rowRect.sizeDelta = new Vector2(0, 60);

                var label = CreateText($"Label{i}", row.transform, 28);
                label.text = $"{i + 1}. {answer}";
                label.alignment = TextAnchor.MiddleLeft;
                SetAnchors(label.rectTransform, new Vector2(0, 0.5f), new Vector2(0.8f, 0.5f),
                    new Vector2(20, -25), new Vector2(0, 25));

                // Can't vote for your own answer, or if you already voted.
                var canVote = !voted && i != localIdx;
                var idx = i;
                var btn = CreateButton($"Vote{i}", row.transform, "Vote", () => _flow.SubmitVote(idx));
                btn.interactable = canVote;
                SetAnchors(btn.GetComponent<RectTransform>(), new Vector2(0.85f, 0.5f), new Vector2(0.85f, 0.5f),
                    new Vector2(-80, -25), new Vector2(80, 25));
            }

            _voteStatus.text = voted ? "Vote submitted!" : "Vote for the funniest answer (not your own).";
        }

        private void RefreshScoreboard()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SCORES");
            sb.AppendLine();

            // Guard against the parallel lists being momentarily out of sync.
            var count = Mathf.Min(_flow.PlayerNames.Count, _flow.Scores.Count, _flow.Votes.Count, _flow.Answers.Count);

            var best = -1;
            var bestVotes = -1;
            for (var i = 0; i < count; i++)
            {
                sb.AppendLine($"{_flow.PlayerNames[i]}: {_flow.Scores[i]} pts");
                if (_flow.Votes[i] > bestVotes)
                {
                    bestVotes = _flow.Votes[i];
                    best = i;
                }
            }

            sb.AppendLine();
            if (best >= 0 && !string.IsNullOrEmpty(_flow.Answers[best]))
                sb.AppendLine($"Best answer: \"{_flow.Answers[best]}\" ({bestVotes} votes)");

            _scoreboardText.text = sb.ToString();
        }

        private void RefreshFinal()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("GAME OVER");
            sb.AppendLine();

            var count = Mathf.Min(_flow.PlayerNames.Count, _flow.Scores.Count);

            var winner = -1;
            var bestScore = -1;
            for (var i = 0; i < count; i++)
            {
                sb.AppendLine($"{_flow.PlayerNames[i]}: {_flow.Scores[i]} pts");
                if (_flow.Scores[i] > bestScore)
                {
                    bestScore = _flow.Scores[i];
                    winner = i;
                }
            }

            sb.AppendLine();
            if (winner >= 0)
                sb.AppendLine($"{_flow.PlayerNames[winner]} wins!");

            _finalText.text = sb.ToString();
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static string PhaseLabel(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Waiting: return "Waiting";
                case GamePhase.Prompt: return "Answer";
                case GamePhase.Reveal: return "Reveal";
                case GamePhase.Vote: return "Vote";
                case GamePhase.Scoreboard: return "Scores";
                case GamePhase.Final: return "Final";
                default: return phase.ToString();
            }
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null)
                go.SetActive(active);
        }

        private static void SetAnchors(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private static Text CreateText(string name, Transform parent, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.9f, 0.7f, 0.1f, 1f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => onClick?.Invoke());

            var text = CreateText("Label", go.transform, 24);
            text.text = label;
            text.color = Color.black;
            SetAnchors(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }

        private static InputField CreateInputField(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.9f);
            var input = go.AddComponent<InputField>();
            input.targetGraphic = img;

            var text = CreateText("Text", go.transform, 28);
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            SetAnchors(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0));
            input.textComponent = text;

            return input;
        }
    }
}