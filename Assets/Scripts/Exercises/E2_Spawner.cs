using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Jam.Exercises
{
    /// <summary>
    /// E2 — ServerRpc + spawning + ownership.
    ///
    /// Press Space on any client: it sends a [ServerRpc] to the server, which
    /// spawns a networked cube and gives ownership to the requester. The owner
    /// can then move it (E2_MovableCube + NetworkTransform), and everyone sees
    /// it move.
    ///
    /// Setup:
    ///   1. Create a Cube (GameObject > 3D Object > Cube).
    ///   2. Add NetworkIdentity + NetworkTransform (Owner Auth is on by default) + E2_MovableCube.
    ///   3. Drag it into Assets/Prefabs to make a prefab, then delete the scene cube.
    ///   4. Verify it appears in Assets/Scenes/NetworkPrefabs.asset (auto-generates).
    ///   5. Create an empty "E2 Spawner" in MainGame, add E2_Spawner, assign the cube prefab.
    /// </summary>
    public class E2_Spawner : NetworkBehaviour
    {
        [SerializeField] private GameObject _cubePrefab;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                SpawnCubeRpc();
        }

        // requireOwnership:false lets ANY client call this on the scene object.
        [ServerRpc(requireOwnership: false)]
        private void SpawnCubeRpc(RPCInfo info = default)
        {
            // Pass the position to Instantiate so it's part of the networked spawn.
            var position = new Vector3(Random.Range(-4f, 4f), 1f, Random.Range(-4f, 4f));
            var cube = Instantiate(_cubePrefab, position, Quaternion.identity);

            if (cube.TryGetComponent<NetworkIdentity>(out var identity))
                identity.GiveOwnership(info.sender);
        }
    }
}