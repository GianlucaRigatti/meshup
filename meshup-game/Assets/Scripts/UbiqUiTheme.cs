using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Meshup
{
    /// <summary>
    /// Applies the visual settings used by Ubiq's sample Menu prefab to
    /// MeshUp's scene-authored and runtime-created UI.
    /// </summary>
    public static class UbiqUiTheme
    {
        public static readonly Color MenuSurface =
            new(0.035f, 0.04f, 0.045f, 0.9f);
        public static readonly Color ControlSurface =
            new(1f, 1f, 1f, 0.22f);
        public static readonly Color SectionSurface =
            new(1f, 1f, 1f, 0.1f);

        private static Sprite uiSprite;

        public static void ApplyTo(GameObject root, bool styleRootAsPanel = false)
        {
            if (root == null)
            {
                return;
            }

            Image panel = null;
            if (styleRootAsPanel && root.TryGetComponent(out panel))
            {
                ConfigureSurface(panel, MenuSurface);
            }

            var buttons = root.GetComponentsInChildren<Button>(true);
            var inputs = root.GetComponentsInChildren<InputField>(true);
            var interactiveGraphics = new HashSet<Graphic>();
            foreach (var button in buttons)
            {
                ApplyTo(button);
                if (button.targetGraphic != null)
                {
                    interactiveGraphics.Add(button.targetGraphic);
                }
            }

            foreach (var input in inputs)
            {
                ApplyTo(input);
                if (input.targetGraphic != null)
                {
                    interactiveGraphics.Add(input.targetGraphic);
                }
            }

            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == panel || interactiveGraphics.Contains(image)
                    || image.sprite != null)
                {
                    continue;
                }
                ConfigureSurface(image, SectionSurface);
            }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                text.font = font;
                text.fontStyle = FontStyle.Normal;
                text.color = Color.white;
                text.lineSpacing = 1f;
            }
        }

        public static void ApplyIconTile(Button button, string symbol)
        {
            if (button == null)
            {
                return;
            }

            ApplyTo(button);
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                var labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0.05f, 0f);
                labelRect.anchorMax = new Vector2(0.95f, 0.36f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                label.fontSize = 16;
                label.fontStyle = FontStyle.Normal;
                label.alignment = TextAnchor.MiddleCenter;
            }

            var iconTransform = button.transform.Find("Ubiq Icon");
            Text icon;
            if (iconTransform == null)
            {
                var iconObject = new GameObject("Ubiq Icon",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                iconObject.transform.SetParent(button.transform, false);
                icon = iconObject.GetComponent<Text>();
            }
            else
            {
                icon = iconTransform.GetComponent<Text>();
            }

            var rect = icon.rectTransform;
            rect.anchorMin = new Vector2(0.08f, 0.35f);
            rect.anchorMax = new Vector2(0.92f, 0.95f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            icon.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            icon.fontSize = 30;
            icon.fontStyle = FontStyle.Normal;
            icon.alignment = TextAnchor.MiddleCenter;
            icon.color = Color.white;
            icon.text = symbol;
        }

        public static void ApplyIconButton(Button button, string symbol)
        {
            if (button == null)
            {
                return;
            }

            ApplyTo(button);
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                var labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0.28f, 0f);
                labelRect.anchorMax = new Vector2(0.96f, 1f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                label.fontStyle = FontStyle.Normal;
                label.alignment = TextAnchor.MiddleCenter;
            }

            var iconTransform = button.transform.Find("Ubiq Icon");
            Text icon;
            if (iconTransform == null)
            {
                var iconObject = new GameObject("Ubiq Icon",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                iconObject.transform.SetParent(button.transform, false);
                icon = iconObject.GetComponent<Text>();
            }
            else
            {
                icon = iconTransform.GetComponent<Text>();
            }

            var rect = icon.rectTransform;
            rect.anchorMin = new Vector2(0.04f, 0.08f);
            rect.anchorMax = new Vector2(0.28f, 0.92f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            icon.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            icon.fontSize = 22;
            icon.alignment = TextAnchor.MiddleCenter;
            icon.color = Color.white;
            icon.text = symbol;
        }

        public static void ApplyTo(Button button)
        {
            if (button == null)
            {
                return;
            }

            // The spatial keyboard is the source asset for this theme. Keep
            // its authored key visuals intact instead of restyling them.
            if (button.GetComponentInParent<Ubiq.Samples.Keyboard>(true)
                != null)
            {
                return;
            }

            if (button.targetGraphic is Image image)
            {
                ConfigureControlImage(image);
            }

            button.transition = Selectable.Transition.ColorTint;
            button.colors = SampleColorBlock();

            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                label.color = Color.white;
            }
        }

        public static void ApplyTo(InputField input)
        {
            if (input == null)
            {
                return;
            }

            if (input.targetGraphic is Image image)
            {
                ConfigureControlImage(image);
            }

            input.transition = Selectable.Transition.ColorTint;
            input.colors = SampleColorBlock();
            if (input.textComponent != null)
            {
                input.textComponent.color = Color.white;
            }
            if (input.placeholder is Text placeholder)
            {
                placeholder.color = new Color(1f, 1f, 1f, 0.5f);
            }
        }

        private static void ConfigureControlImage(Image image)
        {
            ConfigureSurface(image, ControlSurface);
        }

        private static void ConfigureSurface(Image image, Color color)
        {
            image.color = color;
            // Scene-authored controls use Ubiq's real built-in sliced sprite.
            // Only runtime-created graphics need the generated equivalent.
            if (image.sprite == null)
            {
                uiSprite ??= CreateUiSprite();
                image.sprite = uiSprite;
            }
            image.type = Image.Type.Sliced;
        }

        private static Sprite CreateUiSprite()
        {
            const int size = 32;
            const float radius = 7f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32,
                false)
            {
                name = "MeshUp Ubiq UI Sprite",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Max(Mathf.Abs(x + 0.5f - half)
                        - (half - radius), 0f);
                    var dy = Mathf.Max(Mathf.Abs(y + 0.5f - half)
                        - (half - radius), 0f);
                    var distance = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    var alpha = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(0.5f - distance) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(8f, 8f, 8f, 8f));
            sprite.name = "MeshUp Ubiq UI Sprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static ColorBlock SampleColorBlock()
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
