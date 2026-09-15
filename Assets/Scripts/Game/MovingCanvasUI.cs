using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jam
{
    /// <summary>
    /// Builds and updates the Moving Canvas UI at runtime (no editor UI assembly).
    /// Reads synced state from MovingCanvasGame and shows the right panel for the
    /// current phase. Uses legacy uGUI (Text/InputField/Button/RawImage) and reads
    /// all colors/sprites from a GameTheme so the whole game can be re-skinned.
    ///
    /// Setup: add to the same GameObject as MovingCanvasGame (it auto-finds it), or
    /// assign the reference manually.
    /// </summary>
    public class MovingCanvasUI : MonoBehaviour
    {
        [SerializeField] private MovingCanvasGame _flow;
        [SerializeField] private GameTheme _theme;
        [SerializeField] private GameSFX _sfx;
        [SerializeField] private ButtonStyle _buttonStyle;

        // Root
        private Canvas _canvas;
        private Text _header;
        private Text _countdown;
        private RectTransform _timerBar;
        private float _timerBarFullWidth = 1920f;

        // Panels
        private GameObject _waitingPanel;
        private GameObject _drawingPanel;
        private GameObject _guessingPanel;
        private GameObject _scoreboardPanel;
        private GameObject _finalPanel;

        // Waiting
        private Text _waitingText;
        private Button _startButton;
        private Transform _waitingList;

        // Tutorial
        private GameObject _tutorialPanel;
        private Image _tutorialImage;
        private Text _tutorialText;
        private Button _tutorialButton;

        // Drawing
        private StrokeCanvas _drawCanvas;
        private BrushCursor _brushCursor;
        private CanvasMover _drawMover;
        private CanvasModeProps _modeProps;
        private bool _canvasWasSpringIntro;
        private MovementAudio _movementAudio;
        private RectTransform _canvasWrap;
        private RectTransform _rootRt;
        private RectTransform _rawRt;
        private RectTransform _drawRt;
        private RectTransform _shadowRt;
        private RectTransform _borderRt;
        private RectTransform _backdropRt;
        private Image _drawingPanelImage;
        private Image _modeBackground;
        private Image _waitingBg;
        private Image _tutorialBg;
        private Image _guessingBg;
        private Image _scoreboardBg;
        private Image _finalBg;
        private Sprite _pillSprite;
        private int _pillRadius = -1;
        private int _lastScreenW = -1;
        private int _lastScreenH = -1;
        private MovementMode _currentMode;
        private Text _reasonText;
        private Button _undoButton;
        private Button _submitButton;
        private CanvasGroup _undoGroup;
        private CanvasGroup _submitGroup;
        private Text _introText;
        private Text _introLabelText;
        private CanvasGroup _countdownGroup;
        private Text _countdownText;
        private int _builtRound = -1;
        private bool _drawingSetupDone;
        private bool _submittedThisRound;
        private int _lastDrawRestart = -1;

        // Guessing
        private StrokeCanvas _revealCanvas;
        private RectTransform _revealWrap;
        private RectTransform _revealRawRt;
        private RectTransform _revealBorderRt;
        private Transform _guessList;
        private InputField _guessInput;
        private Button _guessSubmit;
        private Text _guessStatus;
        private Text _revealPromptText;
        private Text _revealPromptLabel;
        private Image _revealPromptBg;
        private Text _whatIsThisText;
        private CanvasGroup _guessingGroup;
        private string _lastGuessSignature = "";
        private int _lastGuessCanvas = -1;
        private int _lastGuessCount = 0;
        private int _lastGuessRound = -1;

        // Scoreboard
        private Text _scoreboardText;
        private CanvasGroup _scoreboardGroup;
        private Transform _scoreboardList;
        private string _lastScoreboardSignature = "";

        // Final
        private Text _finalText;
        private Transform _finalList;
        private CanvasGroup _finalGroup;
        private string _lastFinalSignature = "";
        private Transform _finalScoresList;
        private RectTransform _showcaseScroll;
        private RectTransform _showcaseContent;
        private Button _endGameButton;
        private bool _showcaseStarted;

        // Phase tracking (for entry transitions)
        private MovingPhase _lastPhase = MovingPhase.Waiting;

        private GameTheme Theme => _theme != null ? _theme : DefaultTheme;

        // Ease-out curve (fast start, slow end) for slide/pop animations.
        private static readonly AnimationCurve EaseOut = new AnimationCurve(
            new Keyframe(0f, 0f, 2f, 2f),
            new Keyframe(1f, 1f, 0f, 0f));

        private static GameTheme _defaultTheme;
        private static GameTheme DefaultTheme
        {
            get
            {
                if (_defaultTheme == null)
                {
                    _defaultTheme = ScriptableObject.CreateInstance<GameTheme>();
                }
                return _defaultTheme;
            }
        }

        private void Awake()
        {
            if (_flow == null)
                _flow = GetComponent<MovingCanvasGame>();
            if (_flow == null)
                _flow = FindAnyObjectByType<MovingCanvasGame>();

            SFX.SetBank(_sfx);
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

        /// <summary>Auto-submit the drawing when the draw timer ends.</summary>
        private void OnLocalTimerEnd()
        {
            if (_flow == null)
                return;

            if (_flow.Phase == MovingPhase.Drawing)
                SubmitDrawing();
            else if (_flow.Phase == MovingPhase.Guessing)
                SubmitGuess();
        }

        /// <summary>Enable/disable movement ambience (loops + trampoline boing).</summary>
        private void SetMovementAudio(bool active)
        {
            if (_movementAudio != null)
                _movementAudio.Active = active;
        }

        // ---------------------------------------------------------------
        // UI construction
        // ---------------------------------------------------------------

        private void BuildUI()
        {
            var canvasGo = new GameObject("MovingCanvasUI Canvas");
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f; // responsive: balances portrait/landscape
            canvasGo.AddComponent<GraphicRaycaster>();

            var root = CreatePanel("Root", canvasGo.transform, Theme.background);

            // Top bar
            var topBar = CreatePanel("TopBar", root.transform, Theme.panel, Theme.panelSprite);
            SetAnchors(topBar.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(0, -90), new Vector2(0, 0));

            _header = CreateText("Header", topBar.transform, 40, Theme.text);
            _header.alignment = TextAnchor.MiddleLeft;
            SetAnchors(_header.rectTransform, new Vector2(0, 0.5f), new Vector2(0.8f, 0.5f),
                new Vector2(20, -25), new Vector2(0, 25));

            _countdown = CreateText("Countdown", topBar.transform, 72, Theme.text);
            _countdown.alignment = TextAnchor.MiddleRight;
            SetAnchors(_countdown.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-180, -40), new Vector2(-20, 40));

            // Timer bar under the top bar (shrinks right-to-left as the phase timer runs down).
            var timerBarGo = CreatePanel("TimerBar", root.transform, Theme.accent);
            _timerBar = timerBarGo.GetComponent<RectTransform>();
            var timerBarRt = _timerBar;
            timerBarRt.anchorMin = new Vector2(0, 1);
            timerBarRt.anchorMax = new Vector2(0, 1);
            timerBarRt.pivot = new Vector2(0, 0.5f);
            timerBarRt.anchoredPosition = new Vector2(0, -92);
            timerBarRt.sizeDelta = new Vector2(1920, 8);

            // --- Waiting ---
            _waitingPanel = CreatePanel("Waiting", root.transform, new Color(0, 0, 0, 0));
            _waitingBg = CreatePhaseBackground(_waitingPanel.transform);
            _waitingText = CreateText("WaitingText", _waitingPanel.transform, 40, Theme.text);
            SetAnchors(_waitingText.rectTransform, new Vector2(0.5f, 0.85f), new Vector2(0.5f, 0.85f),
                new Vector2(-600, -40), new Vector2(600, 40));
            _startButton = CreateButton("StartButton", _waitingPanel.transform, "Start Game", Theme.accent, () => _flow.HostStartGame());
            SetAnchors(_startButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.4f), new Vector2(0.5f, 0.4f),
                new Vector2(-240, -60), new Vector2(240, 60));

            var waitingListGo = new GameObject("WaitingList", typeof(RectTransform));
            waitingListGo.transform.SetParent(_waitingPanel.transform, false);
            _waitingList = waitingListGo.transform;
            var wlg = waitingListGo.AddComponent<VerticalLayoutGroup>();
            wlg.spacing = 8;
            wlg.childForceExpandWidth = true;
            wlg.childForceExpandHeight = false;
            wlg.childControlHeight = false;
            SetAnchors(_waitingList as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-400, -200), new Vector2(400, 200));

            // --- Tutorial ---
            _tutorialPanel = CreatePanel("Tutorial", root.transform, new Color(0, 0, 0, 0));
            _tutorialBg = CreatePhaseBackground(_tutorialPanel.transform);
            _tutorialImage = CreatePanel("TutorialImage", _tutorialPanel.transform, Color.white).GetComponent<Image>();
            if (Theme.tutorialImage != null)
            {
                _tutorialImage.sprite = Theme.tutorialImage;
                _tutorialImage.type = Image.Type.Simple;
            }
            SetAnchors(_tutorialImage.rectTransform, new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.7f),
                new Vector2(-400, -200), new Vector2(400, 200));

            _tutorialText = CreateText("TutorialText", _tutorialPanel.transform, 38, Theme.text);
            _tutorialText.alignment = TextAnchor.UpperCenter;
            _tutorialText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tutorialText.text = "Draw the prompt while your canvas is IN MOTION!\n\n" +
                "Each round the canvas moves differently.\n\n" +
                "Everyone draws at once, then you guess each other's drawings.";
            var ttRt = _tutorialText.rectTransform;
            ttRt.anchorMin = new Vector2(0.5f, 1f);
            ttRt.anchorMax = new Vector2(0.5f, 1f);
            ttRt.pivot = new Vector2(0.5f, 1f);
            ttRt.sizeDelta = new Vector2(1200, 240);
            ttRt.anchoredPosition = new Vector2(0f, -400f); // placeholder; SizeTutorial corrects

            _tutorialButton = CreateButton("TutorialButton", _tutorialPanel.transform, "we get it!", Theme.accent, () => _flow.HostStartDrawing());
            SetAnchors(_tutorialButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.12f),
                new Vector2(-200, -60), new Vector2(200, 60));
            SizeTutorial();

            // --- Drawing ---
            _drawingPanel = CreatePanel("Drawing", root.transform, new Color(0, 0, 0, 0));
            _drawingPanelImage = _drawingPanel.GetComponent<Image>();

            // Full-screen background for the per-mode sprite (covers, keeps aspect, crops edges).
            _modeBackground = CreatePanel("ModeBackground", _drawingPanel.transform, Color.white).GetComponent<Image>();
            var mbRt = _modeBackground.rectTransform;
            mbRt.anchorMin = new Vector2(0.5f, 0.5f);
            mbRt.anchorMax = new Vector2(0.5f, 0.5f);
            mbRt.pivot = new Vector2(0.5f, 0.5f);
            mbRt.anchoredPosition = Vector2.zero;
            _modeBackground.transform.SetAsFirstSibling();

            // The prompt starts large in the center (intro), then slides up to the top and stays.
            _introText = CreateText("IntroText", _drawingPanel.transform, 96, Theme.text);
            SetAnchors(_introText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-600, -60), new Vector2(600, 60));

            // Small "your prompt is..." label shown above the prompt during the intro.
            _introLabelText = CreateText("IntroLabel", _drawingPanel.transform, 42, Theme.textMuted, false);
            SetAnchors(_introLabelText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-600, 70), new Vector2(600, 130));
            _introLabelText.gameObject.AddComponent<CanvasGroup>();

            // _reasonText = CreateText("ReasonText", _drawingPanel.transform, 28, Theme.textMuted);
            // SetAnchors(_reasonText.rectTransform, new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.82f),
            //     new Vector2(-400, -20), new Vector2(400, 20));

            // The drawing canvas (built per round in RefreshDrawing). Initial size is a
            // placeholder; SizeDrawingCanvas() corrects it at draw time (after layout).
            _rootRt = root.GetComponent<RectTransform>();
            var canvasSize = 512f;

            var canvasWrap = new GameObject("CanvasWrap", typeof(RectTransform));
            canvasWrap.transform.SetParent(_drawingPanel.transform, false);
            _canvasWrap = canvasWrap.GetComponent<RectTransform>();
            _canvasWrap.anchorMin = new Vector2(0.5f, 0.55f);
            _canvasWrap.anchorMax = new Vector2(0.5f, 0.55f);
            _canvasWrap.pivot = new Vector2(0.5f, 0.5f);
            _canvasWrap.sizeDelta = new Vector2(canvasSize + 48, canvasSize + 48);

            // Moving container (CanvasMover). Border + shadow + drawing are children so
            // they all inherit the same transforms as the canvas.
            var rawGo = new GameObject("Raw", typeof(RectTransform));
            rawGo.transform.SetParent(canvasWrap.transform, false);
            _rawRt = rawGo.GetComponent<RectTransform>();
            _rawRt.anchorMin = new Vector2(0.5f, 0.5f);
            _rawRt.anchorMax = new Vector2(0.5f, 0.5f);
            _rawRt.sizeDelta = new Vector2(canvasSize, canvasSize);
            _drawMover = rawGo.AddComponent<CanvasMover>();

            // Drop shadow behind the drawing (child of the moving container). Its offset
            // is recomputed each frame so it stays put in screen space as the canvas moves.
            var shadow = CreatePanel("Shadow", rawGo.transform, new Color(0, 0, 0, 0.4f));
            _shadowRt = shadow.GetComponent<RectTransform>();
            SetAnchors(_shadowRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-canvasSize / 2, -canvasSize / 2), new Vector2(canvasSize / 2, canvasSize / 2));
            var shadowFollow = shadow.AddComponent<CanvasShadow>();
            shadowFollow.canvasRect = _rawRt;
            shadowFollow.offset = new Vector2(0.08f, -0.08f); // fraction of the canvas size

            // Border around the canvas (child of the moving container, BEHIND the drawing).
            if (Theme.borderSprite != null)
            {
                _borderRt = CreateBorder(rawGo.transform, new Vector2(canvasSize + 0, canvasSize + 0), Theme.borderSprite, Theme.canvasBorder).rectTransform;
            }

            // The drawing surface (RawImage) — on top of the shadow and border.
            var drawGo = new GameObject("Draw", typeof(RectTransform));
            drawGo.transform.SetParent(rawGo.transform, false);
            var raw = drawGo.AddComponent<RawImage>();
            raw.color = Color.white;
            _drawRt = raw.rectTransform;
            SetAnchors(_drawRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-canvasSize / 2, -canvasSize / 2), new Vector2(canvasSize / 2, canvasSize / 2));
            _drawCanvas = drawGo.AddComponent<StrokeCanvas>();
            _drawCanvas.Init(raw);
            _drawCanvas.onStrokeStart += () => SFX.Paint();

            // Decorative per-mode props (wheels, boat, record, springs, trampoline).
            _modeProps = rawGo.AddComponent<CanvasModeProps>();
            _modeProps.Init(_drawMover, Theme, _canvasWrap, _drawRt);

            _movementAudio = rawGo.AddComponent<MovementAudio>();
            _movementAudio.Init(_drawMover, _sfx);

            // Static background shape behind the moving canvas (does NOT move).
            var backdrop = new GameObject("CanvasBackdrop", typeof(RectTransform));
            backdrop.transform.SetParent(_drawingPanel.transform, false);
            _backdropRt = backdrop.GetComponent<RectTransform>();
            SetAnchors(_backdropRt, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f),
                new Vector2(-(canvasSize + 48) / 2, -(canvasSize + 48) / 2), new Vector2((canvasSize + 48) / 2, (canvasSize + 48) / 2));
            backdrop.transform.SetAsFirstSibling();
            var backdropImg = backdrop.AddComponent<Image>();
            backdropImg.color = new Color(0, 0, 0, 0.2f);

            // Brush cursor (optional) — a child of the full-screen drawing panel so it
            // follows the pointer in screen space (arm can reach the screen edge).
            if (Theme.brushCursorSprite != null)
            {
                _brushCursor = CreateBrushCursor(_drawingPanel.transform);
                _drawCanvas.SetBrushCursor(_brushCursor);
            }

            // Brush controls
            _undoButton = CreateButton("Undo", _drawingPanel.transform, "Undo", Theme.accent, () => _drawCanvas.Undo());
            SetAnchors(_undoButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.15f), new Vector2(0.5f, 0.15f),
                new Vector2(-320, -70), new Vector2(-80, 70));
            _undoGroup = _undoButton.gameObject.AddComponent<CanvasGroup>();
            _undoGroup.alpha = 0f;

            _submitButton = CreateButton("Submit", _drawingPanel.transform, "Submit", Theme.positive, SubmitDrawing);
            SetAnchors(_submitButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.15f), new Vector2(0.5f, 0.15f),
                new Vector2(-40, -70), new Vector2(320, 70));
            _submitGroup = _submitButton.gameObject.AddComponent<CanvasGroup>();
            _submitGroup.alpha = 0f;

            // Countdown overlay (3-2-1). No Image, so it doesn't block drawing input.
            var countdownGo = new GameObject("CountdownOverlay", typeof(RectTransform));
            countdownGo.transform.SetParent(_drawingPanel.transform, false);
            _countdownGroup = countdownGo.AddComponent<CanvasGroup>();
            _countdownGroup.alpha = 0f;
            SetAnchors(countdownGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _countdownText = CreateText("CountdownText", countdownGo.transform, 160, Theme.text);
            SetAnchors(_countdownText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-300, -80), new Vector2(300, 80));

            // --- Guessing ---
            _guessingPanel = CreatePanel("Guessing", root.transform, new Color(0, 0, 0, 0));
            _guessingBg = CreatePhaseBackground(_guessingPanel.transform);
            _guessingGroup = _guessingPanel.AddComponent<CanvasGroup>();

            // Reveal canvas — bigger, near the top.
            var revealWrap = new GameObject("RevealWrap", typeof(RectTransform));
            revealWrap.transform.SetParent(_guessingPanel.transform, false);
            _revealWrap = revealWrap.GetComponent<RectTransform>();
            SetAnchors(_revealWrap, new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f),
                new Vector2(-240, -240), new Vector2(240, 240));

            var revealRawGo = new GameObject("RevealRaw", typeof(RectTransform));
            revealRawGo.transform.SetParent(revealWrap.transform, false);
            var revealRaw = revealRawGo.AddComponent<RawImage>();
            revealRaw.color = Color.white;
            _revealRawRt = revealRaw.rectTransform;
            SetAnchors(_revealRawRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-200, -200), new Vector2(200, 200));
            _revealCanvas = revealRawGo.AddComponent<StrokeCanvas>();
            _revealCanvas.Init(revealRaw);
            _revealCanvas.interactable = false;

            // Border behind the reveal canvas (optional).
            if (Theme.borderSprite != null)
            {
                _revealBorderRt = CreateBorder(revealWrap.transform, new Vector2(426, 426), Theme.borderSprite, Theme.canvasBorder).rectTransform;
                _revealBorderRt.transform.SetAsFirstSibling();
            }

            SizeRevealCanvas();

            _guessStatus = CreateText("GuessStatus", _guessingPanel.transform, 28, Theme.text, false);
            SetAnchors(_guessStatus.rectTransform, new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.9f),
                new Vector2(-600, -20), new Vector2(600, 20));

            // "What is this?" label between the canvas and the guess list.
            _whatIsThisText = CreateText("WhatIsThis", _guessingPanel.transform, 40, Theme.text);
            SetAnchors(_whatIsThisText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-600, -20), new Vector2(600, 20));
            _whatIsThisText.text = "What is this?";
            _whatIsThisText.gameObject.SetActive(false);

            // Guess list under the canvas.
            var guessListGo = new GameObject("GuessList", typeof(RectTransform));
            guessListGo.transform.SetParent(_guessingPanel.transform, false);
            _guessList = guessListGo.transform;
            var vlg = guessListGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = false;
            SetAnchors(_guessList as RectTransform, new Vector2(0.5f, 0.42f), new Vector2(0.5f, 0.42f),
                new Vector2(-600, -180), new Vector2(600, 180));

            // Revealed correct prompt — floats over the guess list with a background.
            _revealPromptBg = CreatePanel("RevealPromptBg", _guessingPanel.transform, new Color(0, 0, 0, 0.85f)).GetComponent<Image>();
            SetAnchors(_revealPromptBg.GetComponent<RectTransform>(), new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f),
                new Vector2(-500, -110), new Vector2(500, 110));
            _revealPromptBg.gameObject.SetActive(false);

            _revealPromptText = CreateText("RevealPrompt", _guessingPanel.transform, 96, Theme.accent);
            SetAnchors(_revealPromptText.rectTransform, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f),
                new Vector2(-600, -60), new Vector2(600, -10));
            _revealPromptText.alignment = TextAnchor.MiddleCenter;
            _revealPromptText.gameObject.SetActive(false);
            _revealPromptText.transform.SetAsLastSibling();

            // Small "the prompt was…" label above the big prompt.
            _revealPromptLabel = CreateText("RevealPromptLabel", _guessingPanel.transform, 36, Theme.textMuted, false);
            SetAnchors(_revealPromptLabel.rectTransform, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f),
                new Vector2(-600, 20), new Vector2(600, 60));
            _revealPromptLabel.alignment = TextAnchor.MiddleCenter;
            _revealPromptLabel.gameObject.SetActive(false);
            _revealPromptLabel.transform.SetAsLastSibling();

            _guessInput = CreateInputField("GuessInput", _guessingPanel.transform, 36);
            SetAnchors(_guessInput.GetComponent<RectTransform>(), new Vector2(0.5f, 0.18f), new Vector2(0.5f, 0.18f),
                new Vector2(-500, -70), new Vector2(500, 70));
            _guessInput.onSubmit.AddListener(_ => SubmitGuess()); // Enter submits
            _guessSubmit = CreateButton("GuessSubmit", _guessingPanel.transform, "Guess", Theme.accent, SubmitGuess);
            SetAnchors(_guessSubmit.GetComponent<RectTransform>(), new Vector2(0.5f, 0.09f), new Vector2(0.5f, 0.09f),
                new Vector2(-220, -80), new Vector2(220, 80));

            // --- Scoreboard ---
            _scoreboardPanel = CreatePanel("Scoreboard", root.transform, new Color(0, 0, 0, 0));
            _scoreboardBg = CreatePhaseBackground(_scoreboardPanel.transform);
            _scoreboardGroup = _scoreboardPanel.AddComponent<CanvasGroup>();
            _scoreboardText = CreateText("ScoreboardText", _scoreboardPanel.transform, 38, Theme.text);
            SetAnchors(_scoreboardText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-600, -70), new Vector2(600, -10));

            var scoreboardListGo = new GameObject("ScoreboardList", typeof(RectTransform));
            scoreboardListGo.transform.SetParent(_scoreboardPanel.transform, false);
            _scoreboardList = scoreboardListGo.transform;
            var slg = scoreboardListGo.AddComponent<VerticalLayoutGroup>();
            slg.spacing = 8;
            slg.childForceExpandWidth = true;
            slg.childForceExpandHeight = false;
            slg.childControlHeight = false;
            SetAnchors(_scoreboardList as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-400, -300), new Vector2(400, 300));

            // --- Final ---
            _finalPanel = CreatePanel("Final", root.transform, new Color(0, 0, 0, 0));
            _finalBg = CreatePhaseBackground(_finalPanel.transform);
            _finalGroup = _finalPanel.AddComponent<CanvasGroup>();
            _finalText = CreateText("FinalText", _finalPanel.transform, 44, Theme.text);
            SetAnchors(_finalText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-600, -120), new Vector2(600, -20));

            // Scores list.
            var finalScoresGo = new GameObject("FinalScores", typeof(RectTransform));
            finalScoresGo.transform.SetParent(_finalPanel.transform, false);
            _finalScoresList = finalScoresGo.transform;
            var fslg = finalScoresGo.AddComponent<VerticalLayoutGroup>();
            fslg.spacing = 8;
            fslg.childForceExpandWidth = true;
            fslg.childForceExpandHeight = false;
            fslg.childControlHeight = false;
            SetAnchors(_finalScoresList as RectTransform, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f),
                new Vector2(-400, -200), new Vector2(400, 200));

            // Horizontal scrolling canvas showcase.
            var showcaseGo = new GameObject("Showcase", typeof(RectTransform));
            showcaseGo.transform.SetParent(_finalPanel.transform, false);
            _showcaseScroll = showcaseGo.GetComponent<RectTransform>();
            SetAnchors(_showcaseScroll, new Vector2(0.5f, 0.35f), new Vector2(0.5f, 0.35f),
                new Vector2(-700, -380), new Vector2(700, 380));
            showcaseGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("ShowcaseContent", typeof(RectTransform));
            contentGo.transform.SetParent(_showcaseScroll, false);
            _showcaseContent = contentGo.GetComponent<RectTransform>();
            _showcaseContent.anchorMin = new Vector2(0, 0.5f);
            _showcaseContent.anchorMax = new Vector2(0, 0.5f);
            _showcaseContent.pivot = new Vector2(0, 0.5f);
            _showcaseContent.anchoredPosition = Vector2.zero;

            // Host end button.
            _endGameButton = CreateButton("EndGame", _finalPanel.transform, "Finish!", Theme.accent, () => _flow.HostEndGame());
            SetAnchors(_endGameButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.09f), new Vector2(0.5f, 0.09f),
                new Vector2(-200, -60), new Vector2(200, 60));

            // Keep the HUD (top bar + timer line) above the phase panels — otherwise the
            // drawing phase's full-screen mode background covers the timer bar.
            topBar.transform.SetAsLastSibling();
            _timerBar.transform.SetAsLastSibling();

            Refresh();
        }

        // ---------------------------------------------------------------
        // Refresh
        // ---------------------------------------------------------------

        private void Update()
        {
            UpdateTimerBar();
            UpdateCountdown();
            CheckScreenSizeChange();
        }

        /// <summary>Update the top-bar countdown every frame so it appears the moment the timer starts.</summary>
        private void UpdateCountdown()
        {
            if (_flow == null)
                return;
            if (_flow.Phase == MovingPhase.Drawing && !_flow.IsTimerRunning)
                _countdown.text = ""; // hide during the intro
            else
                _countdown.text = _flow.TimeRemaining.ToString();
        }

        /// <summary>Shrink the top timer bar right-to-left as the phase timer runs down.</summary>
        private void UpdateTimerBar()
        {
            if (_timerBar == null || _flow == null)
                return;
            var total = _flow.PhaseTotalTime;
            var remaining = _flow.TimeRemainingFloat;
            float frac;
            if (!_flow.IsTimerRunning && _flow.Phase == MovingPhase.Drawing)
                frac = 1f; // during the intro, before the draw timer starts
            else
                frac = total > 0 ? Mathf.Clamp01(remaining / total) : 0f;
            var fullWidth = _rootRt != null && _rootRt.rect.width > 0 ? _rootRt.rect.width : 1920f;
            _timerBarFullWidth = fullWidth;
            _timerBar.sizeDelta = new Vector2(fullWidth * frac, _timerBar.sizeDelta.y);
        }

        /// <summary>Re-apply responsive background/canvas sizing when the screen size changes.</summary>
        private void CheckScreenSizeChange()
        {
            if (Screen.width == _lastScreenW && Screen.height == _lastScreenH)
                return;
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;

            if (_modeBackground != null && _modeBackground.gameObject.activeSelf)
            {
                var bg = Theme.GetModeBackground(_currentMode);
                if (bg != null)
                    SizeToCover(_modeBackground, bg);
            }
            if (_flow != null && _flow.Phase != MovingPhase.Drawing)
                ApplyPhaseBackground(_flow.Phase);
            SizeDrawingCanvas();
            SizeRevealCanvas();
            SizeGuessList();
            SizeGuessInput();
            SizeWhatIsThis();

            // Reposition the prompt to its settled top position.
            if (_introText != null && _introText.gameObject.activeSelf)
            {
                _introText.rectTransform.anchoredPosition = new Vector2(0f, GetPromptSettleY());
                _introText.fontSize = GetPromptSettleFontSize();
            }
        }

        private void Refresh()
        {
            if (_flow == null)
                return;

            // Make sure the player-list mirrors reflect the latest synced value before
            // any phase reads PlayerNames (PurrNet may deliver the initial SyncVar value
            // without firing onChanged, leaving a client's list empty).
            _flow.EnsurePlayerStateParsed();

            var phase = _flow.Phase;
            var modeLabel = phase == MovingPhase.Drawing ? $"  |  {_flow.CurrentMovementMode}" : "";
            _header.text = $"ROUND {Mathf.Max(1, _flow.Round)}  |  {PhaseLabel(phase)}{modeLabel}";
            // Hide the top-bar countdown during the drawing intro (it would show 0).
            if (phase == MovingPhase.Drawing && !_flow.IsTimerRunning)
                _countdown.text = "";
            else
                _countdown.text = _flow.TimeRemaining.ToString();

            SetActive(_waitingPanel, phase == MovingPhase.Waiting);
            SetActive(_tutorialPanel, phase == MovingPhase.Tutorial);
            SetActive(_drawingPanel, phase == MovingPhase.Drawing);
            SetActive(_guessingPanel, phase == MovingPhase.Guessing);
            SetActive(_scoreboardPanel, phase == MovingPhase.Scoreboard);
            SetActive(_finalPanel, phase == MovingPhase.Final);
            if (_timerBar != null)
                SetActive(_timerBar.gameObject, phase != MovingPhase.Waiting && phase != MovingPhase.Tutorial);

            // Non-drawing phases get their own full-screen background (the drawing phase
            // has its per-mode background applied via ApplyRoundTheming).
            if (phase != MovingPhase.Drawing)
                ApplyPhaseBackground(phase);

            // Play an entry transition when the phase changes.
            if (phase != _lastPhase)
            {
                _lastPhase = phase;
                PlayPhaseEntry(phase);
            }

            switch (phase)
            {
                case MovingPhase.Waiting:
                    RefreshWaiting();
                    break;
                case MovingPhase.Tutorial:
                    RefreshTutorial();
                    break;
                case MovingPhase.Drawing:
                    RefreshDrawing();
                    break;
                case MovingPhase.Guessing:
                    RefreshGuessing();
                    break;
                case MovingPhase.Scoreboard:
                    RefreshScoreboard();
                    break;
                case MovingPhase.Final:
                    RefreshFinal();
                    break;
            }
        }

        /// <summary>Fade the newly-entered phase's panel in.</summary>
        private void PlayPhaseEntry(MovingPhase phase)
        {
            SFX.PhaseChange();
            if (phase != MovingPhase.Drawing)
                SetMovementAudio(false); // stop movement FX when leaving the drawing phase
            switch (phase)
            {
                case MovingPhase.Guessing:
                    StartCoroutine(FadeIn(_guessingGroup));
                    break;
                case MovingPhase.Scoreboard:
                    StartCoroutine(FadeIn(_scoreboardGroup));
                    break;
                case MovingPhase.Final:
                    StartCoroutine(FadeIn(_finalGroup));
                    if (!_showcaseStarted)
                    {
                        _showcaseStarted = true;
                        StartCoroutine(ScrollShowcase());
                    }
                    break;
            }
        }

        private IEnumerator FadeIn(CanvasGroup group)
        {
            if (group == null)
                yield break;
            group.alpha = 0f;
            yield return UITween.FadeTo(this, group, 1f, 0.3f);
        }

        private void RefreshWaiting()
        {
            _waitingText.text = _flow.AllowSinglePlayer
                ? "Waiting for players... (host can start with 1 for testing)"
                : "Waiting for players... (need 2+ to start)";
            _startButton.gameObject.SetActive(_flow.IsHost);
            var canStart = _flow.PlayerCount >= 2 || (_flow.AllowSinglePlayer && _flow.PlayerCount >= 1);
            _startButton.interactable = canStart;
            RebuildWaitingList();
        }

        private void RefreshTutorial()
        {
            // Size responsively now that layout is settled (also done on creation/resize).
            SizeTutorial();
            // Only the host can advance past the tutorial.
            if (_tutorialButton != null)
                _tutorialButton.gameObject.SetActive(_flow.IsHost);
        }

        private void RebuildWaitingList()
        {
            if (_waitingList == null)
                return;
            for (var i = _waitingList.childCount - 1; i >= 0; i--)
                Destroy(_waitingList.GetChild(i).gameObject);
            for (var i = 0; i < _flow.PlayerNames.Count; i++)
                CreatePlayerRow(_waitingList, i, "");
        }

        // ---------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------

        /// <summary>
        /// Compute the settled Y (center-anchored) for the prompt so it sits in the gap
        /// between the canvas top edge and the bottom of the top bar. Responsive to screen
        /// height and canvas size, so it never hides behind the canvas on any screen.
        /// </summary>
        private float GetPromptSettleY()
        {
            if (_rootRt == null)
                return 340f;
            var h = _rootRt.rect.height;
            var canvasSize = _canvasWrap != null ? _canvasWrap.sizeDelta.x - 48f : 512f;
            // Canvas center is anchored at 0.55 of height; its top edge:
            var canvasTop = h * 0.55f + (canvasSize + 48f) * 0.5f;
            // Top bar is 90px tall at the very top.
            var topBarBottom = h - 90f;
            // Center the prompt in the gap between the canvas top and the top bar.
            var y = (canvasTop + topBarBottom) * 0.5f;
            return y - h * 0.5f;
        }

        /// <summary>Font size for the settled prompt, sized to fit the gap above the canvas.</summary>
        private int GetPromptSettleFontSize()
        {
            if (_rootRt == null)
                return 64;
            var h = _rootRt.rect.height;
            var canvasSize = _canvasWrap != null ? _canvasWrap.sizeDelta.x - 48f : 512f;
            var canvasTop = h * 0.55f + (canvasSize + 48f) * 0.5f;
            var topBarBottom = h - 90f;
            var gap = Mathf.Max(40f, topBarBottom - canvasTop);
            // Mobile has a big gap, so allow a much larger prompt there.
            return Mathf.Clamp((int)(gap * 0.6f), 40, 160);
        }

        /// <summary>Size the drawing canvas to up to 85% of the screen width on portrait/mobile.</summary>
        private void SizeDrawingCanvas()
        {
            if (_rootRt == null || _rawRt == null)
                return;
            var screenW = _rootRt.rect.width;
            var screenH = _rootRt.rect.height;
            var canvasSize = screenW < screenH
                ? Mathf.Min(screenW * 0.81f, screenH * 0.81f)
                : 512f;

            _canvasWrap.sizeDelta = new Vector2(canvasSize + 48, canvasSize + 48);
            _rawRt.sizeDelta = new Vector2(canvasSize, canvasSize);
            if (_shadowRt != null)
            {
                _shadowRt.offsetMin = new Vector2(-canvasSize / 2, -canvasSize / 2);
                _shadowRt.offsetMax = new Vector2(canvasSize / 2, canvasSize / 2);
            }
            if (_borderRt != null)
                _borderRt.sizeDelta = new Vector2(canvasSize + 48, canvasSize + 48);
            if (_drawRt != null)
            {
                _drawRt.offsetMin = new Vector2(-canvasSize / 2, -canvasSize / 2);
                _drawRt.offsetMax = new Vector2(canvasSize / 2, canvasSize / 2);
            }
            if (_backdropRt != null)
            {
                _backdropRt.offsetMin = new Vector2(-(canvasSize + 48) / 2, -(canvasSize + 48) / 2);
                _backdropRt.offsetMax = new Vector2((canvasSize + 48) / 2, (canvasSize + 48) / 2);
            }
            if (_modeProps != null)
                _modeProps.Resize();
            // Size the cursor proportionally to the canvas, and give it the canvas's
            // horizontal half-extent so its tilt maps to the drawing area.
            if (_brushCursor != null)
            {
                _brushCursor.SetSize(canvasSize * 0.6f);
                _brushCursor.SetCanvasHalfWidth((canvasSize + 48) * 0.5f);
            }
        }

        /// <summary>Size and position the reveal canvas to sit just below the top bar, up to 70% of screen width on portrait/mobile.</summary>
        private void SizeRevealCanvas()
        {
            if (_revealWrap == null || _revealRawRt == null)
                return;
            var screenW = _rootRt != null ? _rootRt.rect.width : 1920f;
            var screenH = _rootRt != null ? _rootRt.rect.height : 1080f;
            var size = screenW < screenH
                ? Mathf.Min(screenW * 0.7f, screenH * 0.7f)
                : 480f;

            // Sit just below the top bar (90px) responsively — pivot at top so the canvas's
            // top edge sits at -110, extending downward.
            _revealWrap.anchorMin = new Vector2(0.5f, 1f);
            _revealWrap.anchorMax = new Vector2(0.5f, 1f);
            _revealWrap.pivot = new Vector2(0.5f, 1f);
            _revealWrap.anchoredPosition = new Vector2(0f, -110f);
            _revealWrap.sizeDelta = new Vector2(size, size);

            _revealRawRt.sizeDelta = new Vector2(size - 80, size - 80);
            if (_revealBorderRt != null)
                _revealBorderRt.sizeDelta = new Vector2(size - 8, size - 8);
        }

        private void RefreshDrawing()
        {
            if (!_flow.IsRoundReady)
                return;

            // A restart (tuning) resets the timer and lets you draw again.
            if (_flow.DrawRestart != _lastDrawRestart)
            {
                _lastDrawRestart = _flow.DrawRestart;
                _drawingSetupDone = false;
                _submittedThisRound = false;
                _drawCanvas.Clear();
                _drawCanvas.SetBrushColor(Theme.GetPlayerColor(_flow.LocalPlayerIndex));
            }

            if (_builtRound != _flow.Round)
            {
                _builtRound = _flow.Round;
                _drawingSetupDone = false;
                _submittedThisRound = false;
                _drawCanvas.Clear();
                _drawCanvas.SetBrushColor(Theme.GetPlayerColor(_flow.LocalPlayerIndex));
            }

            if (!_drawingSetupDone)
            {
                SizeDrawingCanvas();
                _drawingSetupDone = true;
                _drawCanvas.interactable = true;
                // Apply the editable movement config from the game, then start moving.
                // Use the full draw time (NOT TimeRemaining, which is 0 before the timer
                // starts) so the movement ramps over the whole round.
                _flow.ApplyMovementConfig(_drawMover);
                // Spin/movement should last the whole drawing phase (intro + draw time),
                // otherwise it stops a few seconds before the round ends.
                _drawMover.SetMode(_flow.CurrentMovementMode, _flow.DrawTime + _flow.IntroTime);
                ApplyRoundTheming(_flow.CurrentMovementMode);
                if (_modeProps != null)
                    _modeProps.Configure(_flow.CurrentMovementMode);
                SetMovementAudio(false); // movement FX wait until the intro is over
                // _reasonText.text = ReasonForMode(_flow.CurrentMovementMode);
                _submitButton.gameObject.SetActive(false);
                _undoButton.gameObject.SetActive(false);

                StartCoroutine(PlayDrawingIntro());
            }
        }

        /// <summary>Position the prompt ends up at (top of the drawing area), relative to a center anchor.</summary>
        private static readonly Vector2 TopPromptPos = new Vector2(0f, 410f);

        /// <summary>Juice: prompt pops in center, slides up to the top and stays, then 3-2-1, then canvas slides in.</summary>
        private IEnumerator PlayDrawingIntro()
        {
            // Reset the prompt to center, large (so it replays on every round).
            _introText.gameObject.SetActive(true);
            _introText.rectTransform.anchoredPosition = Vector2.zero;
            _introText.rectTransform.localScale = Vector3.one;
            _introText.fontSize = 96;
            _introText.text = _flow.GetPrompt(_flow.LocalPlayerIndex).ToUpperInvariant();

            _introLabelText.gameObject.SetActive(true);
            _introLabelText.text = "your prompt is...";
            _introLabelText.rectTransform.anchoredPosition = new Vector2(0f, 100f); // reset from last round
            var labelGroup = _introLabelText.GetComponent<CanvasGroup>();
            if (labelGroup != null)
                labelGroup.alpha = 1f;

            // Canvas starts hidden below the screen; it slides in after the countdown.
            // Shake (jack-in-the-box): instead start tiny at the top of the box, then pop out.
            _canvasWrap.localScale = Vector3.one;
            _canvasWasSpringIntro = _flow.CurrentMovementMode == MovementMode.Shake
                && _modeProps != null && _modeProps.SpringCanvasStartAP.HasValue;
            if (_canvasWasSpringIntro)
            {
                _canvasWrap.anchoredPosition = _modeProps.SpringCanvasStartAP.Value;
                _canvasWrap.localScale = Vector3.one * 0.001f; // start really small
            }
            else
            {
                _canvasWrap.anchoredPosition = new Vector2(0f, -1700f);
            }

            // 1) Show "your prompt is..." first, hold for a second (prompt hidden).
            _introText.rectTransform.localScale = Vector3.zero;
            yield return new WaitForSeconds(1f);

            // 2) Pop in the prompt, hold for 2 seconds.
            _introText.rectTransform.localScale = Vector3.one * 0.6f;
            SFX.DrawingPrompt(); // the prompt is revealed to the player
            yield return UITween.ScaleTo(this, _introText.rectTransform, Vector3.one * 1.15f, 0.25f, EaseOut);
            yield return new WaitForSeconds(2f);

            // Slide the prompt up to the gap between the canvas top and the top bar, and
            // shrink to the persistent size. Position is computed responsively from the
            // canvas top edge so it never hides behind the canvas on any screen.
            var promptY = GetPromptSettleY();
            // Move the label up with the prompt while fading it out.
            StartCoroutine(MoveAndFadeLabel(promptY));
            yield return UITween.MoveTo(this, _introText.rectTransform, new Vector2(0f, promptY), 0.4f, EaseOut);
            // Lerp the font size down to the settled size (and scale to 1) so it doesn't snap.
            var fromSize = _introText.fontSize;
            var toSize = GetPromptSettleFontSize();
            var sizeT = 0f;
            while (sizeT < 0.3f)
            {
                sizeT += Time.deltaTime;
                var p = EaseOut.Evaluate(Mathf.Clamp01(sizeT / 0.3f));
                _introText.fontSize = (int)Mathf.Lerp(fromSize, toSize, p);
                _introText.rectTransform.localScale = Vector3.Lerp(Vector3.one * 1.15f, Vector3.one, p);
                yield return null;
            }
            _introText.fontSize = toSize;
            _introText.rectTransform.localScale = Vector3.one;

            // 3) 3-2-1 countdown in the center.
            _countdownGroup.gameObject.SetActive(true);
            _countdownGroup.alpha = 1f;
            for (var i = 3; i >= 1; i--)
            {
                _countdownText.text = i.ToString();
                _countdownText.rectTransform.localScale = Vector3.one * 0.6f;
                SFX.Tick(3 - i); // 0,1,2 -> the beeps climb in pitch as the count drops
                yield return UITween.ScaleTo(this, _countdownText.rectTransform, Vector3.one, 0.2f, EaseOut);
                yield return new WaitForSeconds(0.5f);
            }
            SFX.CountdownGo();
            yield return UITween.FadeTo(this, _countdownGroup, 0f, 0.3f);
            _countdownGroup.gameObject.SetActive(false);
            _introLabelText.gameObject.SetActive(false); // hide the label after the intro

            // Canvas slides in after the countdown (jack-in-the-box: pops out — grows to
            // full size while rising into place).
            if (_modeProps != null)
                _modeProps.SlideInWater();
            if (_canvasWasSpringIntro)
            {
                if (_modeProps != null)
                    _modeProps.OpenJackBox(); // swap the box to its "open" version
                UITween.ScaleTo(this, _canvasWrap, Vector3.one, 0.4f, EaseOut); // runs in parallel
                yield return UITween.MoveTo(this, _canvasWrap, Vector2.zero, 0.4f, EaseOut);
                _canvasWrap.localScale = Vector3.one;
            }
            else
            {
                yield return UITween.MoveTo(this, _canvasWrap, Vector2.zero, 0.4f, EaseOut);
            }

            // Show the brush controls (fade + slide in from below).
            ShowBrushControls();
            SetMovementAudio(true); // canvas is drawable now — start movement FX
        }

        /// <summary>Fade in and slide up the Undo/Submit buttons after the intro.</summary>
        private void ShowBrushControls()
        {
            if (_undoButton == null || _submitButton == null)
                return;
            _undoButton.gameObject.SetActive(true);
            _submitButton.gameObject.SetActive(true);

            var undoRt = _undoButton.GetComponent<RectTransform>();
            var submitRt = _submitButton.GetComponent<RectTransform>();
            var undoBase = undoRt.anchoredPosition;
            var submitBase = submitRt.anchoredPosition;

            _undoGroup.alpha = 0f;
            _submitGroup.alpha = 0f;
            undoRt.anchoredPosition = undoBase + new Vector2(0, -60);
            submitRt.anchoredPosition = submitBase + new Vector2(0, -60);

            StartCoroutine(AnimateBrushControls(undoRt, submitRt, undoBase, submitBase));
        }

        private IEnumerator AnimateBrushControls(RectTransform undoRt, RectTransform submitRt, Vector2 undoBase, Vector2 submitBase)
        {
            var duration = 0.3f;
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                var p = Mathf.Clamp01(t / duration);
                var e = EaseOut.Evaluate(p);
                undoRt.anchoredPosition = Vector2.Lerp(undoBase + new Vector2(0, -60), undoBase, e);
                submitRt.anchoredPosition = Vector2.Lerp(submitBase + new Vector2(0, -60), submitBase, e);
                _undoGroup.alpha = p;
                _submitGroup.alpha = p;
                yield return null;
            }
            undoRt.anchoredPosition = undoBase;
            submitRt.anchoredPosition = submitBase;
            _undoGroup.alpha = 1f;
            _submitGroup.alpha = 1f;
        }

        /// <summary>Move the "your prompt is..." label up with the prompt and fade it out by the settle point.</summary>
        private IEnumerator MoveAndFadeLabel(float promptY)
        {
            if (_introLabelText == null)
                yield break;
            var group = _introLabelText.GetComponent<CanvasGroup>();
            if (group == null)
                group = _introLabelText.gameObject.AddComponent<CanvasGroup>();

            var startPos = _introLabelText.rectTransform.anchoredPosition;
            var targetPos = new Vector2(0f, promptY + 100f);
            var duration = 0.45f; // move duration
            var fadeDuration = 0.3f; // fade out quicker
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                var p = Mathf.Clamp01(t / duration);
                _introLabelText.rectTransform.anchoredPosition = Vector2.Lerp(startPos, targetPos, p);
                var fadeP = Mathf.Clamp01(t / fadeDuration);
                group.alpha = 1f - fadeP;
                yield return null;
            }
            _introLabelText.gameObject.SetActive(false);
        }

        private void SubmitDrawing()
        {
            SplitLog.Log($"[MovingCanvasUI] SubmitDrawing: phase={_flow?.Phase}, submitted={_submittedThisRound}, strokes={(_drawCanvas != null ? _drawCanvas.StrokeCount : -1)}");
            if (_flow == null || _flow.Phase != MovingPhase.Drawing)
                return;
            if (_submittedThisRound)
                return;

            // Capture any in-progress stroke before serializing.
            _drawCanvas.CommitCurrentStroke();
            if (_drawCanvas.StrokeCount == 0)
                return; // don't submit an empty drawing
            var json = _drawCanvas.GetStrokeData();
            if (string.IsNullOrEmpty(json))
                return;

            _submittedThisRound = true;
            _drawCanvas.interactable = false;
            _drawMover.Reset();
            if (_modeProps != null)
                _modeProps.Reset();
            SetMovementAudio(false);
            //_drawStatus.text = "Submitted! Waiting for others...";
            _submitButton.gameObject.SetActive(false);
            _undoButton.gameObject.SetActive(false);
            SFX.SubmitDrawing();
            _flow.SubmitDrawing(_flow.LocalPlayerIndex, json);
        }

        private static string ReasonForMode(MovementMode mode)
        {
            switch (mode)
            {
                case MovementMode.Sway: return "A windy day...";
                case MovementMode.Wobble: return "On a wobbly table...";
                case MovementMode.Spin: return "On a spinning record player...";
                case MovementMode.Shake: return "On a bumpy car ride...";
                case MovementMode.ZoomOut: return "Falling away...";
                default: return "";
            }
        }

        /// <summary>Per-round background tint/sprite to set the scene.</summary>
        private void ApplyRoundTheming(MovementMode mode)
        {
            _currentMode = mode;
            if (_drawingPanelImage == null)
                return;
            var bg = Theme.GetModeBackground(mode);
            if (bg != null)
            {
                _modeBackground.gameObject.SetActive(true);
                _modeBackground.sprite = bg;
                _modeBackground.type = Image.Type.Simple;
                _modeBackground.color = Color.white;
                SizeToCover(_modeBackground, bg);
                _drawingPanelImage.color = new Color(0, 0, 0, 0); // transparent; bg shows through
            }
            else
            {
                _modeBackground.gameObject.SetActive(false);
                _drawingPanelImage.sprite = null;
                switch (mode)
                {
                    case MovementMode.Sway: _drawingPanelImage.color = new Color(0.10f, 0.16f, 0.28f, 0.7f); break;
                    case MovementMode.Wobble: _drawingPanelImage.color = new Color(0.22f, 0.12f, 0.28f, 0.7f); break;
                    case MovementMode.Spin: _drawingPanelImage.color = new Color(0.28f, 0.10f, 0.16f, 0.7f); break;
                    case MovementMode.Shake: _drawingPanelImage.color = new Color(0.28f, 0.20f, 0.10f, 0.7f); break;
                    case MovementMode.ZoomOut: _drawingPanelImage.color = new Color(0.10f, 0.24f, 0.24f, 0.7f); break;
                    default: _drawingPanelImage.color = new Color(0, 0, 0, 0); break;
                }
            }
        }

        /// <summary>Size an Image to cover the screen while preserving the sprite's aspect ratio (crops overflow).</summary>
        private void SizeToCover(Image img, Sprite sprite)
        {
            if (img == null || sprite == null || _rootRt == null)
                return;
            var rt = img.rectTransform;
            var screenW = _rootRt.rect.width;
            var screenH = _rootRt.rect.height;
            var spriteW = sprite.rect.width;
            var spriteH = sprite.rect.height;
            if (spriteW <= 0 || spriteH <= 0 || screenW <= 0 || screenH <= 0)
                return;
            var screenAspect = screenW / screenH;
            var spriteAspect = spriteW / spriteH;
            float scale = spriteAspect > screenAspect
                ? screenH / spriteH   // sprite wider -> height fills, width overflows (cropped left/right)
                : screenW / spriteW;  // sprite taller -> width fills, height overflows (cropped top/bottom)
            rt.sizeDelta = new Vector2(spriteW * scale, spriteH * scale);
        }

        // ---------------------------------------------------------------
        // Guessing
        // ---------------------------------------------------------------

        private void RefreshGuessing()
        {
            if (!_flow.IsRoundReady)
                return;

            var canvas = _flow.CurrentGuessCanvas;
            // Reset guess state when the round changes (clears stale guesses between rounds).
            if (_flow.Round != _lastGuessRound)
            {
                _lastGuessRound = _flow.Round;
                _lastGuessCanvas = -1;
                _lastGuessSignature = "";
                _lastGuessCount = 0;
            }
            if (canvas != _lastGuessCanvas)
            {
                _lastGuessCanvas = canvas;
                _lastGuessSignature = "";
                _lastGuessCount = 0;
                if (_guessInput != null)
                    _guessInput.text = "";
                _whatIsThisText.gameObject.SetActive(false);
                RebuildGuessList(); // force clear + rebuild for the new canvas
                StartCoroutine(RevealCanvas(canvas));
            }

            var isArtist = _flow.IsArtist(canvas);
            var alreadyCorrect = LocalPlayerAlreadyCorrect(canvas);
            var canGuess = !isArtist && !alreadyCorrect;
            // Never show the "What is this?" label to the player who drew this canvas.
            if (isArtist)
                _whatIsThisText.gameObject.SetActive(false);
            _guessStatus.text = isArtist
                ? $"Canvas {canvas + 1} — you drew this. Watch others guess!"
                : alreadyCorrect
                    ? $"Canvas {canvas + 1} — you got it! Watch others guess."
                    : $"Canvas {canvas + 1} — guess the prompt!";

            _guessInput.gameObject.SetActive(canGuess);
            _guessSubmit.gameObject.SetActive(canGuess);

            var sig = BuildGuessSignature();
            if (sig != _lastGuessSignature)
            {
                _lastGuessSignature = sig;
                RebuildGuessList();
            }

            // Show the revealed correct prompt at the end of this canvas's guess phase.
            var reveal = _flow.RevealPrompt;
            if (!string.IsNullOrEmpty(reveal))
            {
                _revealPromptLabel.text = "the prompt was…";
                _revealPromptText.text = $"{reveal.ToUpperInvariant()}!";
                _revealPromptLabel.gameObject.SetActive(true);
                _revealPromptText.gameObject.SetActive(true);
                _revealPromptBg.gameObject.SetActive(true);
                SFX.RevealPrompt();
                StartCoroutine(AnimateRevealPrompt());
            }
            else
            {
                _revealPromptLabel.gameObject.SetActive(false);
                _revealPromptText.gameObject.SetActive(false);
                _revealPromptBg.gameObject.SetActive(false);
            }
        }

        private IEnumerator RevealCanvas(int canvas)
        {
            _whatIsThisText.gameObject.SetActive(false);
            _revealCanvas.Clear();
            _revealCanvas.SetStrokeData(_flow.GetCanvasStrokes(canvas), false);
            SFX.RevealCanvas();
            // Pop the reveal wrap in.
            _revealWrap.localScale = Vector3.one * 0.7f;
            yield return UITween.ScaleTo(this, _revealWrap, Vector3.one, 0.25f, EaseOut);
            // Trace each stroke over ~0.2s so the drawing visibly draws itself.
            yield return _revealCanvas.PlayReveal(0.03f, 0.25f);
            // Animate the "What is this?" label in (guessers only).
            if (!_flow.IsArtist(canvas))
                StartCoroutine(AnimateWhatIsThis());
        }

        /// <summary>Juicy pop-in for the revealed prompt (bg + big prompt + small label).</summary>
        private IEnumerator AnimateRevealPrompt()
        {
            if (_revealPromptText == null || _revealPromptBg == null)
                yield break;
            _revealPromptBg.rectTransform.localScale = Vector3.one * 0.8f;
            _revealPromptText.rectTransform.localScale = Vector3.one * 0.5f;
            _revealPromptLabel.rectTransform.localScale = Vector3.one * 0.5f;
            yield return UITween.ScaleTo(this, _revealPromptBg.rectTransform, Vector3.one, 0.2f, EaseOut);
            // Label pops in first, then the big prompt.
            yield return UITween.ScaleTo(this, _revealPromptLabel.rectTransform, Vector3.one, 0.15f, EaseOut);
            yield return UITween.ScaleTo(this, _revealPromptText.rectTransform, Vector3.one * 1.15f, 0.2f, EaseOut);
            yield return UITween.ScaleTo(this, _revealPromptText.rectTransform, Vector3.one, 0.15f, EaseOut);
        }

        private IEnumerator AnimateWhatIsThis()
        {
            if (_whatIsThisText == null)
                yield break;
            _whatIsThisText.gameObject.SetActive(true);
            _whatIsThisText.rectTransform.localScale = Vector3.one * 0.6f;
            yield return UITween.ScaleTo(this, _whatIsThisText.rectTransform, Vector3.one, 0.3f, EaseOut);
        }

        private void SubmitGuess()
        {
            if (_flow == null || _flow.Phase != MovingPhase.Guessing)
                return;
            var canvas = _flow.CurrentGuessCanvas;
            if (_flow.IsArtist(canvas))
                return;
            if (LocalPlayerAlreadyCorrect(canvas))
                return;
            if (string.IsNullOrWhiteSpace(_guessInput.text))
                return;

            SFX.SubmitGuess();
            _flow.SubmitGuess(canvas, _guessInput.text);
            _guessInput.text = "";
            // Keep the field focused so the player can keep typing/guessing.
            StartCoroutine(RefocusGuessInput());
        }

        private bool LocalPlayerAlreadyCorrect(int canvas)
        {
            var localIdx = _flow.LocalPlayerIndex;
            // Use GuessCount + safe accessors: the parallel guess lists can be briefly
            // out of sync while a SyncList change is being applied, so never index them
            // directly by GuessCanvas.Count.
            for (var i = 0; i < GuessCount; i++)
                if (SafeGuessCanvas(i) == canvas && SafeGuessPlayer(i) == localIdx && SafeGuessCorrect(i))
                    return true;
            return false;
        }

        private IEnumerator RefocusGuessInput()
        {
            yield return null; // wait a frame so the submit doesn't steal focus back
            if (_guessInput != null && _guessInput.gameObject.activeInHierarchy)
            {
                _guessInput.ActivateInputField();
                _guessInput.Select();
            }
        }

        private string BuildGuessSignature()
        {
            var sb = new System.Text.StringBuilder();
            var canvas = _flow.CurrentGuessCanvas;
            for (var i = 0; i < GuessCount; i++)
            {
                if (SafeGuessCanvas(i) != canvas)
                    continue;
                sb.Append(SafeGuessPlayer(i)).Append('|')
                  .Append(SafeGuessText(i)).Append('|')
                  .Append(SafeGuessCorrect(i)).Append('|')
                  .Append(SafeGuessOrder(i)).Append(';');
            }
            return sb.ToString();
        }

        private void RebuildGuessList()
        {
            if (_guessList == null)
                return;

            for (var i = _guessList.childCount - 1; i >= 0; i--)
                Destroy(_guessList.GetChild(i).gameObject);

            var canvas = _flow.CurrentGuessCanvas;
            var built = 0;
            var maxRows = 6;
            // Keep only the newest maxRows guesses for this canvas so the list stays fitting.
            var indices = new List<int>();
            for (var i = 0; i < GuessCount; i++)
                if (SafeGuessCanvas(i) == canvas)
                    indices.Add(i);
            var start = Mathf.Max(0, indices.Count - maxRows);
            for (var k = start; k < indices.Count; k++)
            {
                BuildGuessRow(indices[k]);
                built++;
            }

            // Animate the newly added rows (those beyond the previous count) with a scale-in.
            var newCount = built - _lastGuessCount;
            if (newCount > 0)
            {
                var children = _guessList.childCount;
                for (var c = children - newCount; c < children; c++)
                {
                    var rt = _guessList.GetChild(c) as RectTransform;
                    if (rt == null)
                        continue;
                    rt.localScale = Vector3.one * 0.85f;
                    UITween.ScaleTo(this, rt, Vector3.one, 0.25f, EaseOut);
                }
            }
            _lastGuessCount = built;
        }

        private void BuildGuessRow(int index)
        {
            var guesser = SafeGuessPlayer(index);
            var guesserName = guesser >= 0 && guesser < _flow.PlayerNames.Count ? _flow.PlayerNames[guesser] : "?";
            var correct = SafeGuessCorrect(index);
            var text = SafeGuessText(index);

            var row = new GameObject($"GuessRow{index}", typeof(RectTransform));
            row.transform.SetParent(_guessList, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0, 88);

            // Player color swatch
            var swatch = CreatePanel("Swatch", row.transform, Theme.GetPlayerColor(guesser));
            SetAnchors(swatch.GetComponent<RectTransform>(), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(0, -32), new Vector2(64, 32));

            // Player icon (optional).
            var leftPad = 80f;
            var icon = Theme.GetPlayerIcon(guesser);
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(row.transform, false);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = icon;
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;
                SetAnchors(iconImg.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(72, -32), new Vector2(136, 32));
                leftPad = 152f;
            }

            var label = CreateText("Label", row.transform, 48, Theme.text);
            label.alignment = TextAnchor.MiddleLeft;
            SetAnchors(label.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(leftPad, -40), new Vector2(-20, 40));

            if (correct)
            {
                // Redact the correct answer so players can't copy it.
                label.text = $"{guesserName}: [REDACTED]";
                label.color = Theme.textMuted;

                var chip = CreateText("Chip", row.transform, 40, Theme.positive);
                chip.text = "CORRECT!";
                SetAnchors(chip.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                    new Vector2(-320, -30), new Vector2(-20, 30));
            }
            else
            {
                label.text = $"{guesserName}: {text}";
            }
        }

        /// <summary>Build a row with a color swatch, optional icon, name, and a right-side label.</summary>
        private GameObject CreatePlayerRow(Transform parent, int playerIndex, string rightText, string placePrefix = null)
        {
            var row = new GameObject($"PlayerRow{playerIndex}", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0, 110);

            var leftPad = 0f;

            // Ordinal (1st/2nd/...) on the far left, in a larger font.
            if (!string.IsNullOrEmpty(placePrefix))
            {
                var place = CreateText("Place", row.transform, 76, Theme.accent);
                place.alignment = TextAnchor.MiddleCenter;
                place.text = placePrefix;
                SetAnchors(place.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(0, -52), new Vector2(110, 52));
                leftPad = 120f;
            }

            // Color swatch.
            var swatch = CreatePanel("Swatch", row.transform, Theme.GetPlayerColor(playerIndex));
            SetAnchors(swatch.GetComponent<RectTransform>(), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(leftPad, -40), new Vector2(leftPad + 72, 40));

            // Icon (optional).
            leftPad += 80f;
            var icon = Theme.GetPlayerIcon(playerIndex);
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(row.transform, false);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = icon;
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;
                SetAnchors(iconImg.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(leftPad, -40), new Vector2(leftPad + 72, 40));
                leftPad += 80f;
            }

            // Name.
            var name = CreateText("Name", row.transform, 64, Theme.GetPlayerBrightColor(playerIndex));
            name.alignment = TextAnchor.MiddleLeft;
            name.horizontalOverflow = HorizontalWrapMode.Overflow; // keep on one line
            name.text = _flow.PlayerNames[playerIndex];
            SetAnchors(name.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(leftPad, -48), new Vector2(-300, 48));

            // Right label (e.g. score).
            if (!string.IsNullOrEmpty(rightText))
            {
                var right = CreateText("Right", row.transform, 64, Theme.text);
                right.alignment = TextAnchor.MiddleRight;
                right.text = rightText;
                SetAnchors(right.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                    new Vector2(-280, -48), new Vector2(-20, 48));
            }
            return row;
        }

        // ---------------------------------------------------------------
        // Scoreboard / Final
        // ---------------------------------------------------------------

        private void RefreshScoreboard()
        {
            _scoreboardText.text = "SCORES";
            if (_scoreboardList == null)
                return;

            // Only rebuild when the scoreboard data changes. Rebuilding on every timer
            // tick destroyed/recreated the rows while AnimateScoreboardRows was still
            // iterating them → "Transform child out of bounds".
            var sig = BuildScoreboardSignature();
            if (sig == _lastScoreboardSignature)
                return;
            _lastScoreboardSignature = sig;

            for (var i = _scoreboardList.childCount - 1; i >= 0; i--)
                Destroy(_scoreboardList.GetChild(i).gameObject);

            // Sort players by score descending so first place is at the top.
            var order = new List<int>();
            for (var i = 0; i < _flow.PlayerNames.Count; i++)
                order.Add(i);
            order.Sort((a, b) => SafeGet(_flow.Scores, b).CompareTo(SafeGet(_flow.Scores, a)));

            // Create all rows hidden, then reveal them one-by-one from last to first place.
            for (var i = 0; i < order.Count; i++)
            {
                var row = CreatePlayerRow(_scoreboardList, order[i], SafeGet(_flow.Scores, order[i]).ToString(), Place(i + 1));
                var cg = row.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
            }
            StartCoroutine(AnimateScoreboardRows());
        }

        /// <summary>Signature of the current scoreboard data (round + each player's score).</summary>
        private string BuildScoreboardSignature()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_flow.Round).Append('|');
            for (var i = 0; i < _flow.PlayerNames.Count; i++)
                sb.Append(_flow.PlayerNames[i]).Append(':').Append(SafeGet(_flow.Scores, i)).Append(';');
            return sb.ToString();
        }

        /// <summary>Reveal scoreboard rows one-by-one, from last place (bottom) to first place (top).</summary>
        private IEnumerator AnimateScoreboardRows()
        {
            if (_scoreboardList == null)
                yield break;
            // Snapshot the rows first so a rebuild can't invalidate our indices mid-animation.
            var groups = new List<CanvasGroup>();
            for (var i = _scoreboardList.childCount - 1; i >= 0; i--)
            {
                var cg = _scoreboardList.GetChild(i).GetComponent<CanvasGroup>();
                if (cg != null)
                    groups.Add(cg);
            }
            foreach (var cg in groups)
            {
                if (cg == null) // destroyed by a rebuild
                    continue;
                yield return UITween.FadeTo(this, cg, 1f, 0.3f);
                yield return new WaitForSeconds(0.3f);
            }
        }

        private void RefreshFinal()
        {
            // Winner + scores from the snapshot (stable even if a player leaves).
            var (names, colors, icons, scores) = ParseFinalScores();
            var best = -1;
            var bestScore = int.MinValue;
            for (var i = 0; i < scores.Count; i++)
                if (scores[i] > bestScore) { bestScore = scores[i]; best = i; }
            var name = best >= 0 && best < names.Count ? names[best] : "?";
            _finalText.text = $"GAME OVER\n\n{name} wins with {bestScore} points!";

            if (_finalScoresList != null)
            {
                for (var i = _finalScoresList.childCount - 1; i >= 0; i--)
                    Destroy(_finalScoresList.GetChild(i).gameObject);
                // Sort by score descending so the winner (1st place) is at the TOP.
                var order = new List<int>();
                for (var i = 0; i < scores.Count; i++)
                    order.Add(i);
                order.Sort((a, b) => scores[b].CompareTo(scores[a]));
                for (var k = 0; k < order.Count; k++)
                {
                    var i = order[k];
                    var row = CreateFinalScoreRow(_finalScoresList, names[i], colors[i], icons[i], scores[i], Place(k + 1), k == 0);
                    var cg = row.AddComponent<CanvasGroup>();
                    cg.alpha = 0f;
                }
                StartCoroutine(AnimateFinalScores(0)); // winner is the first (top) row
            }

            // Build the showcase once (when the archive changes).
            var sig = BuildFinalSignature();
            if (sig != _lastFinalSignature)
            {
                _lastFinalSignature = sig;
                RebuildShowcase();
                SFX.Winner();
            }

            _endGameButton.gameObject.SetActive(_flow.IsHost);
        }

        private (List<string>, List<int>, List<int>, List<int>) ParseFinalScores()
        {
            var names = new List<string>();
            var colors = new List<int>();
            var icons = new List<int>();
            var scores = new List<int>();
            var data = _flow.FinalScores;
            if (string.IsNullOrEmpty(data))
                return (names, colors, icons, scores);
            foreach (var entry in data.Split('\u0006'))
            {
                var parts = entry.Split('\u0005');
                if (parts.Length < 4)
                    continue;
                names.Add(parts[0]);
                colors.Add(int.TryParse(parts[1], out var c) ? c : 0);
                icons.Add(int.TryParse(parts[2], out var ic) ? ic : 0);
                scores.Add(int.TryParse(parts[3], out var s) ? s : 0);
            }
            return (names, colors, icons, scores);
        }

        private GameObject CreateFinalScoreRow(Transform parent, string name, int colorIndex, int iconIndex, int score, string place, bool isWinner)
        {
            var row = new GameObject("FinalScoreRow", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0, 96);

            var leftPad = 0f;

            // Ordinal on the far left, larger font.
            var placeText = CreateText("Place", row.transform, 72, isWinner ? Theme.accent : Theme.text);
            placeText.alignment = TextAnchor.MiddleCenter;
            placeText.text = place;
            SetAnchors(placeText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(0, -46), new Vector2(100, 46));
            leftPad = 110f;

            // Color swatch.
            var swatch = CreatePanel("Swatch", row.transform, Theme.GetPlayerColor(colorIndex));
            SetAnchors(swatch.GetComponent<RectTransform>(), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(leftPad, -34), new Vector2(leftPad + 60, 34));

            // Icon.
            leftPad += 68f;
            var icon = Theme.GetPlayerIcon(iconIndex);
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(row.transform, false);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = icon;
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;
                SetAnchors(iconImg.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(leftPad, -34), new Vector2(leftPad + 60, 34));
                leftPad += 68f;
            }

            var label = CreateText("Label", row.transform, 60, isWinner ? Theme.accent : Theme.text);
            label.alignment = TextAnchor.MiddleLeft;
            label.text = isWinner ? $"{name}  👑" : name;
            SetAnchors(label.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(leftPad, -44), new Vector2(-200, 44));

            var right = CreateText("Score", row.transform, 60, Theme.text);
            right.alignment = TextAnchor.MiddleRight;
            right.text = score.ToString();
            SetAnchors(right.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-190, -44), new Vector2(-20, 44));
            return row;
        }

        /// <summary>Reveal final score rows one at a time with a delay between each; confetti on the winner.</summary>
        private IEnumerator AnimateFinalScores(int winnerIndex)
        {
            if (_finalScoresList == null)
                yield break;
            for (var i = 0; i < _finalScoresList.childCount; i++)
            {
                var cg = _finalScoresList.GetChild(i).GetComponent<CanvasGroup>();
                if (cg != null)
                    yield return UITween.FadeTo(this, cg, 1f, 0.3f);
                if (i == winnerIndex)
                    ConfettiBurst(Vector2.zero, _finalPanel.transform);
                yield return new WaitForSeconds(0.35f);
            }
        }

        private string BuildFinalSignature()
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < _flow.ArchiveCount; i++)
                sb.Append(_flow.ArchiveRound(i)).Append('|')
                  .Append(_flow.ArchiveCanvas(i)).Append('|')
                  .Append(_flow.ArchiveStrokes(i)?.Length ?? 0).Append(';');
            return sb.ToString();
        }

        private void RebuildShowcase()
        {
            if (_showcaseContent == null)
                return;
            for (var i = _showcaseContent.childCount - 1; i >= 0; i--)
                Destroy(_showcaseContent.GetChild(i).gameObject);

            var x = 0f;
            var spacing = 48f;
            var thumbSize = 500f;
            for (var i = 0; i < _flow.ArchiveCount; i++)
            {
                var thumbGo = new GameObject($"Thumb{i}", typeof(RectTransform));
                thumbGo.transform.SetParent(_showcaseContent, false);
                var thumbRt = thumbGo.GetComponent<RectTransform>();
                thumbRt.anchorMin = new Vector2(0, 0.5f);
                thumbRt.anchorMax = new Vector2(0, 0.5f);
                thumbRt.pivot = new Vector2(0, 0.5f);
                thumbRt.sizeDelta = new Vector2(thumbSize, thumbSize);
                thumbRt.anchoredPosition = new Vector2(x, 0f);

                var thumb = thumbGo.AddComponent<RawImage>();
                thumb.color = Color.white;
                var strokes = _flow.ArchiveStrokes(i);
                if (!string.IsNullOrEmpty(strokes))
                {
                    var sc = thumbGo.AddComponent<StrokeCanvas>();
                    sc.Init(thumb);
                    sc.interactable = false;
                    sc.SetStrokeData(strokes);
                }

                // Prompt label centered below the canvas.
                var prompt = CreateText("Prompt", thumbGo.transform, 34, Theme.text);
                prompt.text = _flow.ArchivePrompt(i).ToUpperInvariant();
                SetAnchors(prompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(-220, -70), new Vector2(220, -20));

                x += thumbSize + spacing;
            }
            _showcaseContent.sizeDelta = new Vector2(x, thumbSize + 160);
        }

        private IEnumerator ScrollShowcase()
        {
            // Wait until the showcase is fully built and laid out before scrolling, so the
            // duration is computed from the real content width (not a partial/zero width).
            if (_showcaseContent == null)
                yield break;
            var expected = _flow.ArchiveCount;
            while (_showcaseContent.childCount < expected)
                yield return null;
            // Let layout settle for a couple frames so rect.width is final.
            yield return null;
            yield return null;

            while (true)
            {
                var viewportWidth = _showcaseScroll.rect.width;
                var contentWidth = _showcaseContent.rect.width;
                if (contentWidth <= 0 || viewportWidth <= 0)
                {
                    yield return null;
                    continue;
                }
                // Start with the content shifted to the far right (first canvas offscreen
                // right), then scroll left so canvases are revealed from the right. End
                // when the content's right edge reaches the left side of the screen.
                var startX = viewportWidth + 280f;
                var endX = -contentWidth - 280f;
                var duration = contentWidth / 150f; // slow horizontal scroll
                var t = 0f;
                while (t < duration)
                {
                    t += Time.deltaTime;
                    _showcaseContent.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, t / duration), 0f);
                    yield return null;
                }
                _showcaseContent.anchoredPosition = new Vector2(startX, 0f);
            }
        }

        private IEnumerator FlashImage(Image img, Color color, float duration)
        {
            if (img == null)
                yield break;
            var original = img.color;
            img.color = color;
            yield return new WaitForSeconds(duration);
            img.color = original;
        }

        private void ConfettiBurst(Vector2 center, Transform parent)
        {
            var colors = new[] { Theme.accent, Theme.positive, Theme.GetPlayerColor(0), Theme.GetPlayerColor(1) };
            for (var i = 0; i < 60; i++)
            {
                var go = new GameObject("Confetti", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(28, 28);
                rt.anchoredPosition = center;
                var img = go.AddComponent<Image>();
                img.color = colors[i % colors.Length];
                img.raycastTarget = false;
                var dir = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(200f, 600f);
                StartCoroutine(ConfettiRoutine(rt, dir));
            }
        }

        private IEnumerator ConfettiRoutine(RectTransform rt, Vector2 velocity)
        {
            var t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * 1.5f;
                rt.anchoredPosition += velocity * Time.deltaTime;
                rt.localRotation = Quaternion.Euler(0, 0, t * 720f);
                rt.localScale = Vector3.one * (1f - t);
                yield return null;
            }
            Destroy(rt.gameObject);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private int GuessCount => Mathf.Min(
            _flow.GuessCanvas.Count,
            Mathf.Min(_flow.GuessPlayer.Count,
            Mathf.Min(_flow.GuessText.Count,
            Mathf.Min(_flow.GuessCorrect.Count, _flow.GuessOrder.Count))));

        private int SafeGuessCanvas(int i) => (i >= 0 && i < _flow.GuessCanvas.Count) ? _flow.GuessCanvas[i] : -1;
        private int SafeGuessPlayer(int i) => (i >= 0 && i < _flow.GuessPlayer.Count) ? _flow.GuessPlayer[i] : -1;
        private string SafeGuessText(int i) => (i >= 0 && i < _flow.GuessText.Count) ? _flow.GuessText[i] : "";
        private bool SafeGuessCorrect(int i) => (i >= 0 && i < _flow.GuessCorrect.Count) && _flow.GuessCorrect[i];
        private int SafeGuessOrder(int i) => (i >= 0 && i < _flow.GuessOrder.Count) ? _flow.GuessOrder[i] : 0;

        private static int SafeGet(IReadOnlyList<int> list, int i)
        {
            return (i >= 0 && i < list.Count) ? list[i] : 0;
        }

        private static string PhaseLabel(MovingPhase phase)
        {
            switch (phase)
            {
                case MovingPhase.Waiting: return "WAITING";
                case MovingPhase.Tutorial: return "TUTORIAL";
                case MovingPhase.Drawing: return "DRAWING";
                case MovingPhase.Guessing: return "GUESSING";
                case MovingPhase.Scoreboard: return "SCORES";
                case MovingPhase.Final: return "FINAL";
                default: return phase.ToString();
            }
        }

        /// <summary>Ordinal suffix for a place (1st, 2nd, 3rd, 4th...).</summary>
        private static string Place(int n)
        {
            var suffix = (n % 100) >= 11 && (n % 100) <= 13 ? "th"
                : (n % 10) == 1 ? "st" : (n % 10) == 2 ? "nd" : (n % 10) == 3 ? "rd" : "th";
            return n + suffix;
        }

        /// <summary>Keep the guess list within 90% of the screen width on narrow/mobile screens.</summary>
        private void SizeGuessList()
        {
            if (_guessList == null || _rootRt == null)
                return;
            var rt = _guessList as RectTransform;
            var w = Mathf.Min(1200f, _rootRt.rect.width * 0.9f);
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
        }

        /// <summary>Keep the guess input within 90% of the screen width on narrow/mobile screens.</summary>
        private void SizeGuessInput()
        {
            if (_guessInput == null || _rootRt == null)
                return;
            var rt = _guessInput.GetComponent<RectTransform>();
            var w = Mathf.Min(1000f, _rootRt.rect.width * 0.9f);
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
        }

        /// <summary>Position the "What is this?" label just below the reveal canvas.</summary>
        private void SizeWhatIsThis()
        {
            if (_whatIsThisText == null || _revealWrap == null || _rootRt == null)
                return;
            var size = _revealWrap.sizeDelta.x;
            var yFromTop = 110f + size + 30f;
            _whatIsThisText.rectTransform.anchoredPosition = new Vector2(0f, -yFromTop);
        }

        /// <summary>Responsively size the tutorial image (fit within bounds, keep aspect) and text width.</summary>
        private void SizeTutorial()
        {
            if (_tutorialImage == null || _rootRt == null)
                return;
            var screenW = _rootRt.rect.width;
            var screenH = _rootRt.rect.height;
            var maxW = screenW * 0.55f;
            var maxH = screenH * 0.38f;
            var sprite = _tutorialImage.sprite;
            if (sprite != null && sprite.rect.width > 0f && sprite.rect.height > 0f)
            {
                var aspect = sprite.rect.width / sprite.rect.height;
                var w = maxW;
                var h = w / aspect;
                if (h > maxH) { h = maxH; w = h * aspect; }
                _tutorialImage.rectTransform.sizeDelta = new Vector2(w, h);
            }
            if (_tutorialText != null)
            {
                var rt = _tutorialText.rectTransform;
                rt.sizeDelta = new Vector2(Mathf.Min(1200f, screenW * 0.85f), rt.sizeDelta.y);
                // Larger text on mobile/portrait.
                _tutorialText.fontSize = screenW < screenH ? 54 : 38;
                // Top-anchor the text just below the image's bottom edge.
                var imgBottomFromTop = screenH * 0.25f + _tutorialImage.rectTransform.sizeDelta.y * 0.5f;
                rt.anchoredPosition = new Vector2(0f, -(imgBottomFromTop + 140f));
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

        private GameObject CreatePanel(string name, Transform parent, Color color, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
            }
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        /// <summary>
        /// A full-screen phase background (centered, stretched to cover like the drawing
        /// phase's per-mode background). Hidden until ApplyPhaseBackground assigns a sprite.
        /// </summary>
        private Image CreatePhaseBackground(Transform parent)
        {
            var bg = CreatePanel("PhaseBg", parent, Color.white).GetComponent<Image>();
            var rt = bg.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            bg.transform.SetAsFirstSibling();
            bg.gameObject.SetActive(false);
            return bg;
        }

        /// <summary>Apply the themed background for a non-drawing phase (sized to cover).</summary>
        private void ApplyPhaseBackground(MovingPhase phase)
        {
            Image bg = null;
            switch (phase)
            {
                case MovingPhase.Waiting: bg = _waitingBg; break;
                case MovingPhase.Tutorial: bg = _tutorialBg; break;
                case MovingPhase.Guessing: bg = _guessingBg; break;
                case MovingPhase.Scoreboard: bg = _scoreboardBg; break;
                case MovingPhase.Final: bg = _finalBg; break;
                default: return;
            }
            if (bg == null)
                return;
            var sprite = Theme.GetPhaseBackground(phase);
            if (sprite != null)
            {
                bg.gameObject.SetActive(true);
                bg.sprite = sprite;
                bg.type = Image.Type.Simple;
                bg.color = Color.white;
                SizeToCover(bg, sprite);
            }
            else
            {
                bg.gameObject.SetActive(false);
            }
        }

        private Image CreateBorder(Transform parent, Vector2 size, Sprite sprite, Color color)
        {
            var go = new GameObject("Border", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return img;
        }

        private BrushCursor CreateBrushCursor(Transform parent)
        {
            var go = new GameObject("BrushCursor", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;

            // Single combined paw/brush sprite (the arm is part of the art).
            var tip = CreateCursorImage("Tip", go.transform, Theme.pawSprite != null ? Theme.pawSprite : Theme.brushCursorSprite, new Vector2(192, 192));

            var cursor = go.AddComponent<BrushCursor>();
            cursor.Init(tip);
            go.SetActive(false);
            return cursor;
        }

        private Image CreateCursorImage(string name, Transform parent, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return img;
        }

        private static Font _boldFont;
        private static Font _lightFont;

        /// <summary>Load the bold (important) or light (secondary) font, falling back to the built-in.</summary>
        private static Font GetFont(bool bold)
        {
            if (bold)
            {
                if (DefaultTheme.boldFont != null)
                    return DefaultTheme.boldFont;
                if (_boldFont == null)
                    _boldFont = Resources.Load<Font>("Fonts/Outfit-ExtraBold");
                return _boldFont != null ? _boldFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            if (DefaultTheme.lightFont != null)
                return DefaultTheme.lightFont;
            if (_lightFont == null)
                _lightFont = Resources.Load<Font>("Fonts/Outfit-Light");
            return _lightFont != null ? _lightFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static Text CreateText(string name, Transform parent, int fontSize, Color color, bool bold = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = GetFont(bold);
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Generate (once) a white pill-shaped sprite with 9-slice borders so buttons are pill-shaped.</summary>
        private Sprite GetPillSprite(int radius)
        {
            if (_pillSprite != null && _pillRadius == radius)
                return _pillSprite;
            const int size = 64;
            radius = Mathf.Clamp(radius, 1, size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var leftCenter = new Vector2(radius, size / 2f);
            var rightCenter = new Vector2(size - radius, size / 2f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside;
                    if (x < radius)
                        inside = Vector2.Distance(p, leftCenter) <= radius;
                    else if (x >= size - radius)
                        inside = Vector2.Distance(p, rightCenter) <= radius;
                    else
                        inside = true;
                    tex.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            }
            tex.Apply();
            _pillSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            _pillRadius = radius;
            return _pillSprite;
        }

        private Button CreateButton(string name, Transform parent, string label, Color color, Action onClick, Sprite icon = null)
        {
            var style = _buttonStyle;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = GetPillSprite(style != null ? style.pillRadius : 32);
            img.type = Image.Type.Sliced;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => { SFX.Tap(); onClick?.Invoke(); });

            // Drop shadow below the pill — a solid color, tinted darker by the shadow
            // color's alpha (the lerp uses the alpha as the darkening amount).
            var shadowComp = go.AddComponent<Shadow>();
            var tintColor = style != null ? style.shadowColor : new Color(0, 0, 0, 0.4f);
            var tintStrength = tintColor.a;
            var shadowColor = Color.Lerp(color, tintColor, tintStrength);
            shadowColor.a = 1f; // solid (not transparent)
            shadowComp.effectColor = shadowColor;
            shadowComp.effectDistance = style != null ? style.shadowOffset : new Vector2(0, -6);

            var leftPad = 0f;
            if (icon != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(go.transform, false);
                var iconImg = iconGo.AddComponent<Image>();
                iconImg.sprite = icon;
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;
                SetAnchors(iconImg.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                    new Vector2(16, -18), new Vector2(52, 18));
                leftPad = 60f;
            }

            var text = CreateText("Label", go.transform, style != null ? style.fontSize : 48, style != null ? style.fontColor : Color.black);
            text.text = label;
            SetAnchors(text.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(leftPad, -20), new Vector2(0, 20));
            // Single light outline (4x vertices — safe) for readability.
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.4f);
            outline.effectDistance = new Vector2(2f, -2f);
            // Drop shadow under the text.
            var textShadow = text.gameObject.AddComponent<Shadow>();
            textShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            textShadow.effectDistance = new Vector2(0f, -4f);
            return button;
        }

        private InputField CreateInputField(string name, Transform parent, int fontSize = 24)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            if (Theme.panelSprite != null)
            {
                img.sprite = Theme.panelSprite;
                img.type = Image.Type.Sliced;
            }
            var input = go.AddComponent<InputField>();
            input.targetGraphic = img;

            var text = CreateText("Text", go.transform, fontSize, Color.black, false);
            text.alignment = TextAnchor.MiddleLeft;
            SetAnchors(text.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(10, -fontSize), new Vector2(-10, fontSize));
            input.textComponent = text;

            var placeholder = CreateText("Placeholder", go.transform, fontSize, new Color(0.4f, 0.4f, 0.4f, 1f), false);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.text = "Type your guess...";
            SetAnchors(placeholder.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f),
                new Vector2(10, -fontSize), new Vector2(-10, fontSize));
            input.placeholder = placeholder;

            return input;
        }
    }
}