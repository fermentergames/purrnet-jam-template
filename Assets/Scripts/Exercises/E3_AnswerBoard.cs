using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Jam.Exercises
{
    /// <summary>
    /// E3 — SyncList + ObserversRpc (the "reveal all answers" mechanic).
    ///
    /// Press 1-4 on any client to submit a canned answer. The server collects
    /// answers into a SyncList&lt;string&gt; (every client sees each one arrive via
    /// onChanged). When 2 answers are in, the server broadcasts an [ObserversRpc]
    /// reveal, and every client logs the full list.
    ///
    /// Setup:
    ///   1. Create an empty "E3 Answer Board" in MainGame, add E3_AnswerBoard.
    ///   2. Play (editor host) + client build. Press 1-4 on each window.
    /// </summary>
    public class E3_AnswerBoard : NetworkBehaviour
    {
        private readonly SyncList<string> _answers = new SyncList<string>();
        private bool _revealed;

        private void OnEnable()
        {
            _answers.onChanged += OnAnswersChanged;
        }

        private void OnDisable()
        {
            _answers.onChanged -= OnAnswersChanged;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame) SubmitAnswerRpc("The best answer");
            if (keyboard.digit2Key.wasPressedThisFrame) SubmitAnswerRpc("A silly answer");
            if (keyboard.digit3Key.wasPressedThisFrame) SubmitAnswerRpc("A clever answer");
            if (keyboard.digit4Key.wasPressedThisFrame) SubmitAnswerRpc("A wrong answer");
        }

        [ServerRpc(requireOwnership: false)]
        private void SubmitAnswerRpc(string answer, RPCInfo info = default)
        {
            _answers.Add($"{info.sender}: {answer}");
            Debug.Log($"[E3] Server collected: {info.sender} -> {answer}");

            if (_answers.Count >= 2 && !_revealed)
            {
                _revealed = true;
                RevealRpc();
            }
        }

        [ObserversRpc]
        private void RevealRpc()
        {
            var all = string.Join(" | ", _answers);
            Debug.Log($"[E3] REVEAL: {all}");
        }

        private void OnAnswersChanged(SyncListChange<string> change)
        {
            Debug.Log($"[E3] List {change.operation} at {change.index}: {change.value} (count={_answers.Count})");
        }
    }
}