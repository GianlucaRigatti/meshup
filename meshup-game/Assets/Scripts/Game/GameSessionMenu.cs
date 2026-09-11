using Meshup.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace Meshup.Game
{
    public sealed class GameSessionMenu : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Behaviour playerMovement;
        [SerializeField] private PlayerMovementAuthority movementAuthority;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Text statusText;

        private UbiqRoomSession session;
        private VoiceChatController voiceChat;
        private Button voiceButton;
        private Text voiceButtonLabel;

        private void Start()
        {
            transform.localScale = Vector3.one;
            BuildVoiceControls();
            resumeButton.onClick.AddListener(Resume);
            leaveButton.onClick.AddListener(LeaveRoom);
            voiceButton?.onClick.AddListener(ToggleVoiceMute);
            session = UbiqRoomSession.Instance;
            voiceChat = VoiceChatController.Instance;
            if (session != null)
            {
                session.StateChanged += HandleStateChanged;
                session.ErrorOccurred += HandleError;
            }
            if (voiceChat != null)
            {
                voiceChat.StateChanged += UpdateVoiceControls;
            }
            UpdateVoiceControls();
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
            if (Input.GetKeyDown(KeyCode.M))
            {
                ToggleVoiceMute();
            }
        }

        public void Open()
        {
            panelRoot.SetActive(true);
            movementAuthority?.SetLock(MovementLockReason.PauseMenu, true);
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
            movementAuthority?.SetLock(MovementLockReason.PauseMenu, false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void SetMovementAuthority(PlayerMovementAuthority authority)
        {
            movementAuthority = authority;
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

        private void BuildVoiceControls()
        {
            if (panelRoot == null || resumeButton == null || leaveButton == null)
            {
                return;
            }

            var panelRect = panelRoot.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x,
                    Mathf.Max(380f, panelRect.sizeDelta.y));
            }

            var resumeRect = resumeButton.GetComponent<RectTransform>();
            var leaveRect = leaveButton.GetComponent<RectTransform>();
            if (resumeRect != null)
            {
                resumeRect.anchoredPosition = new Vector2(
                    resumeRect.anchoredPosition.x, 60f);
            }
            if (leaveRect != null)
            {
                leaveRect.anchoredPosition = new Vector2(
                    leaveRect.anchoredPosition.x, -60f);
            }

            var voiceObject = Instantiate(resumeButton.gameObject,
                resumeButton.transform.parent, false);
            voiceObject.name = "Voice Mute Button";
            voiceButton = voiceObject.GetComponent<Button>();
            voiceButtonLabel = voiceObject.GetComponentInChildren<Text>(true);
            var voiceRect = voiceObject.GetComponent<RectTransform>();
            if (voiceRect != null)
            {
                voiceRect.anchoredPosition = new Vector2(
                    voiceRect.anchoredPosition.x, 0f);
            }
        }

        private void ToggleVoiceMute()
        {
            voiceChat ??= VoiceChatController.Instance;
            voiceChat?.ToggleManualMute();
            UpdateVoiceControls();
        }

        private void UpdateVoiceControls()
        {
            if (voiceButton == null || voiceButtonLabel == null)
            {
                return;
            }

            voiceChat ??= VoiceChatController.Instance;
            if (voiceChat == null)
            {
                voiceButton.interactable = false;
                voiceButtonLabel.text = "Voice unavailable";
                return;
            }

            voiceButton.interactable = !voiceChat.IsCapturing;
            if ((voiceChat.MuteReasons & VoiceMuteReason.GuessRecording) != 0)
            {
                voiceButtonLabel.text = "Muted while recording guess";
            }
            else if ((voiceChat.MuteReasons
                    & VoiceMuteReason.ObjectDescription) != 0)
            {
                voiceButtonLabel.text = "Muted while describing object";
            }
            else if (voiceChat.IsManuallyMuted)
            {
                voiceButtonLabel.text = "Unmute Voice (M)";
            }
            else if (!voiceChat.IsAvailable)
            {
                voiceButton.interactable = false;
                voiceButtonLabel.text = "Microphone unavailable";
            }
            else if (!voiceChat.IsReady)
            {
                voiceButtonLabel.text = "Voice starting…";
            }
            else
            {
                voiceButtonLabel.text = "Mute Voice (M)";
            }
        }

        private void OnDestroy()
        {
            resumeButton?.onClick.RemoveListener(Resume);
            leaveButton?.onClick.RemoveListener(LeaveRoom);
            voiceButton?.onClick.RemoveListener(ToggleVoiceMute);
            if (session != null)
            {
                session.StateChanged -= HandleStateChanged;
                session.ErrorOccurred -= HandleError;
            }
            if (voiceChat != null)
            {
                voiceChat.StateChanged -= UpdateVoiceControls;
            }
        }
    }
}
