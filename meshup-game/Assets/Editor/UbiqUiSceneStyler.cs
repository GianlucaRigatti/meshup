using System.Collections.Generic;
using Meshup.Game;
using Meshup.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meshup.Editor
{
    public static class UbiqUiSceneStyler
    {
        private const string SampleScene = "Assets/Scenes/SampleScene.unity";
        private const string GameScene = "Assets/Scenes/GameScene.unity";
        private const string KeyboardPrefab =
            "Packages/com.ucl.ubiq/Runtime/UI/Keyboard.prefab";

        [MenuItem("Tools/MeshUp/Apply Ubiq UI Style To Scenes")]
        public static void ApplyAllScenes()
        {
            var original = SceneManager.GetActiveScene().path;
            StyleScene(SampleScene);
            StyleScene(GameScene);
            if (!string.IsNullOrEmpty(original))
            {
                EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
            }
        }

        public static void ApplyAllScenesFromCommandLine()
        {
            StyleScene(SampleScene);
            StyleScene(GameScene);
        }

        private static void StyleScene(string path)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var sampleSprite = LoadSampleSprite();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var panel in root.GetComponentsInChildren<
                    RoomTotemPanel>(true))
                {
                    var serialized = new SerializedObject(panel);
                    var panelRoot = serialized.FindProperty("panelRoot")
                        .objectReferenceValue as GameObject;
                    StyleMenu(panelRoot, sampleSprite);
                }

                foreach (var menu in root.GetComponentsInChildren<
                    GameSessionMenu>(true))
                {
                    var serialized = new SerializedObject(menu);
                    var panelRoot = serialized.FindProperty("panelRoot")
                        .objectReferenceValue as GameObject;
                    StyleMenu(panelRoot, sampleSprite);
                    StylePauseLayout(panelRoot, serialized);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static Sprite LoadSampleSprite()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeyboardPrefab);
            return prefab != null
                ? prefab.GetComponentInChildren<Button>(true)?.targetGraphic
                    is Image image ? image.sprite : null
                : null;
        }

        private static void StyleMenu(GameObject root, Sprite sprite)
        {
            if (root == null)
            {
                return;
            }

            var interactive = new HashSet<Graphic>();
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                StyleButton(button, sprite);
                if (button.targetGraphic != null)
                {
                    interactive.Add(button.targetGraphic);
                }
            }
            foreach (var input in root.GetComponentsInChildren<InputField>(true))
            {
                if (input.targetGraphic is Image image)
                {
                    StyleImage(image, UbiqUiTheme.ControlSurface, sprite);
                    interactive.Add(image);
                }
                input.colors = SampleColors();
            }

            var rootImage = root.GetComponent<Image>();
            if (rootImage != null)
            {
                StyleImage(rootImage, UbiqUiTheme.MenuSurface, sprite);
            }
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image != rootImage && !interactive.Contains(image)
                    && image.sprite == null)
                {
                    StyleImage(image, UbiqUiTheme.SectionSurface, sprite);
                }
            }
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                text.color = Color.white;
                text.fontStyle = FontStyle.Normal;
            }
        }

        private static void StyleButton(Button button, Sprite sprite)
        {
            if (button.targetGraphic is Image image)
            {
                StyleImage(image, UbiqUiTheme.ControlSurface, sprite);
            }
            button.transition = Selectable.Transition.ColorTint;
            button.colors = SampleColors();
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            {
                text.color = Color.white;
                text.fontStyle = FontStyle.Normal;
            }
        }

        private static void StyleImage(Image image, Color color, Sprite sprite)
        {
            image.color = color;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
        }

        private static void StylePauseLayout(GameObject root,
            SerializedObject menu)
        {
            if (root == null)
            {
                return;
            }
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(440f, 380f);
            Position(menu.FindProperty("resumeButton").objectReferenceValue
                as Button, new Vector2(0f, 70f));
            Position(menu.FindProperty("leaveButton").objectReferenceValue
                as Button, new Vector2(0f, -110f));

            var title = root.transform.Find("Title")?.GetComponent<Text>();
            if (title != null)
            {
                title.fontSize = 28;
                title.color = Color.white;
                title.rectTransform.anchoredPosition = new Vector2(0f, -38f);
                title.rectTransform.sizeDelta = new Vector2(400f, 52f);
            }
        }

        private static void Position(Button button, Vector2 position)
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

        private static ColorBlock SampleColors()
        {
            return new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.9607843f, 0.9607843f,
                    0.9607843f, 1f),
                pressedColor = new Color(0.78431374f, 0.78431374f,
                    0.78431374f, 1f),
                selectedColor = new Color(0.9607843f, 0.9607843f,
                    0.9607843f, 1f),
                disabledColor = new Color(0.78431374f, 0.78431374f,
                    0.78431374f, 0.5019608f),
                colorMultiplier = 1f,
                fadeDuration = 0.1f
            };
        }
    }
}
