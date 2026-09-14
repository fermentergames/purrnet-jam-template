using UnityEngine;

namespace Jam
{
    /// <summary>Static helper that plays one-shot SFX through a persistent AudioSource.</summary>
    public static class SFX
    {
        private static AudioSource _source;
        private static GameSFX _bank;

        public static void SetBank(GameSFX bank) => _bank = bank;

        private static AudioSource Source
        {
            get
            {
                if (_source == null)
                {
                    var go = new GameObject("SFX");
                    Object.DontDestroyOnLoad(go);
                    _source = go.AddComponent<AudioSource>();
                    _source.playOnAwake = false;
                    _source.spatialBlend = 0f;
                }
                return _source;
            }
        }

        public static void Play(AudioClip clip, float volume = 1f)
        {
            if (clip == null)
                return;
            Source.PlayOneShot(clip, volume);
        }

        public static void Tap() => Play(_bank != null ? _bank.tap : null);
        public static void SubmitDrawing() => Play(_bank != null ? _bank.submitDrawing : null);
        public static void SubmitGuess() => Play(_bank != null ? _bank.submitGuess : null);
        public static void CorrectGuess() => Play(_bank != null ? _bank.correctGuess : null);
        public static void Tick() => Play(_bank != null ? _bank.tick : null);
        public static void CountdownGo() => Play(_bank != null ? _bank.countdownGo : null);
        public static void PhaseChange() => Play(_bank != null ? _bank.phaseChange : null);
        public static void RevealCanvas() => Play(_bank != null ? _bank.revealCanvas : null);
        public static void RevealPrompt() => Play(_bank != null ? _bank.revealPrompt : null);
        public static void Winner() => Play(_bank != null ? _bank.winner : null);
    }
}