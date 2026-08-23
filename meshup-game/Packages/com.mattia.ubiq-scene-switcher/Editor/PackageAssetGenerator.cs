using Ubiq.Messaging;
using Ubiq.Rooms;
using Ubiq.SceneSwitcher.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Ubiq.SceneSwitcher.Editor
{
    public static class PackageAssetGenerator
    {
        private const string PackageRoot =
            "Packages/com.mattia.ubiq-scene-switcher";

        [MenuItem("Tools/Ubiq Scene Switcher/Regenerate Package Prefabs")]
        public static void GenerateAll()
        {
            EnsureFolder($"{PackageRoot}/Runtime", "Prefabs");
            GenerateSessionPrefab();
            GenerateBrowserPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Generated Ubiq Room Scene Switcher package prefabs.");
        }

        private static void GenerateSessionPrefab()
        {
            var root = new GameObject("Room Scene Session");
            try
            {
                root.AddComponent<NetworkScene>();
                var client = root.AddComponent<RoomClient>();
                var switcher = root.AddComponent<RoomSceneSwitcher>();
                var rig = new GameObject("Player Rig Root");
                rig.transform.SetParent(root.transform, false);

                var serialized = new SerializedObject(switcher);
                serialized.FindProperty("roomClient").objectReferenceValue = client;
                serialized.FindProperty("playerRigRoot").objectReferenceValue = rig.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root,
                    $"{PackageRoot}/Runtime/Prefabs/Room Scene Session.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void GenerateBrowserPrefab()
        {
            var root = NewUi("Room Browser Canvas", null);
            try
            {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
                root.AddComponent<GraphicRaycaster>();
                var rootRect = (RectTransform)root.transform;
                rootRect.sizeDelta = new Vector2(900f, 600f);
                var presenter = root.AddComponent<RoomBrowserPresenter>();

                var browser = Panel("Private Room Browser", root.transform);
                Fill(browser.GetComponent<RectTransform>());
                var browserLayout = browser.AddComponent<VerticalLayoutGroup>();
                browserLayout.padding = new RectOffset(24, 24, 24, 24);
                browserLayout.spacing = 10f;
                browserLayout.childControlHeight = true;
                browserLayout.childForceExpandHeight = false;

                TextLabel("Rooms", browser.transform, 30, FontStyle.Bold);
                var refresh = Button("Refresh", browser.transform);
                var listRoot = NewUi("Room List", browser.transform).transform;
                listRoot.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 6f;
                listRoot.gameObject.AddComponent<LayoutElement>().preferredHeight = 230f;
                var template = RoomItemTemplate(listRoot);
                var nameInput = Input("Room Name", browser.transform);
                var publish = Toggle("Publish room", browser.transform);
                var create = Button("Create and Enter", browser.transform);
                var codeInput = Input("Join Code", browser.transform);
                var joinCode = Button("Join by Code", browser.transform);
                var status = TextLabel(string.Empty, browser.transform, 20, FontStyle.Normal);

                var current = Panel("Current Room", root.transform);
                Fill(current.GetComponent<RectTransform>());
                var currentLayout = current.AddComponent<VerticalLayoutGroup>();
                currentLayout.padding = new RectOffset(24, 24, 24, 24);
                currentLayout.spacing = 16f;
                var currentName = TextLabel("Room", current.transform, 32, FontStyle.Bold);
                var currentCode = TextLabel("CODE", current.transform, 26, FontStyle.Normal);
                var leave = Button("Leave to Private Room", current.transform);
                current.SetActive(false);

                var busy = Panel("Busy Overlay", root.transform, new Color(0f, 0f, 0f, 0.75f));
                Fill(busy.GetComponent<RectTransform>());
                var busyText = TextLabel("Working…", busy.transform, 30, FontStyle.Bold);
                Center(busyText.rectTransform);
                busy.SetActive(false);

                var errors = Panel("Error Panel", root.transform, new Color(0.35f, 0.04f, 0.04f, 0.96f));
                var errorRect = errors.GetComponent<RectTransform>();
                errorRect.anchorMin = new Vector2(0.15f, 0.35f);
                errorRect.anchorMax = new Vector2(0.85f, 0.65f);
                errorRect.offsetMin = Vector2.zero;
                errorRect.offsetMax = Vector2.zero;
                var errorText = TextLabel("Room operation failed.", errors.transform,
                    24, FontStyle.Normal);
                Center(errorText.rectTransform);
                errors.SetActive(false);

                presenter.Configure(null, browser, listRoot, template, nameInput,
                    publish, codeInput, refresh, create, joinCode, current,
                    currentName, currentCode, leave, busy, status,
                    errors, errorText);

                PrefabUtility.SaveAsPrefabAsset(root,
                    $"{PackageRoot}/Samples~/Room Browser UI/Room Browser Canvas.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static RoomListItemView RoomItemTemplate(Transform parent)
        {
            var item = Panel("Room Item Template", parent,
                new Color(0.12f, 0.12f, 0.12f, 0.9f));
            var layout = item.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 4, 4);
            var name = TextLabel("Room Name", item.transform, 20, FontStyle.Normal);
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var code = TextLabel("CODE", item.transform, 18, FontStyle.Normal);
            var join = Button("Join", item.transform);
            var view = item.AddComponent<RoomListItemView>();
            view.Configure(name, code, join);
            item.AddComponent<LayoutElement>().preferredHeight = 44f;
            item.SetActive(false);
            return view;
        }

        private static GameObject NewUi(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            if (parent != null) gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static GameObject Panel(string name, Transform parent,
            Color? color = null)
        {
            var panel = NewUi(name, parent);
            panel.AddComponent<Image>().color = color ?? new Color(0.05f, 0.05f, 0.05f, 0.92f);
            return panel;
        }

        private static Text TextLabel(string value, Transform parent, int size,
            FontStyle style)
        {
            var gameObject = NewUi(string.IsNullOrEmpty(value) ? "Status" : value, parent);
            var text = gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            gameObject.AddComponent<LayoutElement>().preferredHeight = Mathf.Max(34f, size + 12f);
            return text;
        }

        private static Button Button(string label, Transform parent)
        {
            var gameObject = NewUi(label, parent);
            var image = gameObject.AddComponent<Image>();
            image.color = new Color(0.14f, 0.38f, 0.7f, 1f);
            var button = gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var text = TextLabel(label, gameObject.transform, 20, FontStyle.Normal);
            Fill(text.rectTransform);
            gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            return button;
        }

        private static InputField Input(string placeholderValue, Transform parent)
        {
            var gameObject = NewUi(placeholderValue, parent);
            var image = gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.12f);
            var input = gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            var text = TextLabel(string.Empty, gameObject.transform, 20, FontStyle.Normal);
            text.alignment = TextAnchor.MiddleLeft;
            Fill(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(12f, 0f);
            text.rectTransform.offsetMax = new Vector2(-12f, 0f);
            var placeholder = TextLabel(placeholderValue, gameObject.transform,
                20, FontStyle.Italic);
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            Fill(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(12f, 0f);
            placeholder.rectTransform.offsetMax = new Vector2(-12f, 0f);
            input.textComponent = text;
            input.placeholder = placeholder;
            gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            return input;
        }

        private static Toggle Toggle(string label, Transform parent)
        {
            var gameObject = NewUi(label, parent);
            var background = NewUi("Background", gameObject.transform);
            var bgImage = background.AddComponent<Image>();
            bgImage.color = new Color(1f, 1f, 1f, 0.2f);
            var bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.5f);
            bgRect.anchorMax = new Vector2(0f, 0.5f);
            bgRect.sizeDelta = new Vector2(30f, 30f);
            bgRect.anchoredPosition = new Vector2(18f, 0f);
            var checkmark = NewUi("Checkmark", background.transform);
            var checkImage = checkmark.AddComponent<Image>();
            checkImage.color = new Color(0.2f, 0.7f, 1f, 1f);
            var checkRect = checkmark.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.2f, 0.2f);
            checkRect.anchorMax = new Vector2(0.8f, 0.8f);
            checkRect.offsetMin = checkRect.offsetMax = Vector2.zero;
            var labelText = TextLabel(label, gameObject.transform, 20, FontStyle.Normal);
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.rectTransform.offsetMin = new Vector2(44f, 0f);
            var toggle = gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            toggle.isOn = true;
            gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            return toggle;
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Center(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0.1f, 0.35f);
            rect.anchorMax = new Vector2(0.9f, 0.65f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureFolder(string parent, string name)
        {
            var path = $"{parent}/{name}";
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
