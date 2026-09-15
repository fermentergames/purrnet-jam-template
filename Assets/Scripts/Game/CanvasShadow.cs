using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Keeps a child shadow's offset consistent in screen space relative to a moving
    /// canvas. The shadow is a child of the canvas (so it inherits the canvas's
    /// transforms), but its anchored offset is recomputed each frame from the canvas's
    /// current transform so the shadow doesn't rotate/scale with the canvas.
    /// </summary>
    public class CanvasShadow : MonoBehaviour
    {
        public RectTransform canvasRect;
        [Tooltip("Shadow offset as a fraction of the canvas size (e.g. 0.08 = 8% of the canvas).")]
        public Vector2 offset = new Vector2(0.08f, -0.08f);

        private RectTransform _rt;

        private void Awake()
        {
            _rt = GetComponent<RectTransform>();
        }

        private void LateUpdate()
        {
            if (_rt == null || canvasRect == null)
                return;
            // Scale the offset with the canvas size so the shadow stays proportional on
            // any screen, then convert into the canvas's local space (accounting for the
            // canvas's current rotation/scale from the mover).
            var canvasSize = canvasRect.rect.size;
            var scaled = new Vector2(offset.x * canvasSize.x, offset.y * canvasSize.y);
            var local = canvasRect.InverseTransformVector(new Vector3(scaled.x, scaled.y, 0f));
            _rt.anchoredPosition = new Vector2(local.x, local.y);
        }
    }
}