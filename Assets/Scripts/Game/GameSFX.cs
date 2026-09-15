using UnityEngine;

namespace Jam
{
    /// <summary>Central SFX bank. Assign AudioClips per event; unassigned clips are silent.</summary>
    [CreateAssetMenu(menuName = "Jam/Game SFX", fileName = "GameSFX")]
    public class GameSFX : ScriptableObject
    {
        [Header("Global")]
        [Tooltip("Per-play random pitch spread, e.g. 0.06 = ±6%. 0 disables variation.")]
        [Range(0f, 0.3f)] public float pitchVariance = 0.06f;

        // Each clip is paired with its own volume so the mix can be balanced in the
        // Inspector. Note PlayOneShot clamps volume to [0,1], so these attenuate only.

        [Header("Interactions")]
        public AudioClip tap;
        [Range(0f, 1f)] public float tapVolume = 1f;
        public AudioClip submitDrawing;
        [Range(0f, 1f)] public float submitDrawingVolume = 1f;
        public AudioClip submitGuess;
        [Range(0f, 1f)] public float submitGuessVolume = 1f;
        public AudioClip correctGuess;
        [Range(0f, 1f)] public float correctGuessVolume = 1f;
        /// <summary>Played when ANOTHER player's guess arrives (not the local player's own).</summary>
        public AudioClip guessReceived;
        [Range(0f, 1f)] public float guessReceivedVolume = 1f;
        public AudioClip undo;
        [Range(0f, 1f)] public float undoVolume = 1f;

        [Header("Drawing")]
        /// <summary>Played when the player starts a stroke (pointer down on the canvas).</summary>
        public AudioClip paint;
        [Range(0f, 1f)] public float paintVolume = 1f;
        /// <summary>
        /// Drawing-phase prompt reveals. One is picked per round (cycled in order) so
        /// repeated rounds don't sound identical; unassigned slots are skipped.
        /// </summary>
        public AudioClip drawingPrompt1;
        public AudioClip drawingPrompt2;
        public AudioClip drawingPrompt3;
        public AudioClip drawingPrompt4;
        public AudioClip drawingPrompt5;
        [Range(0f, 1f)] public float drawingPromptVolume = 1f;

        [Header("Props")]
        /// <summary>Played when the canvas lands on the trampoline (ZoomOut mode).</summary>
        public AudioClip boing;
        [Range(0f, 1f)] public float boingVolume = 0.4f;

        [Header("Movement Loops")]
        public AudioClip spinLoop;
        [Range(0f, 1f)] public float spinLoopVolume = 1f;
        public AudioClip swayLoop;
        [Tooltip("Sway velocity (px/s) at which the rumble reaches its loudest/highest.")]
        public float swayVelocityForMax = 600f;
        [Range(0f, 1f)] public float swayMinVolume = 0.15f;
        [Range(0f, 1f)] public float swayMaxVolume = 0.8f;
        [Range(0.5f, 2f)] public float swayMinPitch = 0.8f;
        [Range(0.5f, 2f)] public float swayMaxPitch = 1.4f;
        public AudioClip wobbleLoop;
        [Tooltip("Wobble angular velocity (deg/s) at which the water loop reaches its loudest/highest.")]
        public float wobbleVelocityForMax = 120f;
        [Range(0f, 1f)] public float wobbleMinVolume = 0.2f;
        [Range(0f, 1f)] public float wobbleMaxVolume = 0.8f;
        [Range(0.5f, 2f)] public float wobbleMinPitch = 0.9f;
        [Range(0.5f, 2f)] public float wobbleMaxPitch = 1.3f;
        public AudioClip shakeLoop;
        [Tooltip("Shake velocity (px/s) at which the spring loop reaches its loudest/highest.")]
        public float shakeVelocityForMax = 800f;
        [Range(0f, 1f)] public float shakeMinVolume = 0.2f;
        [Range(0f, 1f)] public float shakeMaxVolume = 0.8f;
        [Range(0.5f, 2f)] public float shakeMinPitch = 0.9f;
        [Range(0.5f, 2f)] public float shakeMaxPitch = 1.4f;
        /// <summary>Played when the wobble boat reaches its extreme left/right tilt.</summary>
        public AudioClip splash;
        [Range(0f, 1f)] public float splashVolume = 1f;

        [Header("Countdown / Timer")]
        public AudioClip tick;
        [Range(0f, 1f)] public float tickVolume = 1f;
        public AudioClip countdownGo;
        [Range(0f, 1f)] public float countdownGoVolume = 1f;
        [Tooltip("Pitch of the FIRST tick (the highest countdown number).")]
        public float tickBasePitch = 1f;
        [Tooltip("Pitch added for each tick after the first, so 3-2-1 climbs toward the go.")]
        public float tickPitchStep = 0.12f;

        [Header("Phases")]
        public AudioClip phaseChange;
        [Range(0f, 1f)] public float phaseChangeVolume = 1f;
        public AudioClip revealCanvas;
        [Range(0f, 1f)] public float revealCanvasVolume = 1f;
        public AudioClip revealPrompt;
        [Range(0f, 1f)] public float revealPromptVolume = 1f;
        public AudioClip winner;
        [Range(0f, 1f)] public float winnerVolume = 1f;
    }
}