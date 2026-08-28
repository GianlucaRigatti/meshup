using Meshup.Multiplayer;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class GameStartInteractable : MonoBehaviour
    {
        [SerializeField] private GameStartCoordinator coordinator;
        [SerializeField] private PlayerMovementAuthority player;
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private Text promptText;
        [SerializeField] private float keyboardInteractionDistance = 2.5f;
        [SerializeField] private float promptDistance = 5f;

        private UbiqRoomSession session;
        private XRSimpleInteractable xrInteractable;

        public void Configure(GameStartCoordinator startCoordinator,
            PlayerMovementAuthority localPlayer, GameObject worldPrompt,
            Text worldPromptText)
        {
            coordinator = startCoordinator;
            player = localPlayer;
            promptRoot = worldPrompt;
            promptText = worldPromptText;
        }

        private void Awake()
        {
            xrInteractable = GetComponent<XRSimpleInteractable>();
        }

        private void Start()
        {
            session = UbiqRoomSession.Instance;
            xrInteractable.selectEntered.AddListener(HandleSelectEntered);
        }

        private void Update()
        {
            if (session == null || coordinator == null || player == null)
            {
                SetPrompt(false, string.Empty);
                return;
            }

            var distance = Vector3.Distance(player.transform.position,
                transform.position);
            var closeEnoughForPrompt = distance <= promptDistance;
            if (coordinator.IsRunning)
            {
                SetPrompt(closeEnoughForPrompt, coordinator.StatusMessage);
                return;
            }
            if (!coordinator.IsIdle)
            {
                SetPrompt(false, string.Empty);
                return;
            }

            string message;
            if (!session.IsRoomCreator)
            {
                message = "Waiting for room creator.";
            }
            else if (session.ParticipantCount < 2)
            {
                message = "Waiting for another player.";
            }
            else
            {
                message = "Press E / Select to start.";
            }
            SetPrompt(closeEnoughForPrompt, message);

            if (distance <= keyboardInteractionDistance
                && Input.GetKeyDown(KeyCode.E))
            {
                TryActivate();
            }
        }

        public void TryActivate()
        {
            if (session != null && session.IsRoomCreator)
            {
                coordinator?.TryStartSequence();
            }
        }

        private void HandleSelectEntered(SelectEnterEventArgs args)
        {
            TryActivate();
        }

        private void SetPrompt(bool visible, string message)
        {
            if (promptRoot != null)
            {
                promptRoot.SetActive(visible);
                if (visible && Camera.main != null)
                {
                    var direction = promptRoot.transform.position
                        - Camera.main.transform.position;
                    if (direction.sqrMagnitude > 0.001f)
                    {
                        promptRoot.transform.rotation = Quaternion.LookRotation(
                            direction, Vector3.up);
                    }
                }
            }
            if (promptText != null)
            {
                promptText.text = message ?? string.Empty;
            }
        }

        private void OnDestroy()
        {
            if (xrInteractable != null)
            {
                xrInteractable.selectEntered.RemoveListener(HandleSelectEntered);
            }
        }
    }
}
