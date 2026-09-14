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
        public Vector2 offset = new Vector2(16, -16);

        private RectTransform _rt;

        private void Awake()
        {
            _rt = GetComponent<RectTransform>();
        }

        private void LateUpdate()
        {
            if (_rt == null || canvasRect == null)
                return;
            // Convert the screen-space offset into the canvas's local space, accounting
            // for the canvas's current rotation/scale.
            var local = canvasRect.InverseTransformVector(new Vector3(offset.x, offset.y, 0f));
            _rt.anchoredPosition = new Vector2(local.x, local.y);
        }
    }
}