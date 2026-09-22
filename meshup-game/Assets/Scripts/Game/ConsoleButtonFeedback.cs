using UnityEngine;

namespace Meshup.Game
{
    /// <summary>Rigid press travel and emissive indicator, without real-time lights.</summary>
    [DisallowMultipleComponent]
    public sealed class ConsoleButtonFeedback : MonoBehaviour
    {
        [SerializeField] private Transform indicator;
        [SerializeField] private Vector3 pressOffset = new(0f, -0.009899495f, -0.009899495f);
        private Vector3 restPosition;
        private bool initialized;
        private bool selected;
        private bool held;
        private float pulseStart = float.NegativeInfinity;
        private float amount;

        public void Configure(Transform lightIndicator, Vector3 travel)
        {
            indicator = lightIndicator;
            pressOffset = travel;
            Initialize();
            ApplyVisuals(0f);
        }

        private void Awake() => Initialize();

        private void Initialize()
        {
            if (initialized) return;
            restPosition = transform.localPosition;
            initialized = true;
        }

        public void SetSelected(bool value)
        {
            Initialize();
            selected = value;
            ApplyVisuals(amount);
        }

        public void Pulse()
        {
            Initialize();
            pulseStart = Time.unscaledTime;
        }

        public void SetHeld(bool value)
        {
            Initialize();
            held = value;
        }

        private void Update()
        {
            var elapsed = Time.unscaledTime - pulseStart;
            var pulse = elapsed < 0.1f ? elapsed / 0.1f
                : elapsed < 0.3f ? 1f : elapsed < 0.5f ? (0.5f - elapsed) / 0.2f : 0f;
            amount = Mathf.MoveTowards(amount, held ? 1f : Mathf.Clamp01(pulse),
                Time.unscaledDeltaTime * 12f);
            ApplyVisuals(amount);
        }

        private void ApplyVisuals(float depression)
        {
            transform.localPosition = restPosition + pressOffset * depression;
            if (indicator != null)
                indicator.localScale = Vector3.one * (selected || depression > 0.02f ? 1f : 0.001f);
        }

        private void OnDisable()
        {
            held = false;
            pulseStart = float.NegativeInfinity;
            amount = 0f;
            if (initialized) ApplyVisuals(0f);
        }
    }
}
