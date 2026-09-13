using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jam
{
    /// <summary>
    /// Builds and updates the Split Canvas UI at runtime (no editor UI assembly).
    /// Reads synced state from SplitCanvasGame and shows the right panel for the
    /// current phase. Uses legacy uGUI (Text/InputField/Button/RawImage) so there
    /// is no TextMeshPro dependency.
    ///
    /// Setup: add to the same GameObject as SplitCanvasGame (it auto-finds it), or
    /// assign the reference manually.
    /// </summary>
    public class SplitCanvasUI : MonoBehaviour
    {
        [SerializeField] private SplitCanvasGame _flow;

        // Root
        private Canvas _canvas;
        private Text _header;
        private Text _countdown;

        // Panels
        private GameObject _waitingPanel;
        private GameObject _drawingPanel;
        private GameObject _guessingPanel;
        private GameObject _judgingPanel;
        private GameObject _scoreboardPanel;
        private GameObject _finalPanel;

        // Waiting
        private Text _waitingText;
        private Button _startButton;

        // Drawing
        private readonly Dictionary<int, DrawingCanvas> _canvasByIndex = new Dictionary<int, DrawingCanvas>();
        private readonly Dictionary<int, Text> _canvasLabel = new Dictionary<int, Text>();
        private readonly Dictionary<int, RawImage> _canvasRaw = new Dictionary<int, RawImage>();
        private readonly Dictionary<int, string> _lastPng = new Dictionary<int, string>();
        private readonly List<GameObject> _canvasWraps = new List<GameObject>();
        private Text _drawingStatus;
        private int _lastTick = -1;
        private bool _drawingSetupDone;
        private int _builtRound = -1;
        private int _currentActiveCanvas = -1;

        // Guessing
        private Transform _guessList;
        private Text _guessStatus;
        private Button _guessNextButton;
        private string _lastGuessSignature = "";
        private readonly Dictionary<int, InputField> _pendingGuessInputs = new Dictionary<int, InputField>();

        // Judging
        private Transform _judgeList;
        private Text _judgeStatus;
        private Button _judgeNextButton;
        private string _lastJudgeSignature = "";

        // Scoreboard
        private Text _scoreboardText;

        // Final
        private Text _finalText;
        private Transform _finalList;
        private string _lastFinalSignature = "";

        private void Awake()
        {
            if (_flow == null)
                _flow = GetComponent<SplitCanvasGame>();
            if (_flow == null)
                _flow = FindAnyObjectByType<SplitCanvasGame>();

            BuildUI();
        }

        private void OnEnable()
        {
            if (_flow != null)
            {
                _flow.onStateChanged += Refresh;
                _flow.onLocalTimerEnd += OnLocalTimerEnd;
            }
        }

        private void OnDisable()
        {
            if (_flow != null)
            {
                _flow.onStateChanged -= Refresh;
                _flow.onLocalTimerEnd -= OnLocalTimerEnd;
            }
        }

        /// <summary>Submit the canvas we were drawing on when the tick timer ends, or auto-submit guesses.</summary>
        private void OnLocalTimerEnd()
        {
            if (_flow == null)
                return;

            if (_flow.Phase == SplitPhase.Drawing)
            {
                if (_currentActiveCanvas >= 0 && _canvasByIndex.TryGetValue(_currentActiveCanvas, out var prev))
                {
                    var png = prev.GetPngBytes();
                    if (png != null)
                    {
                        _flow.SubmitDrawing(_currentActiveCanvas, png);
                        SplitLog.Log($"[SplitCanvasUI] Submitting canvas {_currentActiveCanvas}, pngLen={png.Length}");
                    }
                }
            }
            else if (_flow.Phase == SplitPhase.Guessing)
            {
                // Auto-submit any guesses the player typed but didn't press submit on.
                foreach (var kv in _pendingGuessInputs)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value.text))
                        _flow.SubmitGuess(kv.Key, kv.Value.text);
                }
            }
        }

        // ---------------------------------------------------------------
        // UI construction
        // ---------------------------------------------------------------

        private void BuildUI()
        {
            var canvasGo = new GameObject("SplitCanvasUI Canvas");
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            var root = CreatePanel("Root", canvasGo.transform, new Color(0.08f, 0.08f, 0.1f, 1f));

            // Top bar
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

            // --- Drawing ---
            _drawingPanel = CreatePanel("Drawing", root.transform, new Color(0, 0, 0, 0));
            _drawingStatus = CreateText("DrawingStatus", _drawingPanel.transform, 30);
            SetAnchors(_drawingStatus.rectTransform, new Vector2(0.5f, 0.92f), new Vector2(0.5f, 0.92f),
                new Vector2(-600, -25), new Vector2(600, 25));

            // --- Guessing ---
            _guessingPanel = CreatePanel("Guessing", root.transform, new Color(0, 0, 0, 0));
            var guessListGo = new GameObject("GuessList", typeof(RectTransform));
            guessListGo.transform.SetParent(_guessingPanel.transform, false);
            _guessList = guessListGo.transform;
            var vlg = guessListGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 16;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = false;
            SetAnchors(_guessList as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-700, -420), new Vector2(700, 420));
            _guessStatus = CreateText("GuessStatus", _guessingPanel.transform, 28);
            SetAnchors(_guessStatus.rectTransform, new Vector2(0.5f, 0.02f), new Vector2(0.5f, 0.02f),
                new Vector2(-400, -20), new Vector2(400, 20));

            _guessNextButton = CreateButton("NextButton", _guessingPanel.transform, "Start Judging", () => _flow.HostNextPhase());
            SetAnchors(_guessNextButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f),
                new Vector2(-150, -30), new Vector2(150, 30));

            // --- Judging ---
            _judgingPanel = CreatePanel("Judging", root.transform, new Color(0, 0, 0, 0));
            var judgeListGo = new GameObject("JudgeList", typeof(RectTransform));
            judgeListGo.transform.SetParent(_judgingPanel.transform, false);
            _judgeList = judgeListGo.transform;
            var jlg = judgeListGo.AddComponent<VerticalLayoutGroup>();
            jlg.spacing = 16;
            jlg.childForceExpandWidth = true;
            jlg.childForceExpandHeight = false;
            jlg.childControlHeight = false;
            SetAnchors(_judgeList as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-700, -420), new Vector2(700, 420));
            _judgeStatus = CreateText("JudgeStatus", _judgingPanel.transform, 28);
            SetAnchors(_judgeStatus.rectTransform, new Vector2(0.5f, 0.02f), new Vector2(0.5f, 0.02f),
                new Vector2(-400, -20), new Vector2(400, 20));
            _judgeNextButton = CreateButton("NextButton", _judgingPanel.transform, "Next Round", () => _flow.HostNextPhase());
            SetAnchors(_judgeNextButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f),
                new Vector2(-150, -30), new Vector2(150, 30));

            // --- Scoreboard ---
            _scoreboardPanel = CreatePanel("Scoreboard", root.transform, new Color(0, 0, 0, 0));
            _scoreboardText = CreateText("ScoreboardText", _scoreboardPanel.transform, 34);
            SetAnchors(_scoreboardText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-600, -300), new Vector2(600, 300));

            // --- Final ---
            _finalPanel = CreatePanel("Final", root.transform, new Color(0, 0, 0, 0));
            _finalText = CreateText("FinalText", _finalPanel.transform, 40);
            SetAnchors(_finalText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-600, -90), new Vector2(600, -10));

            var finalListGo = new GameObject("FinalList", typeof(RectTransform));
            finalListGo.transform.SetParent(_finalPanel.transform, false);
            _finalList = finalListGo.transform;
            var flg = finalListGo.AddComponent<VerticalLayoutGroup>();
            flg.spacing = 12;
            flg.childAlignment = TextAnchor.UpperCenter;
            flg.childForceExpandWidth = true;
            flg.childForceExpandHeight = false;
            flg.childControlHeight = false;
            SetAnchors(_finalList as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-700, -420), new Vector2(700, 420));

            topBar.transform.SetAsLastSibling();

            Refresh();
        }

        // ---------------------------------------------------------------
        // Refresh
        // ---------------------------------------------------------------

        private void Refresh()
        {
            if (_flow == null)
                return;

            var phase = _flow.Phase;
            _header.text = $"ROUND {Mathf.Max(1, _flow.Round)}  |  {PhaseLabel(phase)}";
            _countdown.text = _flow.TimeRemaining.ToString();

            SetActive(_waitingPanel, phase == SplitPhase.Waiting);
            SetActive(_drawingPanel, phase == SplitPhase.Drawing);
            SetActive(_guessingPanel, phase == SplitPhase.Guessing);
            SetActive(_judgingPanel, phase == SplitPhase.Judging);
            SetActive(_scoreboardPanel, phase == SplitPhase.Scoreboard);
            SetActive(_finalPanel, phase == SplitPhase.Final);

            switch (phase)
            {
                case SplitPhase.Waiting:
                    RefreshWaiting();
                    break;
                case SplitPhase.Drawing:
                    RefreshDrawing();
                    break;
                case SplitPhase.Guessing:
                    RefreshGuessing();
                    break;
                case SplitPhase.Judging:
                    RefreshJudging();
                    break;
                case SplitPhase.Scoreboard:
                    RefreshScoreboard();
                    break;
                case SplitPhase.Final:
                    RefreshFinal();
                    break;
            }

            // Reset the drawing setup flag whenever we leave the drawing phase, so a
            // new round re-runs the setup (and waits for the canvas lists to sync).
            if (phase != SplitPhase.Drawing)
                _drawingSetupDone = false;
        }

        private void RefreshWaiting()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Waiting for players... (need 3+ to start)");
            for (var i = 0; i < _flow.PlayerNames.Count; i++)
                sb.AppendLine($"  {_flow.PlayerNames[i]}");
            _waitingText.text = sb.ToString();
            _startButton.gameObject.SetActive(_flow.IsHost);
        }

        // ---------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------

        private void RefreshDrawing()
        {
            // Wait until the current round's canvas data has fully synced. The phase
            // SyncVar and canvas SyncLists arrive as separate messages, so the client
            // can enter Drawing before the prompts/PNGs are populated.
            if (!_flow.IsRoundReady)
                return;

            // New round -> throw away the previous round's canvases entirely and build
            // fresh, genuinely blank ones. This guarantees a clean slate for every
            // player regardless of how the synced lists arrived.
            if (_builtRound != _flow.Round)
            {
                _builtRound = _flow.Round;
                _drawingSetupDone = false;
                DestroyDrawingCanvases();
            }

            EnsureDrawingCanvases();
            if (_canvasByIndex.Count == 0)
                return; // local player index not known yet; retry on next state change

            if (!_drawingSetupDone)
            {
                // Just entered drawing (and the canvas lists are ready).
                _drawingSetupDone = true;
                _lastTick = _flow.Tick;
                _currentActiveCanvas = _flow.GetLocalActiveCanvas(_flow.Tick);
                ClearAllCanvases();
                UpdateInteractable();
                UpdateDrawingStatus();
                SplitLog.Log($"[SplitCanvasUI] Drawing start: round={_flow.Round}, localIdx={_flow.LocalPlayerIndex}, canvases={_flow.CanvasCount}, " +
                          $"subjects=[{string.Join(",", _flow.CanvasSubject)}], modifiers=[{string.Join(",", _flow.CanvasModifier)}]");
                return;
            }

            if (_flow.Tick != _lastTick)
            {
                // Tick advanced. The canvas we were drawing on was already submitted on
                // the timer end (OnLocalTimerEnd). Just switch to the new active canvas.
                _lastTick = _flow.Tick;
                _currentActiveCanvas = _flow.GetLocalActiveCanvas(_flow.Tick);
                SplitLog.Log($"[SplitCanvasUI] tick {_lastTick}: localIdx={_flow.LocalPlayerIndex}, players={_flow.PlayerCount}, activeCanvas={_currentActiveCanvas}");

                // Load ONLY the new active canvas (to get the partner's latest). Do NOT
                // reload the canvas we just submitted — the server may not have processed
                // it yet, and reloading would wipe our own strokes.
                if (_currentActiveCanvas >= 0 && _canvasByIndex.TryGetValue(_currentActiveCanvas, out var next))
                {
                    var cur = _flow.GetCanvasPng(_currentActiveCanvas);
                    _lastPng[_currentActiveCanvas] = cur;
                    LoadPng(next, cur);
                }

                UpdateInteractable();
                UpdateDrawingStatus();
                return;
            }

            // Same tick: live-update the non-active canvas if the partner drew.
            foreach (var kv in _canvasByIndex)
            {
                if (kv.Key == _currentActiveCanvas)
                    continue;
                var cur = _flow.GetCanvasPng(kv.Key);
                if (!_lastPng.TryGetValue(kv.Key, out var prev) || cur != prev)
                {
                    _lastPng[kv.Key] = cur;
                    LoadPng(kv.Value, cur);
                }
            }
        }

        private void EnsureDrawingCanvases()
        {
            if (_canvasByIndex.Count > 0)
                return;

            var p = _flow.LocalPlayerIndex;
            if (p < 0 || _flow.PlayerCount < 2)
            {
                if (p < 0)
                    Debug.LogWarning($"[SplitCanvasUI] LocalPlayerIndex is -1 (players={_flow.PlayerCount}). Canvases not created yet.");
                return;
            }

            var n = _flow.PlayerCount;
            var c1 = p;                       // canvas p (left member)
            var c2 = (p - 1 + n) % n;         // canvas (p-1)%n (right member)

            CreateDrawingCanvas(c1, 0);
            CreateDrawingCanvas(c2, 1);
        }

        private void CreateDrawingCanvas(int canvasIndex, int slot)
        {
            // Two clearly separated slots: left quarter and right quarter of the screen.
            var x = slot == 0 ? 0.25f : 0.75f;

            var wrap = new GameObject($"CanvasWrap{slot}", typeof(RectTransform));
            wrap.transform.SetParent(_drawingPanel.transform, false);
            SetAnchors(wrap.GetComponent<RectTransform>(), new Vector2(x, 0.5f), new Vector2(x, 0.5f),
                new Vector2(-300, -320), new Vector2(300, 320));

            // Canvas first (so the label renders on top of it, not behind).
            var rawGo = new GameObject($"Raw{slot}", typeof(RectTransform));
            rawGo.transform.SetParent(wrap.transform, false);
            var raw = rawGo.AddComponent<RawImage>();
            raw.color = Color.white;
            SetAnchors(raw.rectTransform, new Vector2(0.5f, 0.42f), new Vector2(0.5f, 0.42f),
                new Vector2(-256, -256), new Vector2(256, 256));

            var dc = rawGo.AddComponent<DrawingCanvas>();
            dc.Init(raw);
            dc.SetBrushColor(_flow.GetPlayerColor(_flow.LocalPlayerIndex));

            // Label above the canvas, rendered on top.
            var label = CreateText($"Label{slot}", wrap.transform, 26);
            SetAnchors(label.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-280, -70), new Vector2(280, -10));
            label.transform.SetAsLastSibling();

            _canvasByIndex[canvasIndex] = dc;
            _canvasLabel[canvasIndex] = label;
            _canvasRaw[canvasIndex] = raw;
            _lastPng[canvasIndex] = "";
            _canvasWraps.Add(wrap);
        }

        /// <summary>Destroy every drawing canvas widget so the next round rebuilds from scratch.</summary>
        private void DestroyDrawingCanvases()
        {
            foreach (var wrap in _canvasWraps)
                if (wrap != null)
                    Destroy(wrap);
            _canvasWraps.Clear();
            _canvasByIndex.Clear();
            _canvasLabel.Clear();
            _canvasRaw.Clear();
            _lastPng.Clear();
            _currentActiveCanvas = -1;
        }

        private void ReloadAllCanvases()
        {
            foreach (var kv in _canvasByIndex)
            {
                var cur = _flow.GetCanvasPng(kv.Key);
                _lastPng[kv.Key] = cur;
                LoadPng(kv.Value, cur);
            }
        }

        /// <summary>Clear the local canvases at the start of a round (robust against stale server PNGs).</summary>
        private void ClearAllCanvases()
        {
            foreach (var kv in _canvasByIndex)
            {
                kv.Value.Clear();
                // Sentinel: force the first real (round-verified) value to load, even if
                // the previous round's synced value happened to be identical.
                _lastPng[kv.Key] = "";
            }
        }

        private void UpdateInteractable()
        {
            foreach (var kv in _canvasByIndex)
            {
                var active = kv.Key == _currentActiveCanvas;
                kv.Value.interactable = active;

                if (_canvasRaw.TryGetValue(kv.Key, out var raw))
                {
                    // Dim the inactive canvas so it's clear which one you're drawing on.
                    raw.color = active ? Color.white : new Color(0.4f, 0.4f, 0.45f, 1f);

                    // Bring the active canvas to the front of its wrap so it's clickable.
                    if (active)
                        raw.transform.SetAsLastSibling();
                }
            }
        }

        private void UpdateDrawingStatus()
        {
            if (_currentActiveCanvas < 0)
            {
                _drawingStatus.text = "Partner drawing... wait for your turn.";
                _drawingStatus.color = Color.white;
            }
            else
            {
                _drawingStatus.text = $"YOUR TURN — draw: {GetLocalPromptDisplay(_currentActiveCanvas)}";
                // Tint with your brush color so you can see which color you're drawing in.
                _drawingStatus.color = _flow.GetPlayerBrightColor(_flow.LocalPlayerIndex);
            }

            foreach (var kv in _canvasByIndex)
            {
                var label = _flow.GetCanvasLabel(kv.Key);
                var prompt = GetLocalPromptDisplay(kv.Key);
                var turn = kv.Key == _currentActiveCanvas ? "  <YOUR TURN>" : "  <partner>";
                _canvasLabel[kv.Key].text = $"[{label}]{turn}\n{prompt}";
            }
        }

        /// <summary>The prompt as "known half + underlines for the unknown half".</summary>
        private string GetLocalPromptDisplay(int canvas)
        {
            var p = _flow.LocalPlayerIndex;
            var subj = _flow.GetSubject(canvas);
            var mod = _flow.GetModifier(canvas);
            const string blank = "________";

            if (_flow.CanvasLeft(canvas) == p)
                return $"\"{subj}\" + {blank}";
            if (_flow.CanvasRight(canvas) == p)
                return $"{blank} + \"{mod}\"";
            return $"{blank} + {blank}";
        }

        /// <summary>The full prompt tinted with each half's player color (brighter).</summary>
        private string GetTintedPrompt(int canvas)
        {
            var subj = _flow.GetSubject(canvas);
            var mod = _flow.GetModifier(canvas);
            var c1 = ColorUtility.ToHtmlStringRGB(_flow.GetPlayerBrightColor(_flow.CanvasLeft(canvas)));
            var c2 = ColorUtility.ToHtmlStringRGB(_flow.GetPlayerBrightColor(_flow.CanvasRight(canvas)));
            return $"<color=#{c1}>{subj}</color> + <color=#{c2}>{mod}</color>";
        }

        private static void LoadPng(DrawingCanvas dc, string base64)
        {
            if (string.IsNullOrEmpty(base64))
                return;
            try
            {
                dc.LoadImage(Convert.FromBase64String(base64));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SplitCanvasUI] Failed to load PNG: {e.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Guessing
        // ---------------------------------------------------------------

        private void RefreshGuessing()
        {
            if (!_flow.IsRoundReady)
                return;

            // Only rebuild the list when the underlying data changes, so the
            // player's in-progress typed guess isn't wiped every timer tick.
            var sig = BuildGuessSignature();
            if (sig != _lastGuessSignature)
            {
                _lastGuessSignature = sig;
                RebuildGuessList();
            }

            _guessStatus.text = "Guess the combined prompt of every canvas you did NOT draw on.";
            _guessNextButton.gameObject.SetActive(_flow.IsHost);
        }

        private string BuildGuessSignature()
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < _flow.CanvasPng.Count; i++)
                sb.Append(_flow.GetCanvasPng(i)?.Length ?? 0).Append('|');
            for (var i = 0; i < GuessCount; i++)
                sb.Append(SafeGuessText(i)).Append('|')
                  .Append(SafeGuessCanvas(i)).Append('|')
                  .Append(SafeGuessPlayer(i)).Append('|')
                  .Append(SafeGuessResult(i)).Append(';');
            return sb.ToString();
        }

        private void RebuildGuessList()
        {
            if (_guessList == null)
                return;

            // A rebuild is triggered whenever ANY client submits a guess (the guess
            // SyncLists change -> signature changes). Preserve the local player's
            // in-progress typed guesses so they aren't wiped by someone else's submit.
            var saved = new Dictionary<int, string>();
            foreach (var kv in _pendingGuessInputs)
                if (kv.Value != null)
                    saved[kv.Key] = kv.Value.text;

            for (var i = _guessList.childCount - 1; i >= 0; i--)
                Destroy(_guessList.GetChild(i).gameObject);

            _pendingGuessInputs.Clear();

            var n = _flow.CanvasCount;
            for (var c = 0; c < n; c++)
                BuildGuessRow(c);

            // Restore the saved text into the freshly built inputs.
            foreach (var kv in saved)
                if (_pendingGuessInputs.TryGetValue(kv.Key, out var input) && input != null)
                    input.text = kv.Value;
        }

        private void BuildGuessRow(int canvas)
        {
            var row = new GameObject($"GuessRow{canvas}", typeof(RectTransform));
            row.transform.SetParent(_guessList, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0, 150);

            // Canvas thumbnail
            var thumbGo = new GameObject("Thumb", typeof(RectTransform));
            thumbGo.transform.SetParent(row.transform, false);
            var thumb = thumbGo.AddComponent<RawImage>();
            thumb.color = Color.white;
            SetAnchors(thumb.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(0, -60), new Vector2(120, 60));
            var png = _flow.GetCanvasPng(canvas);
            if (!string.IsNullOrEmpty(png))
            {
                try
                {
                    var tex = new Texture2D(1, 1);
                    tex.LoadImage(Convert.FromBase64String(png));
                    thumb.texture = tex;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SplitCanvasUI] thumb load failed: {e.Message}");
                }
            }

            var participated = _flow.LocalParticipated(canvas);
            var label = CreateText("Label", row.transform, 24);
            label.alignment = TextAnchor.MiddleLeft;
            SetAnchors(label.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(130, -30), new Vector2(-10, -5));

            if (participated)
            {
                label.text = $"[{_flow.GetCanvasLabel(canvas)}] YOU DREW THIS. The prompt was: {GetTintedPrompt(canvas)}";
            }
            else if (HasLocalGuessed(canvas))
            {
                label.text = $"[{_flow.GetCanvasLabel(canvas)}] — submitted. Waiting for the host.";
            }
            else
            {
                label.text = $"[{_flow.GetCanvasLabel(canvas)}] — guess the prompt:";

                var input = CreateInputField("Input", row.transform);
                SetAnchors(input.GetComponent<RectTransform>(), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(130, -25), new Vector2(0, 25));
                _pendingGuessInputs[canvas] = input;

                var submit = CreateButton("Submit", row.transform, "Guess", () =>
                {
                    if (!string.IsNullOrWhiteSpace(input.text))
                        _flow.SubmitGuess(canvas, input.text);
                });
                SetAnchors(submit.GetComponent<RectTransform>(), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                    new Vector2(-160, -25), new Vector2(-10, 25));
            }
        }

        private bool HasLocalGuessed(int canvas)
        {
            var p = _flow.LocalPlayerIndex;
            if (p < 0)
                return false;
            for (var i = 0; i < GuessCount; i++)
                if (SafeGuessCanvas(i) == canvas && SafeGuessPlayer(i) == p)
                    return true;
            return false;
        }

        private void BuildJudgeButtons(Transform row, int canvas)
        {
            for (var i = 0; i < GuessCount; i++)
            {
                if (SafeGuessCanvas(i) != canvas)
                    continue;
                if (SafeGuessResult(i) != 0)
                    continue;

                var guesser = SafeGuessPlayer(i);
                var guesserName = guesser >= 0 && guesser < _flow.PlayerNames.Count ? _flow.PlayerNames[guesser] : "?";
                var text = CreateText("Guess", row.transform, 22);
                text.alignment = TextAnchor.MiddleLeft;
                text.text = $"  {guesserName}: \"{SafeGuessText(i)}\"";
                SetAnchors(text.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(130, 0), new Vector2(-10, 30));

                var idx = i;
                var full = CreateButton($"Full{idx}", row.transform, "Full", () => _flow.SubmitVote(idx, 1));
                SetAnchors(full.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0),
                    new Vector2(-360, 0), new Vector2(-250, 30));
                var half = CreateButton($"Half{idx}", row.transform, "Half", () => _flow.SubmitVote(idx, 2));
                SetAnchors(half.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0),
                    new Vector2(-240, 0), new Vector2(-130, 30));
                var wrong = CreateButton($"Wrong{idx}", row.transform, "Wrong", () => _flow.SubmitVote(idx, 3));
                SetAnchors(wrong.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0),
                    new Vector2(-120, 0), new Vector2(-10, 30));
            }
        }

        // ---------------------------------------------------------------
        // Judging
        // ---------------------------------------------------------------

        private void RefreshJudging()
        {
            if (!_flow.IsRoundReady)
                return;

            var sig = BuildJudgeSignature();
            if (sig != _lastJudgeSignature)
            {
                _lastJudgeSignature = sig;
                RebuildJudgeList();
            }

            _judgeStatus.text = "Vote on each guess: Full, Half, or Wrong.";
            _judgeNextButton.gameObject.SetActive(_flow.IsHost);
        }

        private string BuildJudgeSignature()
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < GuessCount; i++)
                sb.Append(SafeGuessText(i)).Append('|')
                  .Append(SafeGuessCanvas(i)).Append('|')
                  .Append(SafeGuessPlayer(i)).Append('|')
                  .Append(SafeGuessResult(i)).Append(';');
            for (var i = 0; i < VoteCount; i++)
                sb.Append(SafeVoteGuess(i)).Append('|')
                  .Append(SafeVotePlayer(i)).Append('|')
                  .Append(SafeVoteValue(i)).Append(';');
            return sb.ToString();
        }

        private void RebuildJudgeList()
        {
            if (_judgeList == null)
                return;

            for (var i = _judgeList.childCount - 1; i >= 0; i--)
                Destroy(_judgeList.GetChild(i).gameObject);

            for (var i = 0; i < GuessCount; i++)
                BuildJudgeRow(i);
        }

        private void BuildJudgeRow(int guessIndex)
        {
            var canvas = SafeGuessCanvas(guessIndex);
            var row = new GameObject($"JudgeRow{guessIndex}", typeof(RectTransform));
            row.transform.SetParent(_judgeList, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0, 170);

            // Canvas thumbnail
            var thumbGo = new GameObject("Thumb", typeof(RectTransform));
            thumbGo.transform.SetParent(row.transform, false);
            var thumb = thumbGo.AddComponent<RawImage>();
            thumb.color = Color.white;
            SetAnchors(thumb.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(0, -60), new Vector2(120, 60));
            var png = _flow.GetCanvasPng(canvas);
            if (!string.IsNullOrEmpty(png))
            {
                try
                {
                    var tex = new Texture2D(1, 1);
                    tex.LoadImage(Convert.FromBase64String(png));
                    thumb.texture = tex;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SplitCanvasUI] judge thumb load failed: {e.Message}");
                }
            }

            // Revealed prompt + guesser + guess text.
            var label = CreateText("Label", row.transform, 22);
            label.alignment = TextAnchor.MiddleLeft;
            var guesser = SafeGuessPlayer(guessIndex);
            var guesserName = guesser >= 0 && guesser < _flow.PlayerNames.Count ? _flow.PlayerNames[guesser] : "?";
            label.text = $"[{_flow.GetCanvasLabel(canvas)}] Prompt: {GetTintedPrompt(canvas)}\n{guesserName} guessed: \"{SafeGuessText(guessIndex)}\"";
            SetAnchors(label.rectTransform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(130, -40), new Vector2(-10, -5));

            // Vote buttons (unless the local player already voted on this guess).
            if (HasLocalVoted(guessIndex))
            {
                var voted = CreateText("Voted", row.transform, 22);
                voted.alignment = TextAnchor.MiddleLeft;
                voted.text = "  You voted.";
                SetAnchors(voted.rectTransform, new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(130, 0), new Vector2(-10, 30));
                return;
            }

            var full = CreateButton($"Full{guessIndex}", row.transform, "Full", () => _flow.SubmitVote(guessIndex, 1));
            SetAnchors(full.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-360, 0), new Vector2(-250, 30));
            var half = CreateButton($"Half{guessIndex}", row.transform, "Half", () => _flow.SubmitVote(guessIndex, 2));
            SetAnchors(half.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-240, 0), new Vector2(-130, 30));
            var wrong = CreateButton($"Wrong{guessIndex}", row.transform, "Wrong", () => _flow.SubmitVote(guessIndex, 3));
            SetAnchors(wrong.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-120, 0), new Vector2(-10, 30));
        }

        private int VoteCount => Mathf.Min(
            _flow.VoteGuess.Count,
            Mathf.Min(_flow.VotePlayer.Count, _flow.VoteValue.Count));

        private int SafeVoteGuess(int i) => (i >= 0 && i < _flow.VoteGuess.Count) ? _flow.VoteGuess[i] : -1;
        private int SafeVotePlayer(int i) => (i >= 0 && i < _flow.VotePlayer.Count) ? _flow.VotePlayer[i] : -1;
        private int SafeVoteValue(int i) => (i >= 0 && i < _flow.VoteValue.Count) ? _flow.VoteValue[i] : 0;

        private bool HasLocalVoted(int guessIndex)
        {
            var p = _flow.LocalPlayerIndex;
            if (p < 0)
                return false;
            for (var i = 0; i < VoteCount; i++)
                if (SafeVoteGuess(i) == guessIndex && SafeVotePlayer(i) == p)
                    return true;
            return false;
        }

        // ---------------------------------------------------------------
        // Scoreboard / Final
        // ---------------------------------------------------------------

        private void RefreshScoreboard()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SCORES");
            for (var i = 0; i < _flow.PlayerNames.Count; i++)
                sb.AppendLine($"  {_flow.PlayerNames[i]}: {SafeGet(_flow.Scores, i)}");
            _scoreboardText.text = sb.ToString();
        }

        private void RefreshFinal()
        {
            var best = -1;
            var bestScore = int.MinValue;
            for (var i = 0; i < _flow.Scores.Count; i++)
            {
                var s = _flow.Scores[i];
                if (s > bestScore)
                {
                    bestScore = s;
                    best = i;
                }
            }

            var name = best >= 0 && best < _flow.PlayerNames.Count ? _flow.PlayerNames[best] : "?";
            _finalText.text = $"GAME OVER\n\n{name} wins with {bestScore} points!";

            // Rebuild the gallery only when the archive changes.
            var sig = BuildFinalSignature();
            if (sig == _lastFinalSignature)
                return;
            _lastFinalSignature = sig;
            RebuildFinalGallery();
        }

        private string BuildFinalSignature()
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < _flow.ArchiveCount; i++)
                sb.Append(_flow.ArchiveRound(i)).Append('|')
                  .Append(_flow.ArchiveCanvas(i)).Append('|')
                  .Append(_flow.ArchivePng(i)?.Length ?? 0).Append(';');
            return sb.ToString();
        }

        private void RebuildFinalGallery()
        {
            if (_finalList == null)
                return;

            for (var i = _finalList.childCount - 1; i >= 0; i--)
                Destroy(_finalList.GetChild(i).gameObject);

            var lastRound = -1;
            for (var i = 0; i < _flow.ArchiveCount; i++)
            {
                var r = _flow.ArchiveRound(i);
                if (r != lastRound)
                {
                    lastRound = r;
                    var head = CreateText($"RoundHead{r}", _finalList, 30);
                    head.alignment = TextAnchor.MiddleCenter;
                    head.text = $"--- Round {r} ---";
                    SetAnchors(head.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(-600, -20), new Vector2(600, 20));
                }

                BuildFinalRow(i);
            }
        }

        private void BuildFinalRow(int index)
        {
            var row = new GameObject($"FinalRow{index}", typeof(RectTransform));
            row.transform.SetParent(_finalList, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0, 120);

            var thumbGo = new GameObject("Thumb", typeof(RectTransform));
            thumbGo.transform.SetParent(row.transform, false);
            var thumb = thumbGo.AddComponent<RawImage>();
            thumb.color = Color.white;
            SetAnchors(thumb.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(0, -55), new Vector2(110, 55));
            var png = _flow.ArchivePng(index);
            if (!string.IsNullOrEmpty(png))
            {
                try
                {
                    var tex = new Texture2D(1, 1);
                    tex.LoadImage(Convert.FromBase64String(png));
                    thumb.texture = tex;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SplitCanvasUI] final thumb load failed: {e.Message}");
                }
            }

            var label = CreateText("Label", row.transform, 24);
            label.alignment = TextAnchor.MiddleLeft;
            label.text = $"[{_flow.GetCanvasLabel(_flow.ArchiveCanvas(index))}]  {_flow.ArchiveSubject(index)} + {_flow.ArchiveModifier(index)}";
            SetAnchors(label.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(120, -20), new Vector2(-10, 20));
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static string SafeGet(IReadOnlyList<string> list, int i)
        {
            return (i >= 0 && i < list.Count) ? list[i] : "";
        }

        private static int SafeGet(IReadOnlyList<int> list, int i)
        {
            return (i >= 0 && i < list.Count) ? list[i] : 0;
        }

        // The four guess lists are parallel SyncLists that sync as separate
        // messages, so they can be momentarily different lengths on clients.
        // Always iterate to the minimum and bounds-check each access.
        private int GuessCount => Mathf.Min(
            _flow.GuessText.Count,
            Mathf.Min(_flow.GuessCanvas.Count,
            Mathf.Min(_flow.GuessPlayer.Count, _flow.GuessResult.Count)));

        private string SafeGuessText(int i) => (i >= 0 && i < _flow.GuessText.Count) ? _flow.GuessText[i] : "";
        private int SafeGuessCanvas(int i) => (i >= 0 && i < _flow.GuessCanvas.Count) ? _flow.GuessCanvas[i] : -1;
        private int SafeGuessPlayer(int i) => (i >= 0 && i < _flow.GuessPlayer.Count) ? _flow.GuessPlayer[i] : -1;
        private int SafeGuessResult(int i) => (i >= 0 && i < _flow.GuessResult.Count) ? _flow.GuessResult[i] : 0;

        private static string PhaseLabel(SplitPhase phase)
        {
            switch (phase)
            {
                case SplitPhase.Waiting: return "WAITING";
                case SplitPhase.Drawing: return "DRAWING";
                case SplitPhase.Guessing: return "GUESSING";
                case SplitPhase.Judging: return "JUDGING";
                case SplitPhase.Scoreboard: return "SCORES";
                case SplitPhase.Final: return "FINAL";
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
            // Text must not block clicks on the drawing canvas beneath it.
            text.raycastTarget = false;
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

            var text = CreateText("Label", go.transform, 22);
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
            img.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            var input = go.AddComponent<InputField>();
            input.targetGraphic = img;

            var text = CreateText("Text", go.transform, 24);
            text.color = Color.black;
            text.alignment = TextAnchor.MiddleLeft;
            SetAnchors(text.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(10, -15), new Vector2(-10, 15));
            input.textComponent = text;

            var placeholder = CreateText("Placeholder", go.transform, 24);
            placeholder.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.text = "Type your guess...";
            SetAnchors(placeholder.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(10, -15), new Vector2(-10, 15));
            input.placeholder = placeholder;

            return input;
        }
    }
}