using System.Collections;
using UnityEngine;

namespace Meshup.Lobby
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class HologramBillboard : MonoBehaviour
    {
        [SerializeField] private Transform followAnchor;
        [SerializeField] private RoomTotemPanel panel;
        [SerializeField] private GameObject idleRoot;
        [SerializeField] private float turnSpeed = 10f;
        [SerializeField] private float hoverAmount = 0.025f;
        [SerializeField] private float hoverSpeed = 1.4f;

        private Camera playerCamera;
        private Canvas worldCanvas;
        private bool wasOpen;
        private Coroutine panelAnimation;

        private void Awake()
        {
            worldCanvas = GetComponent<Canvas>();
            playerCamera = Camera.main;
            if (playerCamera != null)
            {
                worldCanvas.worldCamera = playerCamera;
            }
            wasOpen = false;
            idleRoot.SetActive(false);
        }

        private void LateUpdate()
        {
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
                if (playerCamera == null)
                {
                    return;
                }
                worldCanvas.worldCamera = playerCamera;
            }

            var hover = Mathf.Sin(Time.unscaledTime * hoverSpeed) * hoverAmount;
            transform.position = followAnchor.position + Vector3.up * hover;
            var direction = playerCamera.transform.position - transform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                // A world-space Canvas renders its front toward local -Z.
                var targetRotation = Quaternion.LookRotation(-direction.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation,
                    1f - Mathf.Exp(-turnSpeed * Time.unscaledDeltaTime));
            }

            var isOpen = panel.IsOpen;
            if (isOpen && !wasOpen)
            {
                if (panelAnimation != null)
                {
                    StopCoroutine(panelAnimation);
                }
                panelAnimation = StartCoroutine(AnimatePanelIn());
            }
            wasOpen = isOpen;
        }

        private IEnumerator AnimatePanelIn()
        {
            var panelRoot = panel != null ? panel.PanelRoot : null;
            if (panelRoot == null)
            {
                yield break;
            }
            var panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = panelRoot.gameObject.AddComponent<CanvasGroup>();
            }
            var fullScale = panelRoot.localScale;
            panelCanvasGroup.alpha = 0f;
            panelRoot.localScale = fullScale * 0.88f;
            var elapsed = 0f;
            const float duration = 0.24f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                panelCanvasGroup.alpha = eased;
                panelRoot.localScale = fullScale * Mathf.Lerp(0.88f, 1f, eased);
                yield return null;
            }
            panelCanvasGroup.alpha = 1f;
            panelRoot.localScale = fullScale;
        }
    }
}
