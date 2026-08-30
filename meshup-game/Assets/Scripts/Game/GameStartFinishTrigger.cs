using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class GameStartFinishTrigger : MonoBehaviour
    {
        [SerializeField] private GameStartCoordinator coordinator;
        [SerializeField] private PlayerMovementAuthority localPlayer;

        private bool localControllerInside;

        public bool IsConfigured => coordinator != null && localPlayer != null;

        public void Configure(GameStartCoordinator gameStartCoordinator,
            PlayerMovementAuthority player)
        {
            coordinator = gameStartCoordinator;
            localPlayer = player;
            MakeCollidersNonBlocking();
        }

        private void Awake()
        {
            MakeCollidersNonBlocking();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsLocalCharacterController(other))
            {
                localControllerInside = true;
            }
        }

        private void OnTriggerStay(Collider other)
        {
            // This also covers a player already overlapping the finish volume
            // when a networked walk command becomes active.
            if (IsLocalCharacterController(other))
            {
                localControllerInside = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!localControllerInside || !IsLocalCharacterController(other))
            {
                return;
            }

            // Report on exit, rather than entry, so the final player is fully
            // through the wall and inside the room before group motion stops.
            localControllerInside = false;
            coordinator.NotifyLocalPlayerPassedFinish();
        }

        private bool IsLocalCharacterController(Collider candidate)
        {
            return coordinator != null && localPlayer != null
                && candidate != null
                && candidate == localPlayer.GetComponent<
                    CharacterController>();
        }

        private void MakeCollidersNonBlocking()
        {
            foreach (var item in GetComponents<Collider>())
            {
                item.isTrigger = true;
            }
        }
    }
}
