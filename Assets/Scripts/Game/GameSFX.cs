using UnityEngine;

namespace Jam
{
    /// <summary>Central SFX bank. Assign AudioClips per event; unassigned clips are silent.</summary>
    [CreateAssetMenu(menuName = "Jam/Game SFX", fileName = "GameSFX")]
    public class GameSFX : ScriptableObject
    {
        [Header("Interactions")]
        public AudioClip tap;
        public AudioClip submitDrawing;
        public AudioClip submitGuess;
        public AudioClip correctGuess;

        [Header("Countdown / Timer")]
        public AudioClip tick;
        public AudioClip countdownGo;

        [Header("Phases")]
        public AudioClip phaseChange;
        public AudioClip revealCanvas;
        public AudioClip revealPrompt;
        public AudioClip winner;
    }
}