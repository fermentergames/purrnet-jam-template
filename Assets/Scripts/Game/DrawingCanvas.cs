using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Jam
{
    /// <summary>
    /// A reusable drawing canvas: a RawImage showing a Texture2D that the player
    /// paints on with the mouse. Used by the drawing prototype (DrawingBoard) and
    /// later by the real game's answer phase.
    ///
    /// Setup: add to a GameObject that has a RawImage, then call Init(rawImage),
    /// or assign the RawImage in the inspector.
    /// </summary>
    public class DrawingCanvas : MonoBehaviour
    {
        [SerializeField] private RawImage _display;
        [SerializeField] private int _resolution = 256;
        [SerializeField] private int _brushSize = 6;
        [SerializeField] private Color _brushColor = Color.black;

        /// <summary>When false, the canvas is read-only (shows content but ignores input).</summary>
        public bool interactable = true;

        private Texture2D _texture;
        private bool _drawing;
        private Vector2 _lastPixel;

        private void Awake()
        {
            if (_display != null)
                CreateTexture();
        }

        /// <summary>Assign the RawImage and create the drawing texture.</summary>
        public void Init(RawImage display)
        {
            _display = display;
            CreateTexture();
        }

        private void CreateTexture()
        {
            _texture = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false);
            ClearTexture();
            if (_display != null)
                _display.texture = _texture;
        }

        private void ClearTexture()
        {
            var pixels = new Color[_resolution * _resolution];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            _texture.SetPixels(pixels);
            _texture.Apply();
        }

        /// <summary>Wipe the canvas back to white.</summary>
        public void Clear()
        {
            ClearTexture();
        }

        /// <summary>Set the brush color used for this canvas (call before drawing).</summary>
        public void SetBrushColor(Color color)
        {
            _brushColor = color;
        }

        /// <summary>Load a PNG into the canvas (replaces current content) so you can draw on top.</summary>
        public void LoadImage(byte[] png)
        {
            if (_texture == null)
                CreateTexture();
            _texture.LoadImage(png);
            _texture.Apply();
        }

        private void Update()
        {
            if (!interactable)
            {
                _drawing = false;
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || _texture == null)
                return;

            var screenPos = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                // Don't start drawing when clicking a UI button (Submit/Clear/etc).
                if (IsPointerOverOtherUI(screenPos))
                    return;

                _drawing = true;
                _lastPixel = ScreenToTexturePixel(screenPos);
            }
            else if (mouse.leftButton.isPressed && _drawing)
            {
                var current = ScreenToTexturePixel(screenPos);
                DrawLine(_lastPixel, current);
                _lastPixel = current;
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                _drawing = false;
            }
        }

        /// <summary>True if the pointer is over a UI element that is NOT the drawing canvas.</summary>
        private bool IsPointerOverOtherUI(Vector2 screenPos)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            // If the pointer isn't over any UI element, it's not over a button.
            if (!eventSystem.IsPointerOverGameObject())
                return false;

            // If the pointer is over the drawing canvas itself, allow drawing.
            if (_display != null &&
                RectTransformUtility.RectangleContainsScreenPoint(_display.rectTransform, screenPos, null))
                return false;

            return true;
        }

        private Vector2 ScreenToTexturePixel(Vector2 screenPos)
        {
            if (_display == null)
                return Vector2.zero;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _display.rectTransform, screenPos, null, out var localPoint);

            var rect = _display.rectTransform.rect;
            var u = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
            var v = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);

            return new Vector2(u * _resolution, v * _resolution);
        }

        private void DrawLine(Vector2 from, Vector2 to)
        {
            var dist = Vector2.Distance(from, to);
            var steps = Mathf.Max(1, Mathf.CeilToInt(dist));
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                DrawBrush(Vector2.Lerp(from, to, t));
            }
            _texture.Apply();
        }

        private void DrawBrush(Vector2 center)
        {
            var cx = Mathf.RoundToInt(center.x);
            var cy = Mathf.RoundToInt(center.y);
            var r = _brushSize;

            for (var y = -r; y <= r; y++)
            {
                for (var x = -r; x <= r; x++)
                {
                    if (x * x + y * y > r * r)
                        continue;

                    var px = cx + x;
                    var py = cy + y;
                    if (px < 0 || px >= _resolution || py < 0 || py >= _resolution)
                        continue;

                    _texture.SetPixel(px, py, _brushColor);
                }
            }
        }

        /// <summary>Encode the current drawing to PNG bytes (for sending over the network).</summary>
        public byte[] GetPngBytes()
        {
            return _texture != null ? _texture.EncodeToPNG() : null;
        }
    }
}