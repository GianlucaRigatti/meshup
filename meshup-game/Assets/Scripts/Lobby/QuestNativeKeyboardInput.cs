using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ubiq.Samples;

namespace Meshup.Lobby
{
    /// <summary>
    /// Connects a legacy InputField to the same configured spatial keyboard
    /// prefab used by Ubiq's sample menu.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InputField))]
    public sealed class QuestNativeKeyboardInput : MonoBehaviour,
        IPointerClickHandler
    {
        private InputField input;
        private GameObject keyboardPrefab;
        private RectTransform keyboardParent;
        private GameObject keyboardOverlay;
        private Keyboard keyboard;
        private Action<string> commit;

        public void Initialize(Action<string> onCommit, GameObject prefab,
            RectTransform parent)
        {
            input = GetComponent<InputField>();
            commit = onCommit;
            keyboardPrefab = prefab;
            keyboardParent = parent;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (input == null || !input.interactable || keyboardPrefab == null)
            {
                return;
            }

            if (keyboardOverlay == null)
            {
                CreateKeyboard();
            }
            else
            {
                keyboardOverlay.SetActive(!keyboardOverlay.activeSelf);
            }
        }

        public void HideKeyboard()
        {
            if (keyboardOverlay != null)
            {
                keyboardOverlay.SetActive(false);
            }
            commit?.Invoke(input.text);
            input.DeactivateInputField();
        }

        private void CreateKeyboard()
        {
            var parent = keyboardParent != null
                ? keyboardParent
                : transform.parent as RectTransform;
            keyboardOverlay = new GameObject("Ubiq Keyboard Overlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            keyboardOverlay.transform.SetParent(parent, false);
            var overlayRect = keyboardOverlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = new Vector2(0.5f, 0.5f);
            overlayRect.anchorMax = new Vector2(0.5f, 0.5f);
            overlayRect.anchoredPosition = new Vector2(0f, -82f);
            overlayRect.sizeDelta = new Vector2(790f, 420f);
            keyboardOverlay.GetComponent<Image>().color =
                new Color(0.08f, 0.08f, 0.08f, 0.96f);

            var instance = Instantiate(keyboardPrefab, overlayRect, false);
            instance.name = "Ubiq Keyboard";
            var keyboardRect = instance.GetComponent<RectTransform>();
            keyboardRect.anchorMin = new Vector2(0.5f, 0.5f);
            keyboardRect.anchorMax = new Vector2(0.5f, 0.5f);
            keyboardRect.anchoredPosition = new Vector2(0f, 25f);
            keyboardRect.sizeDelta = new Vector2(240f, 135f);
            keyboardRect.localScale = Vector3.one * 3f;

            keyboard = instance.GetComponent<Keyboard>();
            keyboard.OnInput.AddListener(HandleKey);

            var doneObject = new GameObject("Done", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            doneObject.transform.SetParent(overlayRect, false);
            var doneRect = doneObject.GetComponent<RectTransform>();
            doneRect.anchorMin = new Vector2(1f, 0f);
            doneRect.anchorMax = new Vector2(1f, 0f);
            doneRect.pivot = new Vector2(1f, 0f);
            doneRect.anchoredPosition = new Vector2(-16f, 14f);
            doneRect.sizeDelta = new Vector2(120f, 44f);
            var doneButton = doneObject.GetComponent<Button>();
            doneButton.onClick.AddListener(HideKeyboard);

            var labelObject = new GameObject("Text", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(doneRect, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;
            var label = labelObject.GetComponent<Text>();
            label.text = "Done";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 22;
            label.alignment = TextAnchor.MiddleCenter;
            Meshup.UbiqUiTheme.ApplyTo(doneButton);
        }

        private void HandleKey(KeyCode keyCode)
        {
            var value = input.text;
            var code = (int)keyCode;
            if (keyCode == KeyCode.Backspace)
            {
                if (value.Length > 0)
                {
                    value = value[..^1];
                }
            }
            else if (code is >= 97 and <= 122)
            {
                if (keyboard.currentKeyCase == Keyboard.KeyCase.Upper)
                {
                    code -= 32;
                }
                value += (char)code;
            }
            else if (code is >= 48 and <= 57 || code == 32)
            {
                value += (char)code;
            }

            if (input.characterLimit > 0 && value.Length > input.characterLimit)
            {
                value = value[..input.characterLimit];
            }
            input.text = value;
        }

        private void OnDisable()
        {
            if (keyboardOverlay != null)
            {
                keyboardOverlay.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (keyboard != null)
            {
                keyboard.OnInput.RemoveListener(HandleKey);
            }
        }
    }
}
