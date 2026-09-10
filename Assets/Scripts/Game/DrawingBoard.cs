using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.UI;

namespace Jam
{
    /// <summary>
    /// Drawing mechanic prototype (Drawful-style).
    ///
    /// Builds a runtime UI with a DrawingCanvas for the local player, Submit and
    /// Clear buttons, and a reveal row. On Submit, the drawing is encoded to PNG
    /// bytes and sent to the server via [ServerRpc]; the server then broadcasts
    /// it to every client via [ObserversRpc] — exactly like the text answers in E3.
    ///
    /// Setup: create an empty "DrawingBoard" in MainGame and add this component.
    /// Test with ParrelSync (main editor + clone): draw on each, submit, and both
    /// windows show both drawings in the reveal row.
    /// </summary>
    public class DrawingBoard : NetworkBehaviour
    {
        // Local copy of all drawings (server + each client keep their own).
        private readonly List<byte[]> _drawings = new List<byte[]>();

        private DrawingCanvas _canvas;
        private Button _submitButton;
        private Button _clearButton;
        private Text _status;
        private Transform _revealRow;

        private void Awake()
        {
            BuildUI();
        }

        // ---------------------------------------------------------------
        // UI construction
        // ---------------------------------------------------------------

        private void BuildUI()
        {
            var canvasGo = new GameObject("DrawingBoard Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // render above the game UI

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            var root = CreatePanel("Root", canvasGo.transform, new Color(0.08f, 0.08f, 0.1f, 1f));

            var title = CreateText("Title", root.transform, 40);
            title.text = "DRAWING PROTOTYPE";
            SetAnchors(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-300, -60), new Vector2(300, -10));

            // Drawing area
            var drawGo = new GameObject("DrawArea", typeof(RectTransform));
            drawGo.transform.SetParent(root.transform, false);
            var raw = drawGo.AddComponent<RawImage>();
            raw.color = Color.white;
            SetAnchors(raw.rectTransform, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
                new Vector2(-256, -256), new Vector2(256, 256));

            _canvas = drawGo.AddComponent<DrawingCanvas>();
            _canvas.Init(raw);

            // Clear button
            _clearButton = CreateButton("Clear", root.transform, "Clear", () => _canvas.Clear());
            SetAnchors(_clearButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.3f),
                new Vector2(-260, -30), new Vector2(-20, 30));

            // Submit button
            _submitButton = CreateButton("Submit", root.transform, "Submit", SubmitDrawing);
            SetAnchors(_submitButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.3f),
                new Vector2(20, -30), new Vector2(260, 30));

            // Status
            _status = CreateText("Status", root.transform, 24);
            SetAnchors(_status.rectTransform, new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.22f),
                new Vector2(-400, -20), new Vector2(400, 20));

            // Reveal row (all synced drawings)
            var revealGo = new GameObject("RevealRow", typeof(RectTransform));
            revealGo.transform.SetParent(root.transform, false);
            _revealRow = revealGo.transform;
            var hlg = revealGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            SetAnchors(_revealRow as RectTransform, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f),
                new Vector2(-600, -60), new Vector2(600, 60));
        }

        // ---------------------------------------------------------------
        // Networking
        // ---------------------------------------------------------------

        private void SubmitDrawing()
        {
            var png = _canvas.GetPngBytes();
            if (png == null)
                return;

            // Send as base64 string — string is a codegen-safe RPC type.
            SubmitDrawingRpc(Convert.ToBase64String(png));
            _status.text = "Submitted!";
        }

        [ServerRpc(requireOwnership: false)]
        private void SubmitDrawingRpc(string base64, RPCInfo info = default)
        {
            Debug.Log($"[DrawingBoard] Server received drawing from {info.sender} ({base64.Length} chars)");
            AddDrawing(base64);
            BroadcastDrawingRpc(base64);
        }

        [ObserversRpc]
        private void BroadcastDrawingRpc(string base64)
        {
            // The server already added it in the ServerRpc; skip on the host.
            if (isServer)
                return;
            AddDrawing(base64);
        }

        private void AddDrawing(string base64)
        {
            _drawings.Add(Convert.FromBase64String(base64));
            RebuildReveal();
        }

        private void RebuildReveal()
        {
            if (_revealRow == null)
                return;

            for (var i = _revealRow.childCount - 1; i >= 0; i--)
                Destroy(_revealRow.GetChild(i).gameObject);

            for (var i = 0; i < _drawings.Count; i++)
            {
                var go = new GameObject($"Drawing{i}", typeof(RectTransform));
                go.transform.SetParent(_revealRow, false);

                var raw = go.AddComponent<RawImage>();
                var tex = new Texture2D(1, 1);
                tex.LoadImage(_drawings[i]);
                raw.texture = tex;

                var rt = raw.rectTransform;
                rt.sizeDelta = new Vector2(120, 120);

                // Clicking a thumbnail loads it into the local canvas so you can
                // continue drawing on top of it (testing feature).
                var idx = i;
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = raw;
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => LoadIntoCanvas(idx));
            }
        }

        private void LoadIntoCanvas(int index)
        {
            if (index < 0 || index >= _drawings.Count)
                return;

            _canvas.LoadImage(_drawings[index]);
            _status.text = $"Loaded drawing {index + 1} — draw on top, then Submit to send.";
        }

        // ---------------------------------------------------------------
        // UI helpers
        // ---------------------------------------------------------------

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

        private static Button CreateButton(string name, Transform parent, string label, System.Action onClick)
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
    }
}