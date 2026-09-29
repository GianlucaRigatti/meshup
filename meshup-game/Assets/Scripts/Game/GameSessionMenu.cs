using Meshup.Multiplayer;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Meshup.Game
{
    public sealed class GameSessionMenu : MonoBehaviour
    {
        private const float XrCanvasScale = 0.0015f;
        private const float XrCanvasDistance = 1.2f;
        private const float DesktopMaximumWidthFraction = 0.65f;
        private const float DesktopMaximumHeightFraction = 0.62f;

        [SerializeField] private GameObject panelRoot;
        [SerializeField] private GameInteractionState interactionState;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Button voiceButton;
        [SerializeField] private Text voiceButtonLabel;
        [SerializeField] private Text statusText;

        private UbiqRoomSession session;
        private VoiceChatController voiceChat;
        private readonly List<InputDevice> xrControllers = new();
        private bool xrMenuWasPressed;
        private bool xrCanvasConfigured;

        private void Start()
        {
            transform.localScale = Vector3.one;
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
            interactionState.SetPauseMenuOpen(true);
            resumeButton.interactable = true;
            leaveButton.interactable = session != null
                && session.State == RoomSessionState.InGame;
            statusText.text = "";
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
            interactionState.SetPauseMenuOpen(false);
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
                Open();
                resumeButton.interactable = false;
                leaveButton.interactable = false;
                statusText.text = "Leaving room…";
            }
        }

        private void HandleError(string message)
        {
            Open();
            statusText.text = message;
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

        private void OnDisable()
        {
            if (interactionState != null) interactionState.SetPauseMenuOpen(false);
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
