using Meshup.Multiplayer;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class GameStartInteractable : MonoBehaviour
    {
        [SerializeField] private GameStartCoordinator coordinator;
        [SerializeField] private PlayerMovementAuthority player;
        [SerializeField] private GameObject screenRoot;
        [SerializeField] private Button startButton;
        [SerializeField] private Text screenText;
        [SerializeField] private XRSimpleInteractable screenInteractor;
        [SerializeField] private float keyboardInteractionDistance = 2.5f;

        private UbiqRoomSession session;

        public void Configure(GameStartCoordinator startCoordinator,
            PlayerMovementAuthority localPlayer, GameObject tokenScreen,
            Button tokenButton, Text tokenScreenText,
            XRSimpleInteractable tokenScreenInteractor)
        {
            coordinator = startCoordinator;
            player = localPlayer;
            screenRoot = tokenScreen;
            startButton = tokenButton;
            screenText = tokenScreenText;
            screenInteractor = tokenScreenInteractor;
        }

        private void Start()
        {
            session = UbiqRoomSession.Instance;
            startButton?.onClick.AddListener(TryActivate);
            screenInteractor?.selectEntered.AddListener(OnScreenSelected);
        }

        private void Update()
        {
            if (session == null)
            {
                session = UbiqRoomSession.Instance;
            }

            if (session == null || coordinator == null || player == null)
            {
                SetScreen("CONNECTING…", false);
                return;
            }

            var distance = Vector3.Distance(player.transform.position,
                transform.position);
            if (coordinator.IsRunning)
            {
                SetScreen(coordinator.StatusMessage, false);
                return;
            }
            if (!coordinator.IsIdle)
            {
                SetScreen("STARTING…", false);
                return;
            }

            if (!session.IsRoomCreator)
            {
                SetScreen("WAITING FOR HOST", false);
            }
            else if (session.ParticipantCount < 2)
            {
                SetScreen("WAITING FOR PLAYER", false);
            }
            else
            {
                SetScreen("START GAME", true);
            }

            if (distance <= keyboardInteractionDistance
                && Input.GetKeyDown(KeyCode.E))
            {
                TryActivate();
            }
        }

        public void TryActivate()
        {
            if (startButton != null && startButton.interactable
                && session != null && session.IsRoomCreator)
            {
                coordinator?.TryStartSequence();
            }
        }

        private void OnScreenSelected(SelectEnterEventArgs _)
        {
            TryActivate();
        }

        private void SetScreen(string message, bool interactable)
        {
            if (screenRoot != null && !screenRoot.activeSelf)
            {
                screenRoot.SetActive(true);
            }
            if (startButton != null)
            {
                startButton.interactable = interactable;
            }
            if (screenInteractor != null
                && screenInteractor.enabled != interactable)
            {
                screenInteractor.enabled = interactable;
            }
            if (screenText != null)
            {
                screenText.text = message ?? string.Empty;
            }
        }

        private void OnDestroy()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(TryActivate);
            }
            if (screenInteractor != null)
            {
                screenInteractor.selectEntered.RemoveListener(OnScreenSelected);
            }
        }
    }
}
