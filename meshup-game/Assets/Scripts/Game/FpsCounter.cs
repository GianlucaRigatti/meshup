using UnityEngine;
using UnityEngine.UI;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class FpsCounter : MonoBehaviour
    {
        private const float DefaultRefreshInterval = 0.5f;

        [SerializeField, Min(0.1f)]
        private float refreshInterval = DefaultRefreshInterval;

        private Text label;
        private float elapsedTime;
        private int elapsedFrames;

        public float FramesPerSecond { get; private set; }

        private void Awake()
        {
            BuildOverlay();
        }

        private void Update()
        {
            RecordFrame(Time.unscaledDeltaTime);
        }

        private void RecordFrame(float frameTime)
        {
            if (frameTime <= 0f)
            {
                return;
            }

            elapsedTime += frameTime;
            elapsedFrames++;
            if (elapsedTime < Mathf.Max(0.1f, refreshInterval))
            {
                return;
            }

            FramesPerSecond = elapsedFrames / elapsedTime;
            label.text = $"{Mathf.RoundToInt(FramesPerSecond)} FPS";
            elapsedTime = 0f;
            elapsedFrames = 0;
        }

        private void BuildOverlay()
        {
            var overlay = new GameObject("FPS Overlay", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler));
            overlay.transform.SetParent(transform, false);

            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var scaler = overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var panel = new GameObject("Background", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(overlay.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.one;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            panelRect.sizeDelta = new Vector2(112f, 38f);
            panelRect.anchoredPosition = new Vector2(-16f, -16f);
            var background = panel.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.65f);
            background.raycastTarget = false;

            var textObject = new GameObject("FPS", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(panel.transform, false);
            label = textObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            label.fontSize = 20;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = "-- FPS";

            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }
    }
}
