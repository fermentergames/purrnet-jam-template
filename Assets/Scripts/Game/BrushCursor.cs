using UnityEngine;
using UnityEngine.UI;

namespace Jam
{
    /// <summary>
    /// A cartoony brush cursor: a brush tip at the pointer, held by a paw, with an
    /// arm extending toward the nearest screen edge. Bobs gently (floaty) and shrinks
    /// slightly while the player is actively painting (simulating the brush being
    /// pressed down).
    ///
    /// The cursor GameObject should be a child of a full-screen RectTransform (e.g.
    /// the drawing panel) so its local space matches screen space and the arm can
    /// reach the nearest screen edge.
    /// </summary>
    public class BrushCursor : MonoBehaviour
    {
        private Image _brush;
        private Image _paw;
        private Image _arm;
        private RectTransform _brushRt;
        private RectTransform _pawRt;
        private RectTransform _armRt;
        private float _time;
        private float _baseScale = 1f;

        public void Init(Image brush, Image paw, Image arm)
        {
            _brush = brush;
            _paw = paw;
            _arm = arm;
            _brushRt = brush.rectTransform;
            _pawRt = paw.rectTransform;
            _armRt = arm.rectTransform;
            _baseScale = _brushRt.localScale.x;
        }

        /// <summary>Drive the cursor from a screen-space pointer position.</summary>
        public void UpdateCursor(Vector2 screenPos, bool painting)
        {
            _time += Time.deltaTime;

            var parent = _brushRt.parent as RectTransform;
            if (parent == null)
                return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, null, out var local);

            // Floaty bob.
            var bob = Mathf.Sin(_time * 3f) * 3f;

            // Shrink slightly while painting (brush pressed down).
            var scale = _baseScale * (painting ? 0.82f : 1f);
            _brushRt.localScale = Vector3.one * scale;
            _pawRt.localScale = Vector3.one * scale;

            // Brush tip at the pointer.
            _brushRt.anchoredPosition = local + new Vector2(0f, bob);

            // Paw holds the brush handle (offset behind the tip).
            _pawRt.anchoredPosition = local + new Vector2(0f, 176f + bob);

            // Arm extends from the paw toward the nearest screen edge.
            UpdateArm(local, parent);
        }

        private void UpdateArm(Vector2 local, RectTransform parent)
        {
            var half = parent.rect.size * 0.5f;
            var dLeft = local.x + half.x;
            var dRight = half.x - local.x;
            var dBottom = local.y + half.y;
            var dTop = half.y - local.y;

            var min = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBottom, dTop));
            Vector2 dir;
            if (min == dLeft) dir = Vector2.left;
            else if (min == dRight) dir = Vector2.right;
            else if (min == dBottom) dir = Vector2.down;
            else dir = Vector2.up;

            var pawPos = _pawRt.anchoredPosition;
            var length = Mathf.Max(20f, min);
            _armRt.anchoredPosition = pawPos + dir * (length * 0.5f);
            _armRt.sizeDelta = new Vector2(72f, length);

            // Rotate the arm to point along the direction (arm sprite is vertical).
            var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _armRt.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
        }
    }
}