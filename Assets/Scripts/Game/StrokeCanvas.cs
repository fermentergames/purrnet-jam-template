using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Jam
{
    /// <summary>
    /// A single vector stroke: an ordered list of points in canvas texture space,
    /// plus the brush size and color used. Storing strokes as vectors (instead of
    /// raster snapshots) gives us three features from one data model:
    ///   1. Undo          — drop the last stroke and re-render.
    ///   2. Per-stroke brush size (scales with canvas zoom).
    ///   3. Stroke-by-stroke reveal animation before guessing.
    /// </summary>
    [Serializable]
    public class Stroke
    {
        public List<Vector2> points = new List<Vector2>();
        public float size = 5f;
        public Color color = Color.black;
    }

    /// <summary>JSON wrapper so we can serialize a stroke list to a string (RPC-safe).</summary>
    [Serializable]
    public class StrokeData
    {
        public List<Stroke> strokes = new List<Stroke>();
    }

    /// <summary>
    /// A reusable vector-stroke drawing canvas: a RawImage showing a Texture2D that
    /// the player paints on with mouse / touch / pen. Records strokes as vectors so
    /// the drawing can be undone, replayed, and serialized for the network.
    ///
    /// Setup: add to a GameObject with a RawImage and call Init(rawImage), or assign
    /// the RawImage in the inspector.
    /// </summary>
    public class StrokeCanvas : MonoBehaviour
    {
        [SerializeField] private RawImage _display;
        [SerializeField] private int _resolution = 256;
        [SerializeField] private float _brushSize = 6f;
        [SerializeField] private Color _brushColor = Color.black;

        /// <summary>When false, the canvas is read-only (shows content but ignores input).</summary>
        public bool interactable = true;

        private Texture2D _texture;
        private readonly List<Stroke> _strokes = new List<Stroke>();
        private Stroke _current;
        private bool _drawing;
        private Vector2 _lastPixel;
        private BrushCursor _brushCursor;

        /// <summary>Fired whenever a stroke is committed (drawn) or removed (undo).</summary>
        public event Action onStrokesChanged;

        /// <summary>Fired when the player starts a new stroke (pointer down on the canvas).</summary>
        public event Action onStrokeStart;

        public int StrokeCount => _strokes.Count;

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

        // ---------------------------------------------------------------
        // Brush + public drawing API
        // ---------------------------------------------------------------

        public void SetBrushColor(Color color) => _brushColor = color;
        public void SetBrushSize(float size) => _brushSize = Mathf.Max(1f, size);
        public float BrushSize => _brushSize;

        /// <summary>Assign a BrushCursor to follow the pointer while drawing.</summary>
        public void SetBrushCursor(BrushCursor cursor) => _brushCursor = cursor;

        /// <summary>Wipe the canvas and forget all strokes.</summary>
        public void Clear()
        {
            _strokes.Clear();
            _current = null;
            ClearTexture();
            onStrokesChanged?.Invoke();
        }

        /// <summary>Remove the last committed stroke and re-render.</summary>
        public void Undo()
        {
            if (_current != null)
            {
                _current = null; // cancel an in-progress stroke first
                RenderAll();
                return;
            }
            if (_strokes.Count == 0)
                return;
            _strokes.RemoveAt(_strokes.Count - 1);
            RenderAll();
            onStrokesChanged?.Invoke();
        }

        /// <summary>Commit any in-progress stroke so it's included in the data (used on submit).</summary>
        public void CommitCurrentStroke()
        {
            if (_current != null)
            {
                _strokes.Add(_current);
                _current = null;
                onStrokesChanged?.Invoke();
            }
        }

        // ---------------------------------------------------------------
        // Input
        // ---------------------------------------------------------------

        private void Update()
        {
            if (!interactable || _texture == null)
            {
                _drawing = false;
                if (_brushCursor != null)
                    _brushCursor.gameObject.SetActive(false);
                return;
            }

            // Pointer covers mouse, touch and pen under the new Input System.
            var pointer = Pointer.current;
            if (pointer == null)
                return;

            var screenPos = pointer.position.ReadValue();
            UpdateCursor(screenPos);
            var press = pointer.press;

            if (press.wasPressedThisFrame)
            {
                if (IsPointerOverOtherUI(screenPos))
                    return;

                _drawing = true;
                _lastPixel = ScreenToTexturePixel(screenPos);
                _current = new Stroke { size = _brushSize, color = _brushColor };
                _current.points.Add(_lastPixel);
                DrawDisc(_lastPixel, _brushSize, _brushColor);
                _texture.Apply();
                onStrokeStart?.Invoke();
            }
            else if (press.isPressed && _drawing)
            {
                var current = ScreenToTexturePixel(screenPos);
                if (Vector2.Distance(current, _lastPixel) < 2f)
                    return;

                DrawSegment(_lastPixel, current, _brushSize, _brushColor);
                _current.points.Add(current);
                _lastPixel = current;
                _texture.Apply();
            }
            else if (press.wasReleasedThisFrame && _drawing)
            {
                _drawing = false;
                if (_current != null)
                {
                    _strokes.Add(_current);
                    _current = null;
                    onStrokesChanged?.Invoke();
                }
            }
        }

        /// <summary>Show/hide and drive the brush cursor at the pointer.</summary>
        private void UpdateCursor(Vector2 screenPos)
        {
            if (_brushCursor == null)
                return;
            // Visible for the whole drawing phase, not just while hovering the canvas.
            var show = interactable;
            if (_brushCursor.gameObject.activeSelf != show)
                _brushCursor.gameObject.SetActive(show);
            if (!show)
                return;
            _brushCursor.UpdateCursor(screenPos, _drawing);
        }

        /// <summary>True if the pointer is over a UI element that is NOT this canvas.</summary>
        private bool IsPointerOverOtherUI(Vector2 screenPos)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;
            if (!eventSystem.IsPointerOverGameObject())
                return false;
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

        // ---------------------------------------------------------------
        // Rendering
        // ---------------------------------------------------------------

        /// <summary>Re-render every stroke from scratch (used after undo/clear/load).</summary>
        public void RenderAll()
        {
            ClearTexture();
            foreach (var stroke in _strokes)
                RenderStroke(stroke);
            _texture.Apply();
        }

        /// <summary>Render only the first <paramref name="count"/> strokes (for the reveal animation).</summary>
        public void RenderUpTo(int count)
        {
            ClearTexture();
            var max = Mathf.Clamp(count, 0, _strokes.Count);
            for (var i = 0; i < max; i++)
                RenderStroke(_strokes[i]);
            _texture.Apply();
        }

        /// <summary>
        /// Render all strokes before <paramref name="strokeIndex"/> fully, then render
        /// stroke <paramref name="strokeIndex"/> only up to <paramref name="pointIndex"/>
        /// points. Used to trace a stroke's path progressively.
        /// </summary>
        public void RenderUpToPoint(int strokeIndex, int pointIndex)
        {
            ClearTexture();
            for (var i = 0; i < strokeIndex; i++)
                RenderStroke(_strokes[i]);
            if (strokeIndex >= 0 && strokeIndex < _strokes.Count)
                RenderStrokePartial(_strokes[strokeIndex], pointIndex);
            _texture.Apply();
        }

        private void RenderStroke(Stroke stroke)
        {
            if (stroke == null || stroke.points.Count == 0)
                return;
            if (stroke.points.Count == 1)
            {
                DrawDisc(stroke.points[0], stroke.size, stroke.color);
                return;
            }
            for (var i = 1; i < stroke.points.Count; i++)
                DrawSegment(stroke.points[i - 1], stroke.points[i], stroke.size, stroke.color);
        }

        private void RenderStrokePartial(Stroke stroke, int pointCount)
        {
            if (stroke == null || stroke.points.Count == 0 || pointCount <= 0)
                return;
            var n = Mathf.Min(pointCount, stroke.points.Count);
            if (n == 1)
            {
                DrawDisc(stroke.points[0], stroke.size, stroke.color);
                return;
            }
            for (var i = 1; i < n; i++)
                DrawSegment(stroke.points[i - 1], stroke.points[i], stroke.size, stroke.color);
        }

        private void DrawSegment(Vector2 from, Vector2 to, float radius, Color color)
        {
            var dist = Vector2.Distance(from, to);
            var steps = Mathf.Max(1, Mathf.CeilToInt(dist));
            for (var i = 0; i <= steps; i++)
                DrawDisc(Vector2.Lerp(from, to, i / (float)steps), radius, color);
        }

        private void DrawDisc(Vector2 center, float radius, Color color)
        {
            var cx = Mathf.RoundToInt(center.x);
            var cy = Mathf.RoundToInt(center.y);
            var r = Mathf.Max(1, Mathf.RoundToInt(radius));

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

                    _texture.SetPixel(px, py, color);
                }
            }
        }

        // ---------------------------------------------------------------
        // Network / reveal helpers
        // ---------------------------------------------------------------

        /// <summary>Encode the current drawing to PNG bytes (composited final image).</summary>
        public byte[] GetPngBytes()
        {
            return _texture != null ? _texture.EncodeToPNG() : null;
        }

        /// <summary>Serialize all strokes to a JSON string (RPC-safe payload).</summary>
        public string GetStrokeData()
        {
            var data = new StrokeData();
            data.strokes.AddRange(_strokes);

            // Cap total points so the RPC payload stays under the packet size limit.
            const int maxPoints = 4000;
            var total = 0;
            for (var i = 0; i < data.strokes.Count; i++)
                total += data.strokes[i].points.Count;
            if (total > maxPoints)
            {
                var scale = (float)maxPoints / total;
                for (var i = 0; i < data.strokes.Count; i++)
                {
                    var pts = data.strokes[i].points;
                    var newCount = Mathf.Max(1, Mathf.RoundToInt(pts.Count * scale));
                    if (newCount < pts.Count)
                        pts.RemoveRange(newCount, pts.Count - newCount);
                }
            }

            return JsonUtility.ToJson(data);
        }

        /// <summary>Load strokes from a JSON string and render them.</summary>
        public void SetStrokeData(string json, bool render = true)
        {
            _strokes.Clear();
            _current = null;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var data = JsonUtility.FromJson<StrokeData>(json);
                    if (data != null && data.strokes != null)
                        _strokes.AddRange(data.strokes);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[StrokeCanvas] Failed to parse stroke data: {e.Message}");
                }
            }
            if (render)
                RenderAll();
            onStrokesChanged?.Invoke();
        }

        /// <summary>
        /// Coroutine: reveal the drawing by TRACING each stroke's path progressively.
        /// Each stroke is drawn over <paramref name="perStrokeDuration"/> seconds,
        /// stepping through its points so the line visibly draws itself.
        ///
        /// This draws INCREMENTALLY (only the new segment per step), so it stays fast
        /// even for complex drawings — it does NOT re-render the whole texture each step.
        /// </summary>
        public IEnumerator PlayReveal(float perStrokeDuration, float initialDelay = 0f)
        {
            if (initialDelay > 0f)
                yield return new WaitForSeconds(initialDelay);

            ClearTexture();

            // Count total points so we can cap the overall reveal time — complex
            // drawings shouldn't take forever.
            var totalPoints = 0;
            for (var s = 0; s < _strokes.Count; s++)
                if (_strokes[s] != null)
                    totalPoints += _strokes[s].points.Count;

            if (totalPoints == 0)
            {
                _texture.Apply();
                yield break;
            }

            var maxTotal = 1.0f;
            var step = Mathf.Min(perStrokeDuration, maxTotal / totalPoints);

            for (var s = 0; s < _strokes.Count; s++)
            {
                var stroke = _strokes[s];
                var pointCount = stroke != null ? stroke.points.Count : 0;
                if (pointCount <= 0)
                    continue;

                for (var p = 0; p < pointCount; p++)
                {
                    if (p == 0)
                        DrawDisc(stroke.points[0], stroke.size, stroke.color);
                    else
                        DrawSegment(stroke.points[p - 1], stroke.points[p], stroke.size, stroke.color);
                    // Apply the texture less often to keep it fast.
                    if (p % 2 == 0 || p == pointCount - 1)
                        _texture.Apply();
                    yield return new WaitForSeconds(step);
                }
            }
            _texture.Apply();
        }
    }
}