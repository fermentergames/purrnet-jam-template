using System.Collections;
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
        private RectTransform _jackBox;
        private Image _jackBoxImage;
        private Coroutine _jackBoxShake;
        private Vector2 _jackBoxBasePos;
        private RectTransform _spring;
        private Vector2 _springBase;
        private float _springH = 120f;
        private RectTransform _boatHull;
        private Vector2 _boatBasePos;
        private RectTransform _water;
        private Vector2 _waterBasePos;
        private RectTransform _waterFront;
        private Vector2 _waterFrontBasePos;
        private bool _waterSliding;
        private RectTransform _platter;
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
                case MovementMode.Spin: AnimateRecord(); break;
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
            var canvas = CanvasSize;
            var diameter = canvas * 0.8f;
            _wheelRadius = diameter * 0.5f;

            _wheelL = CreatePropImage("WheelL", _rt, _theme.wheelSprite, new Vector2(diameter, diameter));
            _wheelL.anchorMin = new Vector2(0f, 0f);
            _wheelL.anchorMax = new Vector2(0f, 0f);
            _wheelL.pivot = new Vector2(0.5f, 0.5f);
            _wheelL.anchoredPosition = new Vector2(_wheelRadius * 0.4f, -_wheelRadius * 0.3f);

            _wheelR = CreatePropImage("WheelR", _rt, _theme.wheelSprite, new Vector2(diameter, diameter));
            _wheelR.anchorMin = new Vector2(1f, 0f);
            _wheelR.anchorMax = new Vector2(1f, 0f);
            _wheelR.pivot = new Vector2(0.5f, 0.5f);
            _wheelR.anchoredPosition = new Vector2(-_wheelRadius * 0.4f, -_wheelRadius * 0.3f);
        }

        private void AnimateWheels()
        {
            if (_wheelL == null || _wheelR == null || _mover == null)
                return;
            // Rolling: rotation angle = displacement / radius. Moving right (+x) = clockwise (negative z).
            var angle = -(_mover.SwayOffset.x / _wheelRadius) * Mathf.Rad2Deg * 2f;
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
            var canvas = CanvasSize;
            var hullW = canvas * 2.8f;
            var hullH = canvas * 1.2f;

            // Boat hangs below the canvas with its TOP overlapping the canvas bottom (so
            // it looks attached even if the sprite has transparent padding). It inherits
            // the wobble rotation (child of the moving canvas).
            _boatHull = CreatePropImage("BoatHull", _rt, _theme.boatSprite, new Vector2(hullW, hullH));
            _boatHull.anchorMin = new Vector2(0.5f, 0f);
            _boatHull.anchorMax = new Vector2(0.5f, 0f);
            _boatHull.pivot = new Vector2(0.5f, 0.5f);
            _boatHull.anchoredPosition = new Vector2(0f, -hullH * 0.0f); // top overlaps the canvas bottom
            _boatBasePos = _boatHull.anchoredPosition;

            // Water/waves: a full-screen-width band flush with the screen bottom, whose
            // height reaches up to the canvas bottom edge (where the boat sits).
            if (_theme.waterSprite != null && _canvasWrap != null)
            {
                var screenRt = _canvasWrap.parent as RectTransform;
                if (screenRt != null)
                {
                    var screenW = screenRt.rect.width;
                    var screenH = screenRt.rect.height;
                    var canvasBottomWorld = _rt.TransformPoint(new Vector3(0f, -_rt.rect.height * 0.5f, 0f));
                    var canvasBottomLocal = screenRt.InverseTransformPoint(canvasBottomWorld);
                    // screenRt pivot is (0.5, 0.5), so local y=0 is the center; add half the
                    // height to get the distance from the screen bottom up to the canvas bottom.
                    var waterH = Mathf.Max(0f, canvasBottomLocal.y + screenH * 0.5f);
                    // Width covers the horizontal bob (amplitude = canvas*0.2) with margin,
                    // so the edges never show on any screen (esp. mobile, where the canvas
                    // is large and the bob is big).
                    var waterWidth = screenW + 2f * canvas * 0.2f * 1.2f;

                    // Back water band (behind the boat), wider than the screen so the
                    // horizontal bob never reveals the edges. Slightly higher + darker so
                    // it reads as the distant layer.
                    _water = CreateWaterBand(screenRt, waterWidth, waterH);
                    _water.anchoredPosition = new Vector2(0f, waterH * 0.08f); // slightly higher
                    _waterBasePos = _water.anchoredPosition;
                    _water.GetComponent<Image>().color = new Color(0.55f, 0.6f, 0.75f, 1f); // darker tint
                    _water.transform.SetSiblingIndex(_canvasWrap.GetSiblingIndex()); // behind the canvas

                    // Foreground water band (in front of the boat), same size, bobs with a
                    // slight timing offset for a parallax/depth feel. Slightly lower.
                    _waterFront = CreateWaterBand(screenRt, waterWidth, waterH);
                    _waterFront.anchoredPosition = new Vector2(0f, -waterH * 0.18f); // slightly lower
                    _waterFrontBasePos = _waterFront.anchoredPosition;
                    _waterFront.transform.SetSiblingIndex(_canvasWrap.GetSiblingIndex() + 1); // in front of the canvas/boat

                    // Both start offscreen below; SlideInWater() slides them up with the canvas.
                    _waterSliding = true;
                    _water.anchoredPosition = _waterBasePos + new Vector2(0f, -screenH);
                    _waterFront.anchoredPosition = _waterFrontBasePos + new Vector2(0f, -screenH);
                }
            }
        }

        private void AnimateBoat()
        {
            var canvas = CanvasSize;
            // Back water band bobs horizontally (wider than the screen so no edges show).
            // Skipped while it's sliding in from offscreen.
            if (_water != null && !_waterSliding)
            {
                var bob = Mathf.Sin(Time.time * 2.2f) * canvas * 0.2f;
                _water.anchoredPosition = _waterBasePos + new Vector2(bob, 0f);
            }
            // Foreground water band bobs with a slight timing offset for a parallax feel.
            if (_waterFront != null && !_waterSliding)
            {
                var bob = Mathf.Sin(Time.time * 2.2f + 0.8f) * canvas * 0.2f;
                _waterFront.anchoredPosition = _waterFrontBasePos + new Vector2(bob, 0f);
            }
            // The boat gently bobs on the water.
            if (_boatHull != null)
            {
                var bob = Mathf.Sin(Time.time * 1.2f + 0.5f) * canvas * 0.01f;
                _boatHull.anchoredPosition = _boatBasePos + new Vector2(0f, bob);
            }
        }

        /// <summary>Slide the water band up from offscreen (called when the canvas slides in).</summary>
        public void SlideInWater()
        {
            if (_water == null || !_waterSliding)
                return;
            StartCoroutine(SlideWaterIn());
        }

        private IEnumerator SlideWaterIn()
        {
            var screenRt = _canvasWrap != null ? _canvasWrap.parent as RectTransform : null;
            var offY = -(screenRt != null ? screenRt.rect.height : 1080f);
            var start = _waterBasePos + new Vector2(0f, offY);
            var end = _waterBasePos;
            var startF = _waterFrontBasePos + new Vector2(0f, offY);
            var endF = _waterFrontBasePos;
            _water.anchoredPosition = start;
            if (_waterFront != null)
                _waterFront.anchoredPosition = startF;
            var duration = 0.4f;
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                var p = Mathf.Clamp01(t / duration);
                var e = 1f - (1f - p) * (1f - p); // ease-out
                _water.anchoredPosition = Vector2.Lerp(start, end, e);
                if (_waterFront != null)
                    _waterFront.anchoredPosition = Vector2.Lerp(startF, endF, e);
                yield return null;
            }
            _water.anchoredPosition = end;
            if (_waterFront != null)
                _waterFront.anchoredPosition = endF;
            _waterSliding = false;
        }

        // ---------------------------------------------------------------
        // Spin — a record platter that spins with the canvas
        // ---------------------------------------------------------------

        private void BuildRecord()
        {
            if (_theme.platterSprite == null)
                return;
            var canvas = CanvasSize;
            // On mobile (portrait) the platter is 70% of its normal size.
            var scale = 1f;
            var screenRt = _canvasWrap != null ? _canvasWrap.parent as RectTransform : null;
            if (screenRt != null && screenRt.rect.width < screenRt.rect.height)
                scale = 0.7f;
            var platter = CreatePropImage("Platter", _rt, _theme.platterSprite, new Vector2(canvas * 2.5f * scale, canvas * 2.5f * scale));
            platter.anchorMin = new Vector2(0.5f, 0.5f);
            platter.anchorMax = new Vector2(0.5f, 0.5f);
            platter.pivot = new Vector2(0.5f, 0.5f);
            platter.anchoredPosition = Vector2.zero;
            _platter = platter;
        }

        private void AnimateRecord()
        {
            if (_platter == null || _mover == null)
                return;
            // The platter is a child of the canvas (so it inherits the canvas's spin), and
            // adding the canvas's spin angle again makes it rotate at 2x the canvas rate.
            _platter.localRotation = Quaternion.Euler(0f, 0f, _mover.SpinAngle * 8f);
        }

        // ---------------------------------------------------------------
        // Shake — suspension springs that compress/jitter with the shake
        // ---------------------------------------------------------------

        private void BuildSprings()
        {
            if (_theme.springSprite == null || _canvasWrap == null)
                return;
            var screenRt = _canvasWrap.parent as RectTransform;
            if (screenRt == null)
                return;
            var canvas = CanvasSize;
            var screenW = screenRt.rect.width;
            var screenH = screenRt.rect.height;
            var boxW = screenW * 0.4f;
            var boxH = screenH * 0.3f;
            var boxLift = screenH * 0.01f; // sit a bit above the screen bottom

            // Stationary jack-in-the-box at the bottom of the screen.
            if (_theme.jackBoxSprite != null)
            {
                _jackBox = CreatePropImage("JackBox", screenRt, _theme.jackBoxSprite, new Vector2(boxW, boxH));
                _jackBoxImage = _jackBox.GetComponent<Image>();
                _jackBox.anchorMin = new Vector2(0.5f, 0.5f);
                _jackBox.anchorMax = new Vector2(0.5f, 0.5f);
                _jackBox.pivot = new Vector2(0.5f, 0.5f);
                _jackBox.anchoredPosition = new Vector2(0f, -screenH * 0.5f + boxH * 0.5f + boxLift);
                // Sit just behind the spring/canvas but IN FRONT of the full-screen mode
                // background (SetAsFirstSibling would hide it behind that background).
                _jackBox.transform.SetSiblingIndex(Mathf.Max(0, _canvasWrap.GetSiblingIndex() - 1));
                // Subtle pre-pop shake until OpenJackBox() is called.
                _jackBoxBasePos = _jackBox.anchoredPosition;
                _jackBoxShake = StartCoroutine(ShakeJackBox());
            }

            // Single spring coming out of the box, top pinned to the canvas bottom. The
            // bottom sits a tiny bit below the box top so it reads as emerging from inside.
            var boxTop = new Vector2(0f, -screenH * 0.5f + boxH + boxLift);
            var springBottom = boxTop + new Vector2(0f, -boxH * 0.23f);
            var canvasBottomRest = (Vector2)screenRt.InverseTransformPoint(_rt.TransformPoint(new Vector3(0f, -_rt.rect.height * 0.5f, 0f)));
            _springH = Mathf.Max(1f, (canvasBottomRest - springBottom).magnitude);

            var springW = canvas * 0.2f;
            _spring = CreatePropImage("Spring", screenRt, _theme.springSprite, new Vector2(springW, _springH));
            _spring.anchorMin = new Vector2(0.5f, 0.5f);
            _spring.anchorMax = new Vector2(0.5f, 0.5f);
            _spring.pivot = new Vector2(0.5f, 0f); // rotate around the bottom
            _springBase = springBottom;
            _spring.anchoredPosition = _springBase;
            _spring.transform.SetSiblingIndex(_canvasWrap.GetSiblingIndex()); // behind the canvas
        }

        private void AnimateSprings()
        {
            if (_spring == null || _mover == null || _canvasWrap == null)
                return;
            var screenRt = _canvasWrap.parent as RectTransform;
            if (screenRt == null)
                return;
            // Canvas bottom (moves with the shake) in the spring's parent space.
            var canvasBottomWorld = _rt.TransformPoint(new Vector3(0f, -_rt.rect.height * 0.5f, 0f));
            var canvasBottomLocal = screenRt.InverseTransformPoint(canvasBottomWorld);
            ApplySpring(_spring, _springBase, canvasBottomLocal);
        }

        /// <summary>Rotate a spring around its bottom anchor so its top reaches `top`.</summary>
        private void ApplySpring(RectTransform spring, Vector2 bottom, Vector2 top)
        {
            var v = top - bottom;
            // Negated: in UI space (y-up, positive z = CCW) a point at (0,L) rotated by θ
            // becomes (-L·sinθ, L·cosθ), so to lean the top toward v we need -atan2(v.x, v.y).
            var angle = -Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
            var scaleY = _springH > 0f ? v.magnitude / _springH : 1f;
            spring.localRotation = Quaternion.Euler(0f, 0f, angle);
            spring.localScale = new Vector3(1f, scaleY, 1f);
        }

        /// <summary>
        /// Jack-in-the-box intro: the canvas-wrap anchoredPosition that puts the canvas at
        /// the top of the box (the spring base). Null when not in Shake mode.
        /// </summary>
        public Vector2? SpringCanvasStartAP
        {
            get
            {
                if (_spring == null || _canvasWrap == null)
                    return null;
                var parent = _canvasWrap.parent as RectTransform;
                if (parent == null)
                    return null;
                var rect = parent.rect;
                // The canvasWrap's anchor reference point, relative to the parent's center.
                var acx = (_canvasWrap.anchorMin.x + _canvasWrap.anchorMax.x) * 0.5f - 0.5f;
                var acy = (_canvasWrap.anchorMin.y + _canvasWrap.anchorMax.y) * 0.5f - 0.5f;
                var anchorRef = new Vector2(acx * rect.width, acy * rect.height);
                return _springBase - anchorRef;
            }
        }

        /// <summary>Swap the box to its "open" sprite and stop the pre-pop shake (called when the canvas pops out).</summary>
        public void OpenJackBox()
        {
            if (_jackBoxShake != null)
            {
                StopCoroutine(_jackBoxShake);
                _jackBoxShake = null;
            }
            if (_jackBox != null)
            {
                _jackBox.anchoredPosition = _jackBoxBasePos; // back to normal
                _jackBox.localRotation = Quaternion.identity;
            }
            if (_jackBoxImage != null && _theme.jackBoxOpenSprite != null)
                _jackBoxImage.sprite = _theme.jackBoxOpenSprite;
        }

        /// <summary>Subtle pre-pop shake so the box looks like it's about to burst open.</summary>
        private IEnumerator ShakeJackBox()
        {
            while (true)
            {
                var t = Time.time;
                _jackBox.anchoredPosition = _jackBoxBasePos + new Vector2(
                    Mathf.Sin(t * 30f) * 4f,
                    Mathf.Sin(t * 37f) * 3f);
                _jackBox.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 25f) * 2f);
                yield return null;
            }
        }

        // ---------------------------------------------------------------
        // ZoomOut — top-down trampoline; canvas bounces up (big) / down (small)
        // ---------------------------------------------------------------

        private void BuildTrampoline()
        {
            if (_theme.trampolineSprite == null || _canvasWrap == null)
                return;
            var canvas = CanvasSize;
            // Top-down view: the trampoline is centered directly under the canvas.
            var matSize = canvas * 2.6f;
            _bounceHeight = canvas * 0.4f;

            _trampoline = CreatePropImage("Trampoline", _canvasWrap, _theme.trampolineSprite, new Vector2(matSize, matSize));
            _trampoline.anchorMin = new Vector2(0.5f, 0.5f);
            _trampoline.anchorMax = new Vector2(0.5f, 0.5f);
            _trampoline.pivot = new Vector2(0.5f, 0.5f);
            // Offset the trampoline down by the bounce height so it sits at the canvas's
            // lowest point (the canvas bounces up from here).
            _trampoline.anchoredPosition = new Vector2(0f, -_bounceHeight);
        }

        private void AnimateTrampoline()
        {
            if (_trampoline == null || _mover == null || _rt == null)
                return;
            // Bounce: the canvas is UP (high) when big (zoomed in = jumping toward the
            // viewer) and DOWN (low) when small (zoomed out = landing on the trampoline).
            var osc = _mover.ZoomPhase; // 0..1, 1 = most zoomed out (small)
            var bounce = _bounceHeight * (1f - osc);
            // Offset the whole bounce down by the bounce height, matching the trampoline,
            // so the canvas lands on the mat at its lowest point and springs up from there.
            _rt.anchoredPosition = _mover.BasePosition + new Vector2(0f, bounce - _bounceHeight);

            // The mat squashes when the canvas lands (small) and stretches when it's up.
            var squish = 1f - osc * 0.3f;
            _trampoline.localScale = new Vector3(1f + osc * 0.25f, squish, 1f);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private float CanvasSize => _rt != null ? _rt.sizeDelta.x : 512f;

        private RectTransform CreateWaterBand(RectTransform screenRt, float width, float waterH)
        {
            var w = CreatePropImage("Water", screenRt, _theme.waterSprite, new Vector2(width, waterH));
            w.anchorMin = new Vector2(0.5f, 0f);
            w.anchorMax = new Vector2(0.5f, 0f);
            w.pivot = new Vector2(0.5f, 0f);
            w.anchoredPosition = new Vector2(0f, 0f); // resting: flush with the screen bottom
            w.GetComponent<Image>().preserveAspect = false; // stretch to fill the full width
            return w;
        }

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
            if (_jackBoxShake != null)
            {
                StopCoroutine(_jackBoxShake);
                _jackBoxShake = null;
            }
            _jackBox = null;
            _jackBoxImage = null;
            _spring = null;
            _boatHull = null;
            _platter = null;
            _water = null;
            _waterFront = null;
            _waterSliding = false;
            _trampoline = null;
        }
    }
}