using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jam
{
    /// <summary>
    /// Builds and animates decorative "props" attached to the moving drawing canvas —
    /// one themed prop per MovementMode — so each mode reads as a physical object
    /// instead of a floating canvas:
    ///   Sway    -> two wheels that roll back-and-forth with the sway
    ///   Wobble  -> a boat hull that rocks with the canvas + still water beneath
    ///   Spin    -> a record platter that spins with the canvas
    ///   Shake   -> suspension springs that compress/jitter with the shake
    ///   ZoomOut -> a top-down trampoline; the canvas bounces up (big) / down (small)
    ///
    /// Attach to the same GameObject as the CanvasMover (the moving canvas root).
    /// Call Init() once at build time, Configure(mode) when a round starts,
    /// Reset() when the drawing is submitted, and Resize() when the canvas resizes.
    ///
    /// Props are children of the moving canvas (inherit its transform) or of the
    /// canvas wrap (screen-fixed: water, trampoline). All props render BEHIND the
    /// drawing surface and never block input (raycastTarget = false).
    /// </summary>
    public class CanvasModeProps : MonoBehaviour
    {
        private CanvasMover _mover;
        private GameTheme _theme;
        private RectTransform _canvasWrap;
        private RectTransform _drawRt;   // the drawing surface (props render behind it)
        private RectTransform _rt;       // this component's RectTransform (moving canvas root)

        private MovementMode _mode = MovementMode.None;
        private readonly List<GameObject> _props = new List<GameObject>();

        // Per-mode prop references + layout data for per-frame animation.
        private RectTransform _wheelL;
        private RectTransform _wheelR;
        private float _wheelRadius = 40f;
        private RectTransform _springL;
        private RectTransform _springR;
        private Vector2 _springBaseL;
        private Vector2 _springBaseR;
        private float _springH = 120f;
        private RectTransform _boatHull;
        private Vector2 _boatBasePos;
        private RectTransform _water;
        private Vector2 _waterBasePos;
        private RectTransform _trampoline;
        private float _bounceHeight = 60f;

        /// <summary>Wire up references. Call once after the component is added.</summary>
        public void Init(CanvasMover mover, GameTheme theme, RectTransform canvasWrap, RectTransform drawRt)
        {
            _mover = mover;
            _theme = theme;
            _canvasWrap = canvasWrap;
            _drawRt = drawRt;
            _rt = GetComponent<RectTransform>();
        }

        /// <summary>Build (or hide) the props for the given movement mode.</summary>
        public void Configure(MovementMode mode)
        {
            ClearProps();
            _mode = mode;
            if (_mover == null || _theme == null)
                return;

            switch (mode)
            {
                case MovementMode.Sway: BuildWheels(); break;
                case MovementMode.Wobble: BuildBoat(); break;
                case MovementMode.Spin: BuildRecord(); break;
                case MovementMode.Shake: BuildSprings(); break;
                case MovementMode.ZoomOut: BuildTrampoline(); break;
            }
        }

        /// <summary>Hide props and restore any position this component wrote.</summary>
        public void Reset()
        {
            ClearProps();
            _mode = MovementMode.None;
            if (_mover != null && _rt != null)
                _rt.anchoredPosition = _mover.BasePosition;
        }

        /// <summary>Rebuild props after the canvas is resized (rare: screen change).</summary>
        public void Resize()
        {
            if (_mode != MovementMode.None)
                Configure(_mode);
        }

        private void Update()
        {
            if (_mode == MovementMode.None || _mover == null || _mover.Mode == MovementMode.None)
                return;

            switch (_mode)
            {
                case MovementMode.Sway: AnimateWheels(); break;
                case MovementMode.Wobble: AnimateBoat(); break;
                case MovementMode.Shake: AnimateSprings(); break;
                case MovementMode.ZoomOut: AnimateTrampoline(); break;
            }
        }

        // ---------------------------------------------------------------
        // Sway — two wheels that roll with the sway
        // ---------------------------------------------------------------

        private void BuildWheels()
        {
            if (_theme.wheelSprite == null)
                return;
            var size = CanvasSize;
            var diameter = Mathf.Min(180f, size.x * 0.32f);
            _wheelRadius = diameter * 0.5f;

            _wheelL = CreatePropImage("WheelL", _rt, _theme.wheelSprite, new Vector2(diameter, diameter));
            _wheelL.anchorMin = new Vector2(0f, 0f);
            _wheelL.anchorMax = new Vector2(0f, 0f);
            _wheelL.pivot = new Vector2(0.5f, 0.5f);
            _wheelL.anchoredPosition = new Vector2(_wheelRadius * 1.2f, -_wheelRadius * 0.5f);

            _wheelR = CreatePropImage("WheelR", _rt, _theme.wheelSprite, new Vector2(diameter, diameter));
            _wheelR.anchorMin = new Vector2(1f, 0f);
            _wheelR.anchorMax = new Vector2(1f, 0f);
            _wheelR.pivot = new Vector2(0.5f, 0.5f);
            _wheelR.anchoredPosition = new Vector2(-_wheelRadius * 1.2f, -_wheelRadius * 0.5f);
        }

        private void AnimateWheels()
        {
            if (_wheelL == null || _wheelR == null || _mover == null)
                return;
            // Rolling: rotation angle = displacement / radius. Moving right (+x) = clockwise (negative z).
            var angle = -(_mover.SwayOffset.x / _wheelRadius) * Mathf.Rad2Deg;
            _wheelL.localRotation = Quaternion.Euler(0f, 0f, angle);
            _wheelR.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        // ---------------------------------------------------------------
        // Wobble — a boat hull that rocks with the canvas + still water
        // ---------------------------------------------------------------

        private void BuildBoat()
        {
            if (_theme.boatSprite == null)
                return;
            var size = CanvasSize;
            var hullW = Mathf.Min(720f, size.x * 1.4f);
            var hullH = Mathf.Min(240f, size.y * 0.44f);

            _boatHull = CreatePropImage("BoatHull", _rt, _theme.boatSprite, new Vector2(hullW, hullH));
            _boatHull.anchorMin = new Vector2(0.5f, 0f);
            _boatHull.anchorMax = new Vector2(0.5f, 0f);
            _boatHull.pivot = new Vector2(0.5f, 0.5f);
            _boatHull.anchoredPosition = new Vector2(0f, -hullH * 0.5f); // hangs below the canvas bottom edge
            _boatBasePos = _boatHull.anchoredPosition;
            // The hull inherits the wobble rotation automatically (child of the moving canvas).

            if (_theme.waterSprite != null && _canvasWrap != null)
            {
                _water = CreatePropImage("Water", _canvasWrap, _theme.waterSprite, new Vector2(hullW * 1.6f, hullH * 0.9f));
                _water.anchorMin = new Vector2(0.5f, 0f);
                _water.anchorMax = new Vector2(0.5f, 0f);
                _water.pivot = new Vector2(0.5f, 0.5f);
                _water.anchoredPosition = new Vector2(0f, -hullH * 1.1f);
                _waterBasePos = _water.anchoredPosition;
            }
        }

        private void AnimateBoat()
        {
            // Water stays still while the boat rocks; gentle bob keeps it alive.
            if (_water != null)
            {
                var bob = Mathf.Sin(Time.time * 1.2f) * 6f;
                _water.anchoredPosition = _waterBasePos + new Vector2(bob, 0f);
            }
            if (_boatHull != null)
            {
                var bob = Mathf.Sin(Time.time * 1.2f + 0.5f) * 3f;
                _boatHull.anchoredPosition = _boatBasePos + new Vector2(0f, bob);
            }
        }

        // ---------------------------------------------------------------
        // Spin — a record platter that spins with the canvas
        // ---------------------------------------------------------------

        private void BuildRecord()
        {
            if (_theme.platterSprite == null)
                return;
            var size = CanvasSize;
            var platter = CreatePropImage("Platter", _rt, _theme.platterSprite, new Vector2(size.x * 2.5f, size.y * 2.5f));
            platter.anchorMin = new Vector2(0.5f, 0.5f);
            platter.anchorMax = new Vector2(0.5f, 0.5f);
            platter.pivot = new Vector2(0.5f, 0.5f);
            platter.anchoredPosition = Vector2.zero;
            // Inherits the spin rotation automatically (child of the moving canvas).
        }

        // ---------------------------------------------------------------
        // Shake — suspension springs that compress/jitter with the shake
        // ---------------------------------------------------------------

        private void BuildSprings()
        {
            if (_theme.springSprite == null || _canvasWrap == null)
                return;
            var size = CanvasSize;
            var springW = Mathf.Min(112f, size.x * 0.2f);
            var springH = Mathf.Min(240f, size.y * 0.44f);
            _springH = springH;

            // The spring BOTTOM is anchored to a stationary screen position below the
            // canvas; the TOP is pinned to the bottom of the canvas (which shakes). The
            // spring is a child of the canvas wrap (screen-fixed) with its pivot at the
            // bottom, so it rotates around the bottom anchor to follow the canvas.
            var basePos = _mover != null ? _mover.BasePosition : Vector2.zero;
            var springX = size.x * 0.4f;
            var bottomY = basePos.y - size.y * 0.5f - springH;

            _springL = CreatePropImage("SpringL", _canvasWrap, _theme.springSprite, new Vector2(springW, springH));
            _springL.anchorMin = new Vector2(0.5f, 0.5f);
            _springL.anchorMax = new Vector2(0.5f, 0.5f);
            _springL.pivot = new Vector2(0.5f, 0f); // rotate around the bottom
            _springBaseL = new Vector2(basePos.x - springX, bottomY);
            _springL.anchoredPosition = _springBaseL;

            _springR = CreatePropImage("SpringR", _canvasWrap, _theme.springSprite, new Vector2(springW, springH));
            _springR.anchorMin = new Vector2(0.5f, 0.5f);
            _springR.anchorMax = new Vector2(0.5f, 0.5f);
            _springR.pivot = new Vector2(0.5f, 0f);
            _springBaseR = new Vector2(basePos.x + springX, bottomY);
            _springR.anchoredPosition = _springBaseR;
        }

        private void AnimateSprings()
        {
            if (_springL == null || _springR == null || _mover == null)
                return;
            var basePos = _mover.BasePosition;
            var shake = _mover.ShakeOffset;
            var size = CanvasSize;
            var springX = size.x * 0.4f;
            var topY = basePos.y + shake.y - size.y * 0.5f;

            // Top pinned to the bottom of the canvas (follows the shake); bottom stays fixed.
            ApplySpring(_springL, _springBaseL, new Vector2(basePos.x + shake.x - springX, topY));
            ApplySpring(_springR, _springBaseR, new Vector2(basePos.x + shake.x + springX, topY));
        }

        /// <summary>Rotate a spring around its bottom anchor so its top reaches `top`.</summary>
        private void ApplySpring(RectTransform spring, Vector2 bottom, Vector2 top)
        {
            var v = top - bottom;
            var angle = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
            var scaleY = _springH > 0f ? v.magnitude / _springH : 1f;
            spring.localRotation = Quaternion.Euler(0f, 0f, angle);
            spring.localScale = new Vector3(1f, scaleY, 1f);
        }

        // ---------------------------------------------------------------
        // ZoomOut — top-down trampoline; canvas bounces up (big) / down (small)
        // ---------------------------------------------------------------

        private void BuildTrampoline()
        {
            if (_theme.trampolineSprite == null || _canvasWrap == null)
                return;
            var size = CanvasSize;
            // Top-down view: the trampoline is centered directly under the canvas.
            var matSize = Mathf.Min(840f, size.x * 1.6f);
            _bounceHeight = size.y * 0.3f;

            _trampoline = CreatePropImage("Trampoline", _canvasWrap, _theme.trampolineSprite, new Vector2(matSize, matSize));
            _trampoline.anchorMin = new Vector2(0.5f, 0.5f);
            _trampoline.anchorMax = new Vector2(0.5f, 0.5f);
            _trampoline.pivot = new Vector2(0.5f, 0.5f);
            _trampoline.anchoredPosition = Vector2.zero; // centered under the canvas
        }

        private void AnimateTrampoline()
        {
            if (_trampoline == null || _mover == null || _rt == null)
                return;
            // Bounce: the canvas is UP (high) when big (zoomed in = jumping toward the
            // viewer) and DOWN (low) when small (zoomed out = landing on the trampoline).
            var osc = _mover.ZoomPhase; // 0..1, 1 = most zoomed out (small)
            var bounce = _bounceHeight * (1f - osc);
            _rt.anchoredPosition = _mover.BasePosition + new Vector2(0f, bounce);

            // The mat squashes when the canvas lands (small) and stretches when it's up.
            var squish = 1f - osc * 0.15f;
            _trampoline.localScale = new Vector3(1f + osc * 0.1f, squish, 1f);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private Vector2 CanvasSize => _rt != null ? _rt.sizeDelta : new Vector2(512f, 512f);

        private RectTransform CreatePropImage(string name, Transform parent, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
            img.raycastTarget = false; // never block drawing input
            img.preserveAspect = true; // keep the sprite's own aspect ratio (no distortion)
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            _props.Add(go);

            // Render behind the drawing surface: just before the Draw RawImage for props
            // parented to the moving canvas, or behind the canvas for screen-fixed props.
            if (parent == _rt && _drawRt != null)
                rt.SetSiblingIndex(Mathf.Max(0, _drawRt.GetSiblingIndex()));
            else if (parent == _canvasWrap)
                rt.SetAsFirstSibling();
            return rt;
        }

        private void ClearProps()
        {
            foreach (var go in _props)
            {
                if (go != null)
                    Destroy(go);
            }
            _props.Clear();
            _wheelL = _wheelR = null;
            _springL = _springR = null;
            _boatHull = null;
            _water = null;
            _trampoline = null;
        }
    }
}