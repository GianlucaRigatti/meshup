using UnityEngine;

namespace Meshup.Lobby
{
    [DisallowMultipleComponent]
    public sealed class RoomTotemInteraction : MonoBehaviour
    {
        [SerializeField] private GameObject interactionPrompt;
        [SerializeField] private RoomTotemPanel panel;

        private LobbyFirstPersonController playerInRange;
        private bool interactionAvailable = true;

        public bool InteractionAvailable => interactionAvailable;

        private void Awake()
        {
            interactionPrompt.SetActive(false);
        }

        private void Update()
        {
            var canInteract = interactionAvailable && playerInRange != null && !panel.IsOpen;
            interactionPrompt.SetActive(canInteract);
            if (canInteract && Input.GetKeyDown(KeyCode.E))
            {
                panel.Open(playerInRange);
                interactionPrompt.SetActive(false);
            }
        }

        public void SetInteractionAvailable(bool available)
        {
            interactionAvailable = available;
            if (!available && interactionPrompt != null)
            {
                interactionPrompt.SetActive(false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<LobbyFirstPersonController>();
            if (player != null)
            {
                playerInRange = player;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var player = other.GetComponentInParent<LobbyFirstPersonController>();
            if (player != null && player == playerInRange)
            {
                playerInRange = null;
                interactionPrompt.SetActive(false);
            }
        }
    }
}
