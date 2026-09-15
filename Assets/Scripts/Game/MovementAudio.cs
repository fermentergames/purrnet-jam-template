using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Drives the per-movement-mode ambience with a single looping AudioSource:
    ///   Spin   -> a looping engine (plane_engine_5)
    ///   Sway   -> a looping rumble whose gain + pitch follow the sway velocity
    ///   ZoomOut-> a "boing" on each trampoline landing (one-shot via SFX)
    /// All movement audio lives here so it can be gated together by one `Active`
    /// flag (off during the intro and after submit).
    ///
    /// Attach to the same GameObject as the CanvasMover. Call Init() once, then set
    /// Active = true once the canvas is drawable (intro over).
    /// </summary>
    public class MovementAudio : MonoBehaviour
    {
        private CanvasMover _mover;
        private GameSFX _bank;
        private AudioSource _loop;
        private AudioClip _currentLoop;
        private bool _boingPlayed;
        private int _wobbleVelSign;
        private bool _wasActive;

        /// <summary>When false, no movement audio plays (intro / not drawing).</summary>
        public bool Active;

        public void Init(CanvasMover mover, GameSFX bank)
        {
            _mover = mover;
            _bank = bank;
            _loop = gameObject.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.loop = true;
            _loop.spatialBlend = 0f;
        }

        private void Update()
        {
            if (_mover == null || _bank == null)
                return;

            // Reset the boing latch whenever we (re)activate, so a landing that was
            // mid-cycle during the intro doesn't fire the moment the intro ends.
            if (Active != _wasActive)
            {
                _wasActive = Active;
                if (Active)
                {
                    _boingPlayed = false;
                    _wobbleVelSign = 0;
                }
            }

            if (!Active)
            {
                StopLoop();
                return;
            }

            switch (_mover.Mode)
            {
                case MovementMode.Spin:
                    PlayLoop(_bank.spinLoop, _bank.spinLoopVolume, 1f);
                    break;
                case MovementMode.Sway:
                    PlayLoop(_bank.swayLoop, SwayVolume(), SwayPitch());
                    break;
                case MovementMode.Wobble:
                    PlayLoop(_bank.wobbleLoop, WobbleVolume(), WobblePitch());
                    UpdateSplash();
                    break;
                case MovementMode.Shake:
                    PlayLoop(_bank.shakeLoop, ShakeVolume(), ShakePitch());
                    break;
                case MovementMode.ZoomOut:
                    StopLoop();
                    UpdateBoing();
                    break;
                default:
                    StopLoop();
                    break;
            }
        }

        private void PlayLoop(AudioClip clip, float volume, float pitch)
        {
            if (clip == null)
            {
                StopLoop();
                return;
            }
            if (_currentLoop != clip)
            {
                _currentLoop = clip;
                _loop.clip = clip;
                _loop.Play();
            }
            _loop.volume = Mathf.Clamp01(volume);
            _loop.pitch = pitch;
        }

        private void StopLoop()
        {
            if (_currentLoop == null)
                return;
            _currentLoop = null;
            _loop.Stop();
        }

        /// <summary>Rumble gain follows sway speed: quiet at rest, loud at peak velocity.</summary>
        private float SwayVolume()
        {
            var t = SwayT();
            return Mathf.Lerp(_bank.swayMinVolume, _bank.swayMaxVolume, t);
        }

        /// <summary>Rumble pitch follows sway speed: low at rest, high at peak velocity.</summary>
        private float SwayPitch()
        {
            var t = SwayT();
            return Mathf.Lerp(_bank.swayMinPitch, _bank.swayMaxPitch, t);
        }

        /// <summary>Normalized sway speed 0..1 (|velocity| / velocity-for-max).</summary>
        private float SwayT()
        {
            var v = Mathf.Abs(_mover.SwayVelocity);
            return Mathf.Clamp01(v / Mathf.Max(0.001f, _bank.swayVelocityForMax));
        }

        /// <summary>Water loop gain follows wobble speed: quiet at rest, loud at peak tilt.</summary>
        private float WobbleVolume()
        {
            var t = WobbleT();
            return Mathf.Lerp(_bank.wobbleMinVolume, _bank.wobbleMaxVolume, t);
        }

        /// <summary>Water loop pitch follows wobble speed: low at rest, high at peak tilt.</summary>
        private float WobblePitch()
        {
            var t = WobbleT();
            return Mathf.Lerp(_bank.wobbleMinPitch, _bank.wobbleMaxPitch, t);
        }

        /// <summary>Normalized wobble speed 0..1 (|angular velocity| / velocity-for-max).</summary>
        private float WobbleT()
        {
            var v = Mathf.Abs(_mover.WobbleVelocity);
            return Mathf.Clamp01(v / Mathf.Max(0.001f, _bank.wobbleVelocityForMax));
        }

        /// <summary>Spring loop gain follows shake speed: quiet at rest, loud at peak.</summary>
        private float ShakeVolume()
        {
            var t = ShakeT();
            return Mathf.Lerp(_bank.shakeMinVolume, _bank.shakeMaxVolume, t);
        }

        /// <summary>Spring loop pitch follows shake speed: low at rest, high at peak.</summary>
        private float ShakePitch()
        {
            var t = ShakeT();
            return Mathf.Lerp(_bank.shakeMinPitch, _bank.shakeMaxPitch, t);
        }

        /// <summary>Normalized shake speed 0..1 (|velocity| / velocity-for-max).</summary>
        private float ShakeT()
        {
            var v = Mathf.Abs(_mover.ShakeVelocity);
            return Mathf.Clamp01(v / Mathf.Max(0.001f, _bank.shakeVelocityForMax));
        }

        private void UpdateBoing()
        {
            var osc = _mover.ZoomPhase; // 0..1, 1 = canvas at its lowest point
            if (osc >= 0.98f && !_boingPlayed)
            {
                _boingPlayed = true;
                SFX.Boing();
            }
            else if (osc < 0.9f)
            {
                _boingPlayed = false;
            }
        }

        /// <summary>Water splash when the wobble boat reaches an extreme tilt (left or right).</summary>
        private void UpdateSplash()
        {
            var v = _mover.WobbleVelocity;
            var sign = v > 0f ? 1 : (v < 0f ? -1 : 0);
            // A sign change in angular velocity means the boat hit an extreme edge.
            if (_wobbleVelSign != 0 && sign != 0 && sign != _wobbleVelSign)
                SFX.Splash();
            if (sign != 0)
                _wobbleVelSign = sign;
        }
    }
}