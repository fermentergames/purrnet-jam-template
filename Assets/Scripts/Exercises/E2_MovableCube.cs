using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Jam.Exercises
{
    /// <summary>
    /// E2 — the networked cube. Only the owner can move it; the NetworkTransform
    /// component syncs that movement to everyone else.
    /// </summary>
    public class E2_MovableCube : NetworkBehaviour
    {
        [SerializeField] private float _speed = 5f;

        private void Update()
        {
            if (!isOwner)
                return;

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            var x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            var z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

            transform.Translate(new Vector3(x, 0f, z) * (_speed * Time.deltaTime));
        }
    }
}