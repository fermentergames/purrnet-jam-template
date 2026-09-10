using PurrNet;
using UnityEngine;
using UnityEngine.UI;

namespace Jam.Exercises
{
    /// <summary>
    /// E4 — SyncTimer + phase SyncVar (the game-flow state machine).
    ///
    /// The server drives a simple phase machine: Waiting -> Prompt -> Answering
    /// (10s countdown) -> Reveal -> back to Prompt. Every client sees the phase
    /// change and the countdown tick, all driven by the server.
    ///
    /// Setup:
    ///   1. Create an empty "E4 Game Flow" in MainGame, add E4_GameFlow.
    ///   2. Play (editor host) + client build. Watch the on-screen overlay.
    /// </summary>
    public class E4_GameFlow : NetworkBehaviour
    {
        public enum Phase { Waiting, Prompt, Answering, Reveal }

        private readonly SyncVar<Phase> _phase = new SyncVar<Phase>(Phase.Waiting);
        private readonly SyncTimer _timer = new SyncTimer();

        private Text _overlay;

        private void Awake()
        {
            _overlay = ExerciseUI.CreateOverlay("E4");
        }

        private void OnEnable()
        {
            _phase.onChanged += OnPhaseChanged;
            _timer.onTimerEnd += OnTimerEnd;
            _timer.onTimerSecondTick += OnSecondTick;
        }

        private void OnDisable()
        {
            _phase.onChanged -= OnPhaseChanged;
            _timer.onTimerEnd -= OnTimerEnd;
            _timer.onTimerSecondTick -= OnSecondTick;
        }

        protected override void OnSpawned(bool asServer)
        {
            if (!asServer)
                return;

            // Server starts the flow once spawned.
            _phase.value = Phase.Prompt;
        }

        private void OnPhaseChanged(Phase newPhase)
        {
            Debug.Log($"[E4] Phase -> {newPhase} (isServer={isServer})");
            RefreshOverlay();

            // Only the server starts timers; clients just render the phase.
            if (!isServer)
                return;

            switch (newPhase)
            {
                case Phase.Prompt:
                    // Give players a moment to read the prompt, then start answering.
                    _timer.StartTimer(3f);
                    break;
                case Phase.Answering:
                    _timer.StartTimer(10f);
                    break;
                case Phase.Reveal:
                    _timer.StartTimer(5f);
                    break;
            }
        }

        private void OnTimerEnd()
        {
            if (!isServer)
                return;

            Debug.Log("[E4] Timer ended");

            switch (_phase.value)
            {
                case Phase.Prompt:
                    _phase.value = Phase.Answering;
                    break;
                case Phase.Answering:
                    _phase.value = Phase.Reveal;
                    break;
                case Phase.Reveal:
                    _phase.value = Phase.Prompt; // loop back
                    break;
            }
        }

        private void OnSecondTick()
        {
            Debug.Log($"[E4] {_phase.value} countdown: {_timer.remainingInt}s");
            RefreshOverlay();
        }

        private void RefreshOverlay()
        {
            if (_overlay == null)
                return;

            var role = isServer ? "SERVER" : "CLIENT";
            _overlay.text = $"E4  [{role}]\nPhase: {_phase.value}\nTime: {_timer.remainingInt}s";
        }
    }
}