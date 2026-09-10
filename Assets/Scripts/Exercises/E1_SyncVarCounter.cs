using PurrNet;
using UnityEngine;

namespace Jam.Exercises
{
    /// <summary>
    /// E1 — SyncVar crash course.
    ///
    /// A SyncVar is a networked variable: when the server writes to it,
    /// every client automatically receives the new value and fires onChanged.
    /// No manual RPCs needed for simple state.
    ///
    /// How to test:
    ///   1. Open Assets/Scenes/MainGame.unity
    ///   2. Create an empty GameObject named "E1 Counter" and add this component.
    ///   3. Press Play in the Editor. The NetworkManager auto-starts host + client
    ///      (StartFlags: server = Editor+ServerBuild, client = Editor+Clone+ClientBuild),
    ///      so the Editor is both server and client. You'll see the counter increment.
    ///   4. Build a standalone client (File > Build Settings > Windows) and run it
    ///      next to the Editor. The client connects to 127.0.0.1:5000 and you'll see
    ///      the same counter values arrive on the client's Console.
    /// </summary>
    public class E1_SyncVarCounter : NetworkBehaviour
    {
        // The networked value. Under the template's default rules only the server
        // may write to it; clients receive updates automatically.
        private readonly SyncVar<int> _counter = new(0);

        private float _timer;

        private void OnEnable()
        {
            _counter.onChanged += OnCounterChanged;
        }

        private void OnDisable()
        {
            _counter.onChanged -= OnCounterChanged;
        }

        protected override void OnSpawned(bool asServer)
        {
            if (asServer)
                Debug.Log($"[E1] Server spawned. counter={_counter.value}");
        }

        private void Update()
        {
            // Only the server drives the value; clients just receive it.
            if (!isServer)
                return;

            _timer += Time.deltaTime;
            if (_timer >= 1f)
            {
                _timer = 0f;
                _counter.value++;
            }
        }

        private void OnCounterChanged(int newValue)
        {
            Debug.Log($"[E1] Counter changed to {newValue} (isServer={isServer}, isOwner={isOwner})");
        }
    }
}