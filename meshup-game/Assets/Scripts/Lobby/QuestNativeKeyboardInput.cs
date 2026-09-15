using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Meshup.Lobby
{
    /// <summary>
    /// Explicitly opens Android's system keyboard when an XR pointer clicks a
    /// legacy InputField. XRUIInputModule does not always give InputField the
    /// focus transition that normally opens TouchScreenKeyboard on Quest.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InputField))]
    public sealed class QuestNativeKeyboardInput : MonoBehaviour,
        IPointerClickHandler
    {
        private InputField input;
        private TouchScreenKeyboard keyboard;
        private Action<string> commit;
        private string textBeforeEditing;
        private Coroutine openRoutine;

        public void Initialize(Action<string> onCommit)
        {
            input = GetComponent<InputField>();
            commit = onCommit;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Application.platform != RuntimePlatform.Android
                || !IsXrRunning() || input == null || !input.interactable)
            {
                return;
            }

            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
            }
            openRoutine = StartCoroutine(OpenAfterInputField());
        }

        private IEnumerator OpenAfterInputField()
        {
            // Let InputField try its normal mobile-keyboard path first. If it
            // succeeded, reuse that keyboard rather than opening another one.
            input.ActivateInputField();
            yield return null;
            openRoutine = null;

            if (input.touchScreenKeyboard != null
                && input.touchScreenKeyboard.status
                    == TouchScreenKeyboard.Status.Visible)
            {
                keyboard = null;
                yield break;
            }

            textBeforeEditing = input.text;
            keyboard = TouchScreenKeyboard.Open(input.text,
                input.keyboardType, false, false,
                input.contentType == InputField.ContentType.Password
                    || input.contentType == InputField.ContentType.Pin,
                false, "Player name", input.characterLimit);
        }

        private void Update()
        {
            if (keyboard == null || input == null)
            {
                return;
            }

            if (input.text != keyboard.text)
            {
                input.text = keyboard.text;
            }

            switch (keyboard.status)
            {
                case TouchScreenKeyboard.Status.Done:
                    commit?.Invoke(input.text);
                    keyboard = null;
                    input.DeactivateInputField();
                    break;
                case TouchScreenKeyboard.Status.Canceled:
                    input.SetTextWithoutNotify(textBeforeEditing);
                    keyboard = null;
                    input.DeactivateInputField();
                    break;
                case TouchScreenKeyboard.Status.LostFocus:
                    commit?.Invoke(input.text);
                    keyboard = null;
                    input.DeactivateInputField();
                    break;
            }
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

        private void OnDisable()
        {
            if (keyboard != null)
            {
                keyboard.active = false;
            }
            keyboard = null;
            if (openRoutine != null)
            {
                StopCoroutine(openRoutine);
                openRoutine = null;
            }
        }
    }
}
