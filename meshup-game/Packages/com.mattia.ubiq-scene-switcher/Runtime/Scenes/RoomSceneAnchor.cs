using UnityEngine;

namespace Ubiq.SceneSwitcher
{
    public enum RoomSceneRole
    {
        Private,
        Multiplayer
    }

    [DisallowMultipleComponent]
    public sealed class RoomSceneAnchor : MonoBehaviour
    {
        [SerializeField] private RoomSceneRole role;
        [SerializeField] private Transform playerSpawn;

        public RoomSceneRole Role => role;
        public Transform PlayerSpawn => playerSpawn != null ? playerSpawn : transform;
    }
}
