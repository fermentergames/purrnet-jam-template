using UnityEngine;

namespace Jam
{
    public enum MovementMode
    {
        None = 0,
        Sway = 1,     // horizontal sine sway
        Wobble = 2,   // rotation angle oscillates (was "Spin")
        Spin = 3,     // full continuous clockwise rotation
        Shake = 4,    // random jitter
        ZoomOut = 5   // slow zoom out (scale shrinks)
    }

    /// <summary>
    /// Per-mode movement parameters. Amplitude and frequency are intensified by
    /// SEPARATE curves over the round duration, so you can ramp one without the
    /// other (e.g. keep frequency steady while amplitude grows).
    /// </summary>
    [System.Serializable]
    public class MovementParams
    {
        public float baseAmplitude = 100f;
        public float baseFrequency = 1f;

        [Tooltip("Multiplies amplitude over the round (0..1).")]
        public AnimationCurve amplitudeCurve = MakeCurve(0.3f, 1f);

        [Tooltip("Multiplies frequency over the round (0..1).")]
        public AnimationCurve frequencyCurve = MakeCurve(0.3f, 1f);

        /// <summary>
        /// Build a curve with WEIGHTED tangents so the handles are draggable in the
        /// Inspector (instead of the default auto tangents which are hard to grab).
        /// </summary>
        public static AnimationCurve MakeCurve(float start, float end)
        {
            var k0 = new Keyframe(0f, start);
            var k1 = new Keyframe(1f, end);
            k0.weightedMode = WeightedMode.Both;
            k1.weightedMode = WeightedMode.Both;
            k0.outWeight = 1f / 3f;
            k1.inWeight = 1f / 3f;
            return new AnimationCurve(new[] { k0, k1 });
        }
    }

    /// <summary>
    /// Animates a canvas RectTransform with a movement mode whose intensity ramps
    /// up over the round. Movement is purely LOCAL (each player draws on their own
    /// canvas), so this never needs to be synced — the mode + duration come from the
    /// synced round state, but the animation itself runs on the drawer's client.
    ///
    /// Attach to the canvas display RectTransform and drive it via SetMode().
    /// </summary>
    public class CanvasMover : MonoBehaviour
    {
        [Header("Sway")]
        public MovementParams sway = new MovementParams { baseAmplitude = 120f, baseFrequency = 1.5f };

        [Header("Wobble")]
        public MovementParams wobble = new MovementParams { baseAmplitude = 18f, baseFrequency = 1.2f };

        [Header("Shake")]
        public MovementParams shake = new MovementParams { baseAmplitude = 20f, baseFrequency = 40f };

        [Header("Spin (full rotation)")]
        [Tooltip("baseAmplitude = total degrees to rotate clockwise over the round. amplitudeCurve controls rotation speed (ease-in = speeds up over time).")]
        public MovementParams spin = new MovementParams { baseAmplitude = 1080f, baseFrequency = 1f };

        [Header("ZoomOut")]
        [Tooltip("Zoom back and forth. baseAmplitude = how much to shrink (1 - minScale). baseFrequency = oscillation speed. amplitudeCurve scales the zoom amount over the round.")]
        public MovementParams zoom = new MovementParams { baseAmplitude = 0.65f, baseFrequency = 1f };

        private MovementMode _mode = MovementMode.None;
        private float _duration = 1f;
        private float _elapsed;
        private float _spinTotalIntegral = 1f;
        private RectTransform _rt;
        private Vector2 _basePos;
        private Quaternion _baseRot;
        private Vector3 _baseScale;

        // Per-frame computed animation state, exposed for decorative props
        // (CanvasModeProps) so they stay perfectly in sync with the canvas.
        private Vector2 _swayOffset;
        private float _swayVelocity;
        private float _wobbleAngle;
        private float _wobbleVelocity;
        private float _spinAngle;
        private Vector2 _shakeOffset;
        private float _shakeVelocity;
        private float _zoomScale;
        private float _zoomPhase;

        public MovementMode Mode => _mode;

        /// <summary>Seconds since SetMode.</summary>
        public float Elapsed => _elapsed;
        /// <summary>Total duration of the current mode.</summary>
        public float Duration => _duration;
        /// <summary>Normalized progress 0..1.</summary>
        public float T => Mathf.Clamp01(_elapsed / _duration);
        /// <summary>Resting position captured at SetMode (used as the bounce baseline).</summary>
        public Vector2 BasePosition => _basePos;

        // Per-mode computed state (updated every frame while the mode is active).
        public Vector2 SwayOffset => _swayOffset;
        /// <summary>Horizontal sway velocity in px/s (for audio modulation).</summary>
        public float SwayVelocity => _swayVelocity;
        public float WobbleAngle => _wobbleAngle;
        /// <summary>Wobble angular velocity in deg/s (for audio modulation).</summary>
        public float WobbleVelocity => _wobbleVelocity;
        public float SpinAngle => _spinAngle;
        public Vector2 ShakeOffset => _shakeOffset;
        /// <summary>Shake speed in px/s (for audio modulation).</summary>
        public float ShakeVelocity => _shakeVelocity;
        public float ZoomScale => _zoomScale;
        /// <summary>Zoom oscillation 0..1 (1 = most zoomed out / smallest).</summary>
        public float ZoomPhase => _zoomPhase;

        private void Awake()
        {
            _rt = GetComponent<RectTransform>();
            CaptureBase();
        }

        private void OnEnable()
        {
            CaptureBase();
        }

        private void CaptureBase()
        {
            if (_rt == null)
                return;
            _basePos = _rt.anchoredPosition;
            _baseRot = _rt.localRotation;
            _baseScale = _rt.localScale;
        }

        /// <summary>Start a movement mode for the given duration (seconds).</summary>
        public void SetMode(MovementMode mode, float duration)
        {
            _mode = mode;
            _duration = Mathf.Max(0.1f, duration);
            _elapsed = 0f;
            if (mode == MovementMode.Spin)
            {
                _spinAngle = 0f;
                _spinTotalIntegral = IntegrateCurve(spin.amplitudeCurve, 64);
            }
            // Reset to the centered resting pose BEFORE capturing the base, so a stale
            // offset from a previous round (e.g. leftover sway) can't linger and shift
            // the canvas off-center on the next round.
            if (_rt != null)
            {
                _rt.anchoredPosition = Vector2.zero;
                _rt.localRotation = Quaternion.identity;
                _rt.localScale = Vector3.one;
            }
            CaptureBase();
        }

        /// <summary>Stop moving and restore the base transform.</summary>
        public void Reset()
        {
            _mode = MovementMode.None;
            _swayOffset = Vector2.zero;
            _swayVelocity = 0f;
            _wobbleAngle = 0f;
            _wobbleVelocity = 0f;
            _spinAngle = 0f;
            _shakeOffset = Vector2.zero;
            _shakeVelocity = 0f;
            _zoomScale = 1f;
            _zoomPhase = 0f;
            if (_rt != null)
            {
                _rt.anchoredPosition = _basePos;
                _rt.localRotation = _baseRot;
                _rt.localScale = _baseScale;
            }
        }

        private void Update()
        {
            if (_rt == null || _mode == MovementMode.None)
                return;

            _elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(_elapsed / _duration);

            switch (_mode)
            {
                case MovementMode.Sway:
                    ApplySway(t);
                    break;
                case MovementMode.Wobble:
                    ApplyWobble(t);
                    break;
                case MovementMode.Spin:
                    // Continuous rotation: accumulate the angle each frame so the canvas
                    // keeps spinning for the whole duration and never stops early. The
                    // rate follows the amplitude curve, normalized so the total is
                    // ~baseAmplitude degrees over the intended duration.
                    _spinAngle -= spin.baseAmplitude * spin.amplitudeCurve.Evaluate(t) *
                        (Time.deltaTime / (_duration * _spinTotalIntegral));
                    ApplySpin();
                    break;
                case MovementMode.Shake:
                    ApplyShake(t);
                    break;
                case MovementMode.ZoomOut:
                    ApplyZoomOut(t);
                    break;
            }
        }

        private void ApplySway(float t)
        {
            var amp = sway.baseAmplitude * sway.amplitudeCurve.Evaluate(t);
            var freq = sway.baseFrequency * sway.frequencyCurve.Evaluate(t);
            var prevX = _swayOffset.x;
            var x = Mathf.Sin(_elapsed * freq * Mathf.PI * 2f) * amp;
            _swayOffset = new Vector2(x, 0f);
            // Horizontal velocity (px/s) via finite difference, for audio modulation.
            _swayVelocity = Time.deltaTime > 0f ? (x - prevX) / Time.deltaTime : 0f;
            _rt.anchoredPosition = _basePos + _swayOffset;
        }

        private void ApplyWobble(float t)
        {
            var amp = wobble.baseAmplitude * wobble.amplitudeCurve.Evaluate(t);
            var freq = wobble.baseFrequency * wobble.frequencyCurve.Evaluate(t);
            var prevZ = _wobbleAngle;
            var z = Mathf.Sin(_elapsed * freq * Mathf.PI * 2f) * amp;
            _wobbleAngle = z;
            // Angular velocity (deg/s) via finite difference, for audio modulation.
            _wobbleVelocity = Time.deltaTime > 0f ? (z - prevZ) / Time.deltaTime : 0f;
            _rt.localRotation = _baseRot * Quaternion.Euler(0f, 0f, z);
        }

        private void ApplySpin()
        {
            // Negative Z = clockwise in Unity.
            _rt.localRotation = _baseRot * Quaternion.Euler(0f, 0f, _spinAngle);
        }

        /// <summary>Numeric integral of a curve over [0,1] (trapezoid rule).</summary>
        private static float IntegrateCurve(AnimationCurve curve, int steps)
        {
            if (curve == null || curve.length == 0)
                return 1f;
            var sum = 0f;
            var prev = curve.Evaluate(0f);
            for (var i = 1; i <= steps; i++)
            {
                var x = (float)i / steps;
                var y = curve.Evaluate(x);
                sum += (prev + y) * 0.5f * (1f / steps);
                prev = y;
            }
            return Mathf.Max(0.0001f, sum);
        }

        private void ApplyShake(float t)
        {
            var amp = shake.baseAmplitude * shake.amplitudeCurve.Evaluate(t);
            var freq = shake.baseFrequency * shake.frequencyCurve.Evaluate(t);
            var prev = _shakeOffset;
            var x = (Mathf.PerlinNoise(_elapsed * freq, 0f) - 0.5f) * 2f * amp;
            var y = (Mathf.PerlinNoise(0f, _elapsed * freq) - 0.5f) * 2f * amp;
            _shakeOffset = new Vector2(x, y);
            // Shake speed (px/s) via finite difference, for audio modulation.
            _shakeVelocity = Time.deltaTime > 0f ? (_shakeOffset - prev).magnitude / Time.deltaTime : 0f;
            _rt.anchoredPosition = _basePos + _shakeOffset;
        }

        private void ApplyZoomOut(float t)
        {
            // Zoom back and forth: scale oscillates between 1 and (1 - baseAmplitude).
            // baseFrequency controls the oscillation speed; amplitudeCurve scales the
            // overall zoom amount over the round.
            var osc = (Mathf.Sin(_elapsed * zoom.baseFrequency * Mathf.PI * 2f) + 1f) * 0.5f; // 0..1
            var scale = 1f - zoom.baseAmplitude * osc * zoom.amplitudeCurve.Evaluate(t);
            _zoomScale = scale;
            _zoomPhase = osc;
            _rt.localScale = new Vector3(_baseScale.x * scale, _baseScale.y * scale, _baseScale.z);
        }
    }
}