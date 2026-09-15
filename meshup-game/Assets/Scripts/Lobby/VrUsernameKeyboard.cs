using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Meshup.Lobby
{
    /// <summary>
    /// A small world-space keyboard for headsets. Unity's regular InputField
    /// expects a hardware or flat-screen keyboard, which leaves standalone
    /// Quest players unable to enter their display name.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VrUsernameKeyboard : MonoBehaviour
    {
        private const string Keys = "QWERTYUIOPASDFGHJKL\bZXCVBNM _\n";

        private InputField input;
        private GameObject keyboardRoot;
        private Text preview;
        private Action<string> commit;
        private EventTrigger inputTrigger;
        private EventTrigger.Entry clickEntry;

        public bool IsOpen => keyboardRoot != null && keyboardRoot.activeSelf;

        public void Initialize(InputField target, Button buttonTemplate,
            Transform parent, Action<string> onCommit)
        {
            if (input != null || target == null || buttonTemplate == null
                || parent == null)
            {
                return;
            }

            input = target;
            commit = onCommit;
            BuildKeyboard(buttonTemplate, parent);
            inputTrigger = input.GetComponent<EventTrigger>()
                ?? input.gameObject.AddComponent<EventTrigger>();
            clickEntry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick
            };
            clickEntry.callback.AddListener(HandleInputClicked);
            inputTrigger.triggers.Add(clickEntry);
        }

        public void CloseWithoutCommit()
        {
            if (keyboardRoot != null)
            {
                keyboardRoot.SetActive(false);
            }
        }

        private void HandleInputClicked(BaseEventData _)
        {
            if (IsXrRunning())
            {
                Open();
            }
        }

        private void Open()
        {
            if (keyboardRoot == null || input == null || !input.interactable)
            {
                return;
            }

            keyboardRoot.transform.SetAsLastSibling();
            keyboardRoot.SetActive(true);
            RefreshPreview();
        }

        private void Type(char key)
        {
            if (input == null)
            {
                return;
            }

            switch (key)
            {
                case '\b':
                    if (input.text.Length > 0)
                    {
                        input.text = input.text[..^1];
                    }
                    break;
                case '_':
                    input.text = string.Empty;
                    break;
                case '\n':
                    commit?.Invoke(input.text);
                    keyboardRoot.SetActive(false);
                    EventSystem.current?.SetSelectedGameObject(null);
                    break;
                default:
                    if (input.characterLimit <= 0
                        || input.text.Length < input.characterLimit)
                    {
                        input.text += key;
                    }
                    break;
            }

            RefreshPreview();
        }

        private void BuildKeyboard(Button buttonTemplate, Transform parent)
        {
            keyboardRoot = new GameObject("VR Username Keyboard",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rootRect = keyboardRoot.GetComponent<RectTransform>();
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(820f, 340f);
            keyboardRoot.GetComponent<Image>().color =
                new Color(0.015f, 0.035f, 0.06f, 0.98f);

            var previewObject = new GameObject("Entered Name",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var previewRect = previewObject.GetComponent<RectTransform>();
            previewRect.SetParent(rootRect, false);
            previewRect.anchorMin = new Vector2(0.5f, 1f);
            previewRect.anchorMax = new Vector2(0.5f, 1f);
            previewRect.pivot = new Vector2(0.5f, 1f);
            previewRect.anchoredPosition = new Vector2(0f, -12f);
            previewRect.sizeDelta = new Vector2(780f, 46f);
            preview = previewObject.GetComponent<Text>();
            var templateLabel = buttonTemplate.GetComponentInChildren<Text>(true);
            preview.font = templateLabel != null
                ? templateLabel.font
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            preview.fontSize = 30;
            preview.alignment = TextAnchor.MiddleCenter;
            preview.color = Color.white;

            var gridObject = new GameObject("Keys", typeof(RectTransform),
                typeof(GridLayoutGroup));
            var gridRect = gridObject.GetComponent<RectTransform>();
            gridRect.SetParent(rootRect, false);
            gridRect.anchorMin = new Vector2(0.5f, 0f);
            gridRect.anchorMax = new Vector2(0.5f, 0f);
            gridRect.pivot = new Vector2(0.5f, 0f);
            gridRect.anchoredPosition = new Vector2(0f, 14f);
            gridRect.sizeDelta = new Vector2(765f, 264f);
            var grid = gridObject.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(72f, 62f);
            grid.spacing = new Vector2(5f, 4f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 10;
            grid.childAlignment = TextAnchor.UpperCenter;

            foreach (var key in Keys)
            {
                var capturedKey = key;
                var button = Instantiate(buttonTemplate, gridRect, false);
                button.gameObject.name = GetKeyName(key) + " Key";
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => Type(capturedKey));
                var rect = button.GetComponent<RectTransform>();
                rect.localScale = Vector3.one;
                var label = button.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = GetKeyLabel(key);
                    label.fontSize = Mathf.Min(label.fontSize, 24);
                }
                button.gameObject.SetActive(true);
            }

            keyboardRoot.SetActive(false);
        }

        private void RefreshPreview()
        {
            if (preview != null && input != null)
            {
                preview.text = string.IsNullOrEmpty(input.text)
                    ? "Enter player name"
                    : input.text;
            }
        }

        private static string GetKeyLabel(char key) => key switch
        {
            '\b' => "DEL",
            ' ' => "SPACE",
            '_' => "CLEAR",
            '\n' => "DONE",
            _ => key.ToString()
        };

        private static string GetKeyName(char key) => key switch
        {
            '\b' => "Delete",
            ' ' => "Space",
            '_' => "Clear",
            '\n' => "Done",
            _ => key.ToString()
        };

        private static bool IsXrRunning()
        {
            var displays = new System.Collections.Generic.List<XRDisplaySubsystem>();
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

        private void OnDestroy()
        {
            if (inputTrigger != null && clickEntry != null)
            {
                inputTrigger.triggers.Remove(clickEntry);
            }
        }
    }
}
