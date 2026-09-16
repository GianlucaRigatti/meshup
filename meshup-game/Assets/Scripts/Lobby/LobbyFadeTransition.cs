using System;
using System.Collections;
using Meshup.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meshup.Lobby
{
    /// <summary>
    /// Fades to black before the game scene loads and reveals the scene afterward.
    /// The overlay is created at runtime so the lobby contains no portal visuals.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyFadeTransition : MonoBehaviour
    {
        private const float FadeOutDuration = 0.35f;
        private const float FadeInDuration = 0.35f;

        private UbiqRoomSession session;
        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private Coroutine fadeSequence;
        private bool waitingForGameScene;

        public float Alpha => canvasGroup != null ? canvasGroup.alpha : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (FindAnyObjectByType<LobbyFadeTransition>() != null)
            {
                return;
            }

            new GameObject("Lobby Fade Transition")
                .AddComponent<LobbyFadeTransition>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            BuildOverlay();
            SetAlpha(0f);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryBindSession();
        }

        private void Update()
        {
            if (session == null)
            {
                TryBindSession();
            }

            UpdateCanvasCamera();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnbindSession();
        }

        private void TryBindSession()
        {
            var availableSession = UbiqRoomSession.Instance;
            if (availableSession == null || availableSession == session)
            {
                return;
            }

            UnbindSession();
            session = availableSession;
            session.GameSceneTransitionRequested += HandleTransitionRequested;
            session.StateChanged += HandleSessionStateChanged;
        }

        private void UnbindSession()
        {
            if (session == null)
            {
                return;
            }

            session.GameSceneTransitionRequested -= HandleTransitionRequested;
            session.StateChanged -= HandleSessionStateChanged;
            session = null;
        }

        private void HandleTransitionRequested(Action completeTransition)
        {
            if (completeTransition == null)
            {
                return;
            }

            if (waitingForGameScene)
            {
                completeTransition();
                return;
            }

            waitingForGameScene = true;
            StopFade();
            fadeSequence = StartCoroutine(FadeToBlack(completeTransition));
        }

        private IEnumerator FadeToBlack(Action completeTransition)
        {
            yield return Fade(Alpha, 1f, FadeOutDuration);
            completeTransition();
            fadeSequence = null;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!waitingForGameScene)
            {
                return;
            }

            waitingForGameScene = false;
            UpdateCanvasCamera();
            StopFade();
            fadeSequence = StartCoroutine(FadeFromBlack());
        }

        private void HandleSessionStateChanged(RoomSessionState state)
        {
            if (waitingForGameScene && state == RoomSessionState.Error)
            {
                waitingForGameScene = false;
                StopFade();
                fadeSequence = StartCoroutine(FadeFromBlack());
            }
        }

        private IEnumerator FadeFromBlack()
        {
            yield return Fade(Alpha, 0f, FadeInDuration);
            fadeSequence = null;
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            SetAlpha(from);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(elapsed / duration);
                SetAlpha(Mathf.Lerp(from, to,
                    progress * progress * (3f - 2f * progress)));
                yield return null;
            }

            SetAlpha(to);
        }

        private void StopFade()
        {
            if (fadeSequence == null)
            {
                return;
            }

            StopCoroutine(fadeSequence);
            fadeSequence = null;
        }

        private void BuildOverlay()
        {
            var overlay = new GameObject("Black Fade Overlay",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            overlay.transform.SetParent(transform, false);

            canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.planeDistance = 0.08f;
            canvas.sortingOrder = short.MaxValue;

            canvasGroup = overlay.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var cover = new GameObject("Black Cover",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            cover.transform.SetParent(overlay.transform, false);
            var rect = (RectTransform)cover.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            cover.GetComponent<Image>().color = Color.black;

            UpdateCanvasCamera();
        }

        private void UpdateCanvasCamera()
        {
            if (canvas != null && canvas.worldCamera != Camera.main)
            {
                canvas.worldCamera = Camera.main;
            }
        }

        private void SetAlpha(float alpha)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = alpha;
            canvasGroup.blocksRaycasts = alpha > 0.001f;
        }
    }
}
