using Meshup.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace Meshup.Game
{
    public sealed class GameSessionMenu : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Behaviour playerMovement;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Text statusText;

        private UbiqRoomSession session;

        private void Start()
        {
            transform.localScale = Vector3.one;
            resumeButton.onClick.AddListener(Resume);
            leaveButton.onClick.AddListener(LeaveRoom);
            session = UbiqRoomSession.Instance;
            if (session != null)
            {
                session.StateChanged += HandleStateChanged;
                session.ErrorOccurred += HandleError;
            }
            Resume();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)
                && (session == null || session.State == RoomSessionState.InGame))
            {
                if (panelRoot.activeSelf)
                {
                    Resume();
                }
                else
                {
                    Open();
                }
            }
        }

        public void Open()
        {
            panelRoot.SetActive(true);
            if (playerMovement != null)
            {
                playerMovement.enabled = false;
            }
            resumeButton.interactable = true;
            leaveButton.interactable = session != null
                && session.State == RoomSessionState.InGame;
            statusText.text = "";
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Resume()
        {
            panelRoot.SetActive(false);
            if (playerMovement != null)
            {
                playerMovement.enabled = true;
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void LeaveRoom()
        {
            if (session == null || !session.LeaveRoom())
            {
                HandleError("The room session is unavailable.");
                return;
            }

            resumeButton.interactable = false;
            leaveButton.interactable = false;
            statusText.text = "Leaving room…";
        }

        private void HandleStateChanged(RoomSessionState state)
        {
            if (state == RoomSessionState.Leaving)
            {
                panelRoot.SetActive(true);
                resumeButton.interactable = false;
                leaveButton.interactable = false;
                statusText.text = "Leaving room…";
            }
        }

        private void HandleError(string message)
        {
            panelRoot.SetActive(true);
            statusText.text = message;
        }

        private void OnDestroy()
        {
            resumeButton?.onClick.RemoveListener(Resume);
            leaveButton?.onClick.RemoveListener(LeaveRoom);
            if (session != null)
            {
                session.StateChanged -= HandleStateChanged;
                session.ErrorOccurred -= HandleError;
            }
        }
    }
}
