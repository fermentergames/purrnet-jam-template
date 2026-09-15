using UnityEngine;
using UnityEngine.UI;

namespace Jam
{
    /// <summary>
    /// A cartoony brush cursor made of ONE combined sprite (the paw/brush, with the
    /// arm baked into the art). The brush tip is at the TOP CENTER of the sprite, so
    /// the sprite is positioned with its top-center on the pointer and extends
    /// downward (the arm part). The sprite is scaled up while keeping its native
    /// aspect ratio. Bobs gently (floaty) and shrinks slightly while painting
    /// (raise/lower). Somewhat transparent so the drawing shows through.
    ///
    /// The cursor GameObject should be a child of a full-screen RectTransform (e.g.
    /// the drawing panel) so its local space matches screen space.
    /// </summary>
    public class BrushCursor : MonoBehaviour
    {
        private Image _tip;
        private RectTransform _tipRt;
        private float _time;
        private float _baseScale = 1f;
        private float _canvasHalfWidth = 1f; // horizontal half-extent of the drawing canvas
        private float _currentTilt;           // smoothed tilt (lerps toward target)

        public void Init(Image tip)
        {
            _tip = tip;
            _tipRt = tip.rectTransform;
            _baseScale = _tipRt.localScale.x;

            // Pivot at the TOP CENTER so the tip stays pinned to the pointer when the
            // sprite scales (raise/lower) and rotates.
            _tipRt.pivot = new Vector2(0.5f, 1f);

            // Fix the aspect ratio from the created size (keeps it stable when resized).
            var sprite = tip.sprite;
            if (sprite != null && sprite.rect.height > 0f)
            {
                var aspect = sprite.rect.width / sprite.rect.height;
                _tipRt.sizeDelta = new Vector2(_tipRt.sizeDelta.x, _tipRt.sizeDelta.x / aspect);
            }

            // Somewhat transparent so the drawing shows through.
            _tip.color = new Color(1f, 1f, 1f, 0.5f);
        }

        /// <summary>Resize the cursor to the given width, keeping its native aspect ratio.</summary>
        public void SetSize(float width)
        {
            if (_tipRt == null)
                return;
            var sprite = _tip != null ? _tip.sprite : null;
            if (sprite != null && sprite.rect.height > 0f)
            {
                var aspect = sprite.rect.width / sprite.rect.height;
                _tipRt.sizeDelta = new Vector2(width, width / aspect);
            }
        }

        /// <summary>Set the horizontal half-extent of the drawing canvas (used for tilt).</summary>
        public void SetCanvasHalfWidth(float halfWidth)
        {
            _canvasHalfWidth = Mathf.Max(1f, halfWidth);
        }

        /// <summary>Drive the cursor from a screen-space pointer position.</summary>
        public void UpdateCursor(Vector2 screenPos, bool painting)
        {
            _time += Time.deltaTime;

            var parent = _tipRt.parent as RectTransform;
            if (parent == null)
                return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, null, out var local);

            // Floaty bob.
            var bob = Mathf.Sin(_time * 2f) * 3f;

            // Shrink slightly while painting (brush pressed down). Pivot is at the top
            // center, so the tip stays put while the rest scales down.
            var scale = _baseScale * (painting ? 0.95f : 1f) * 2f;
            _tipRt.localScale = Vector3.one * scale;

            // Top-center (the tip) pinned to the pointer.
            _tipRt.anchoredPosition = local + new Vector2(0f, bob);

            // Passive oscillation + tilt based on horizontal position relative to the
            // CANVAS bounds: -20deg at the canvas left edge, 0 at center, +20deg at the
            // right edge. Uses the canvas half-width (not the full screen) so the tilt
            // maps to the drawing area. The tilt lerps toward its target for a smooth
            // transition instead of snapping.
            var targetTilt = Mathf.Clamp(local.x / _canvasHalfWidth, -1f, 1f) * 50f;
            _currentTilt = Mathf.Lerp(_currentTilt, targetTilt, Time.deltaTime * 6f);
            var osc = Mathf.Sin(_time * 1.4f) * 5f;
            _tipRt.localRotation = Quaternion.Euler(0f, 0f, _currentTilt + osc);
        }
    }
}