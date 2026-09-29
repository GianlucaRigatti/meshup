using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Meshup.Game
{
    /// <summary>Coordinates the terminal and pause menu's shared player input.</summary>
    [DisallowMultipleComponent]
    public sealed class GameInteractionState : MonoBehaviour
    {
        [SerializeField] private PlayerMovementAuthority movementAuthority;
        [SerializeField] private Behaviour desktopInput;
        [SerializeField] private GraphicRaycaster[] desktopOverlays;

        private readonly Dictionary<GraphicRaycaster, bool> overlayStates = new();
        private bool terminalActive;
        private bool pauseMenuOpen;
        private bool desktopInputWasEnabled;
        private CursorLockMode originalCursorLock;
        private bool originalCursorVisible;

        private void OnEnable()
        {
            originalCursorLock = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            ApplyCursorAndOverlays();
        }

        public void SetTerminalActive(bool active)
        {
            terminalActive = active;
            ApplyCursorAndOverlays();
        }

        public void SetPauseMenuOpen(bool open)
        {
            if (pauseMenuOpen != open)
            {
                pauseMenuOpen = open;
                if (open)
                {
                    movementAuthority?.SetLock(MovementLockReason.PauseMenu, true);
                    if (desktopInput != null)
                    {
                        desktopInputWasEnabled = desktopInput.enabled;
                        desktopInput.enabled = false;
                    }
                }
                else
                {
                    if (desktopInput != null) desktopInput.enabled = desktopInputWasEnabled;
                    movementAuthority?.SetLock(MovementLockReason.PauseMenu, false);
                }
            }
            ApplyCursorAndOverlays();
        }

        private void ApplyCursorAndOverlays()
        {
            if (!isActiveAndEnabled || Application.isMobilePlatform) return;
            var cursorFree = terminalActive || pauseMenuOpen;
            Cursor.lockState = cursorFree ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = cursorFree;
            if (terminalActive)
            {
                if (desktopOverlays == null) return;
                foreach (var raycaster in desktopOverlays)
                {
                    if (raycaster == null) continue;
                    if (!overlayStates.ContainsKey(raycaster))
                        overlayStates.Add(raycaster, raycaster.enabled);
                    raycaster.enabled = false;
                }
            }
            else
            {
                RestoreOverlays();
            }
        }

        private void RestoreOverlays()
        {
            foreach (var item in overlayStates)
            {
                if (item.Key != null) item.Key.enabled = item.Value;
            }
            overlayStates.Clear();
        }

        private void OnDisable()
        {
            terminalActive = false;
            SetPauseMenuOpen(false);
            RestoreOverlays();
            if (!Application.isMobilePlatform)
            {
                Cursor.lockState = originalCursorLock;
                Cursor.visible = originalCursorVisible;
            }
        }
    }
}
