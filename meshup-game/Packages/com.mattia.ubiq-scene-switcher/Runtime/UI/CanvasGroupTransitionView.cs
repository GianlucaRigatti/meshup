using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace Ubiq.SceneSwitcher.UI
{
    public sealed class CanvasGroupTransitionView : MonoBehaviour,
        IRoomSceneTransitionView
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text statusText;
        [Min(0f)] [SerializeField] private float duration = 0.2f;

        public Awaitable FadeOutAsync(CancellationToken token) => FadeAsync(1f, token);
        public Awaitable FadeInAsync(CancellationToken token) => FadeAsync(0f, token);

        public void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        private async Awaitable FadeAsync(float target, CancellationToken token)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.gameObject.SetActive(true);
            canvasGroup.blocksRaycasts = true;
            var start = canvasGroup.alpha;
            if (duration <= 0f)
            {
                canvasGroup.alpha = target;
            }
            else
            {
                var elapsed = 0f;
                while (elapsed < duration)
                {
                    token.ThrowIfCancellationRequested();
                    elapsed += Time.unscaledDeltaTime;
                    canvasGroup.alpha = Mathf.Lerp(start, target, elapsed / duration);
                    await Awaitable.NextFrameAsync();
                }
            }

            canvasGroup.alpha = target;
            canvasGroup.blocksRaycasts = target > 0f;
            if (target <= 0f) canvasGroup.gameObject.SetActive(false);
        }
    }
}
