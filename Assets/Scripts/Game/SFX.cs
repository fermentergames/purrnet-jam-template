using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Static helper that plays one-shot SFX through a small pool of persistent
    /// AudioSources, nudging the pitch of each play a little so repeated sounds
    /// (clicks, guesses) don't get fatiguing.
    ///
    /// A pool is used rather than one source because AudioSource.pitch is
    /// per-source: changing it on a shared source would also re-pitch whatever is
    /// still ringing out. Round-robin voices keep each shot's pitch to itself.
    /// </summary>
    public static class SFX
    {
        // Enough overlap for rapid clicking; each voice keeps its own pitch.
        private const int VoiceCount = 6;

        private static readonly AudioSource[] _voices = new AudioSource[VoiceCount];
        private static int _nextVoice;
        private static GameSFX _bank;

        public static void SetBank(GameSFX bank) => _bank = bank;

        /// <summary>Next voice in the round-robin, created (and made persistent) on first use.</summary>
        private static AudioSource NextVoice()
        {
            var i = _nextVoice;
            _nextVoice = (_nextVoice + 1) % VoiceCount;

            // Unity's == overload treats destroyed objects as null, so this also
            // rebuilds the voices if a scene reload destroyed them.
            if (_voices[i] == null)
            {
                var go = new GameObject($"SFX Voice {i}");
                Object.DontDestroyOnLoad(go);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _voices[i] = src;
            }
            return _voices[i];
        }

        /// <summary>
        /// Play a one-shot. Pass <paramref name="pitchVariance"/> to override the
        /// bank's variance for this play (0 = perfectly steady pitch).
        /// </summary>
        public static void Play(AudioClip clip, float volume = 1f, float? pitchVariance = null)
        {
            var variance = pitchVariance ?? (_bank != null ? _bank.pitchVariance : 0f);
            PlayPitched(clip, 1f + Random.Range(-variance, variance), volume);
        }

        /// <summary>Play a one-shot at an exact pitch (1 = the clip as recorded).</summary>
        public static void PlayPitched(AudioClip clip, float pitch, float volume = 1f)
        {
            if (clip == null)
                return;

            var voice = NextVoice();
            voice.pitch = pitch;
            voice.PlayOneShot(clip, volume);
        }

        public static void Tap()
        {
            if (_bank == null)
                return;
            Play(_bank.tap, _bank.tapVolume);
        }

        public static void SubmitDrawing()
        {
            if (_bank == null)
                return;
            Play(_bank.submitDrawing, _bank.submitDrawingVolume);
        }

        public static void SubmitGuess()
        {
            if (_bank == null)
                return;
            Play(_bank.submitGuess, _bank.submitGuessVolume);
        }

        public static void CorrectGuess()
        {
            if (_bank == null)
                return;
            Play(_bank.correctGuess, _bank.correctGuessVolume);
        }

        public static void GuessReceived()
        {
            if (_bank == null)
                return;
            Play(_bank.guessReceived, _bank.guessReceivedVolume);
        }

        public static void Undo()
        {
            if (_bank == null)
                return;
            Play(_bank.undo, _bank.undoVolume);
        }

        public static void Paint()
        {
            if (_bank == null)
                return;
            Play(_bank.paint, _bank.paintVolume);
        }

        // Number of cycling drawing-prompt reveal slots (drawingPrompt1..5).
        private const int DrawingPromptSlots = 5;

        // Which slot the next prompt reveal should use.
        private static int _drawingPromptIndex;

        /// <summary>
        /// Play the next drawing-prompt reveal. The slots are cycled in order so repeated
        /// rounds don't sound identical; any unassigned slot is skipped.
        /// </summary>
        public static void DrawingPrompt()
        {
            if (_bank == null)
                return;

            for (var i = 0; i < DrawingPromptSlots; i++)
            {
                var idx = (_drawingPromptIndex + i) % DrawingPromptSlots;
                var clip = DrawingPromptAt(_bank, idx);
                if (clip == null)
                    continue;
                _drawingPromptIndex = (idx + 1) % DrawingPromptSlots;
                Play(clip, _bank.drawingPromptVolume);
                return;
            }
        }

        private static AudioClip DrawingPromptAt(GameSFX bank, int index)
        {
            switch (index)
            {
                case 0: return bank.drawingPrompt1;
                case 1: return bank.drawingPrompt2;
                case 2: return bank.drawingPrompt3;
                case 3: return bank.drawingPrompt4;
                default: return bank.drawingPrompt5;
            }
        }

        public static void Boing()
        {
            if (_bank == null)
                return;
            Play(_bank.boing, _bank.boingVolume);
        }

        public static void Splash()
        {
            if (_bank == null)
                return;
            Play(_bank.splash, _bank.splashVolume);
        }

        /// <summary>
        /// Countdown tick. <paramref name="index"/> is the tick's position in the
        /// countdown (0 = first tick, i.e. "3") and each step raises the pitch, so
        /// the 3-2-1 climbs toward the "go" instead of sitting flat.
        /// </summary>
        public static void Tick(int index)
        {
            if (_bank == null)
                return;
            PlayPitched(_bank.tick, _bank.tickBasePitch + Mathf.Max(0, index) * _bank.tickPitchStep,
                _bank.tickVolume);
        }

        public static void CountdownGo()
        {
            if (_bank == null)
                return;
            Play(_bank.countdownGo, _bank.countdownGoVolume, 0f); // steady pitch
        }

        public static void PhaseChange()
        {
            if (_bank == null)
                return;
            Play(_bank.phaseChange, _bank.phaseChangeVolume);
        }

        public static void RevealCanvas()
        {
            if (_bank == null)
                return;
            Play(_bank.revealCanvas, _bank.revealCanvasVolume);
        }

        public static void RevealPrompt()
        {
            if (_bank == null)
                return;
            Play(_bank.revealPrompt, _bank.revealPromptVolume);
        }

        public static void Winner()
        {
            if (_bank == null)
                return;
            Play(_bank.winner, _bank.winnerVolume);
        }
    }
}