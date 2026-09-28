using Meshup.Multiplayer;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Meshup.Game
{
    public sealed class GameSessionMenu : MonoBehaviour
    {
        private const float XrCanvasScale = 0.0015f;
        private const float XrCanvasDistance = 1.2f;
        private const float DesktopMaximumWidthFraction = 0.65f;
        private const float DesktopMaximumHeightFraction = 0.62f;

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
        private readonly List<InputDevice> xrControllers = new();
        private bool xrMenuWasPressed;
        private bool xrCanvasConfigured;

        private void Start()
        {
            transform.localScale = Vector3.one;
            BuildMenuControls();
            ConfigureUbiqLayout();
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
            var xrMenuPressed = ReadXrMenuButton();
            var toggleMenu = Input.GetKeyDown(KeyCode.Escape)
                || (xrMenuPressed && !xrMenuWasPressed);
            xrMenuWasPressed = xrMenuPressed;
            if (toggleMenu
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
            ConfigureXrCanvas();
            PositionXrCanvas();
            panelRoot.SetActive(true);
            FitPanelToCanvas();
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

        private bool ReadXrMenuButton()
        {
            xrControllers.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller, xrControllers);
            foreach (var controller in xrControllers)
            {
                if (controller.TryGetFeatureValue(CommonUsages.menuButton,
                        out var pressed) && pressed)
                {
                    return true;
                }
            }
            return false;
        }

        private void ConfigureXrCanvas()
        {
            if (xrCanvasConfigured || !IsXrRunning())
            {
                return;
            }

            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            var rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1024f, 768f);
            rect.localScale = Vector3.one * XrCanvasScale;
            if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            {
                gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            }
            xrCanvasConfigured = true;
        }

        private void PositionXrCanvas()
        {
            if (!xrCanvasConfigured)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var forward = camera.transform.forward;
            transform.position = camera.transform.position
                + forward * XrCanvasDistance;
            // A world-space Canvas renders its front toward local -Z.
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void FitPanelToCanvas()
        {
            var panel = panelRoot.GetComponent<RectTransform>();
            if (panel == null)
            {
                return;
            }
            if (xrCanvasConfigured)
            {
                panel.localScale = Vector3.one;
                return;
            }

            Canvas.ForceUpdateCanvases();
            var canvas = GetComponent<RectTransform>();
            if (canvas == null || canvas.rect.width <= 0f
                || canvas.rect.height <= 0f || panel.rect.width <= 0f
                || panel.rect.height <= 0f)
            {
                return;
            }
            var widthScale = canvas.rect.width
                * DesktopMaximumWidthFraction / panel.rect.width;
            var heightScale = canvas.rect.height
                * DesktopMaximumHeightFraction / panel.rect.height;
            panel.localScale = Vector3.one * Mathf.Min(1f,
                widthScale, heightScale);
        }

        private static bool IsXrRunning()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (display.running)
                {
                    return true;
                }
            }
            return false;
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
                FitPanelToCanvas();
                resumeButton.interactable = false;
                leaveButton.interactable = false;
                statusText.text = "Leaving room…";
            }
        }

        private void HandleError(string message)
        {
            panelRoot.SetActive(true);
            FitPanelToCanvas();
            statusText.text = message;
        }

        private void BuildMenuControls()
        {
            if (panelRoot == null || resumeButton == null || leaveButton == null)
            {
                return;
            }

            var panelRect = panelRoot.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x,
                    Mathf.Max(450f, panelRect.sizeDelta.y));
            }

            var resumeRect = resumeButton.GetComponent<RectTransform>();
            var leaveRect = leaveButton.GetComponent<RectTransform>();
            if (resumeRect != null)
            {
                resumeRect.anchoredPosition = new Vector2(
                    resumeRect.anchoredPosition.x, 90f);
            }
            if (leaveRect != null)
            {
                leaveRect.anchoredPosition = new Vector2(
                    leaveRect.anchoredPosition.x, -90f);
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
                    voiceRect.anchoredPosition.x, -30f);
            }
        }

        private void ConfigureUbiqLayout()
        {
            var panelRect = panelRoot.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRect.sizeDelta = new Vector2(440f, 320f);
            }

            PositionControl(resumeButton, new Vector2(0f, 60f));
            PositionControl(voiceButton, new Vector2(0f, -10f));
            PositionControl(leaveButton, new Vector2(0f, -80f));

            var title = panelRoot.transform.Find("Title")?.GetComponent<Text>();
            if (title != null)
            {
                title.fontSize = 28;
                title.fontStyle = FontStyle.Normal;
                title.color = Color.white;
                title.rectTransform.anchoredPosition = new Vector2(0f, -38f);
                title.rectTransform.sizeDelta = new Vector2(400f, 52f);
            }
            if (statusText != null)
            {
                statusText.fontSize = 18;
                statusText.rectTransform.anchoredPosition = new Vector2(0f, 28f);
            }
        }

        private static void PositionControl(Button button, Vector2 position)
        {
            if (button == null)
            {
                return;
            }
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(320f, 52f);
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(8f, 4f);
                label.rectTransform.offsetMax = new Vector2(-8f, -4f);
                label.fontSize = 20;
                label.alignment = TextAnchor.MiddleCenter;
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
            var desktopShortcut = IsXrRunning() ? string.Empty : " (M)";
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
                voiceButtonLabel.text = "Unmute Voice" + desktopShortcut;
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
                voiceButtonLabel.text = "Mute Voice" + desktopShortcut;
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
