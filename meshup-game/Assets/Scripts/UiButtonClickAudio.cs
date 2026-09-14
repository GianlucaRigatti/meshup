using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meshup.UI
{
    /// <summary>
    /// Adds a consistent, non-spatial click sound to every Unity UI button.
    /// Physical generator and size controls use XR interactables rather than
    /// Unity UI buttons, so they are intentionally outside this system.
    /// </summary>
    public sealed class UiButtonClickAudio : MonoBehaviour
    {
        private const string ClipResourcePath = "ButtonClick";
        private const float ClickVolume = 0.25f;

        private AudioSource audioSource;
        private AudioClip clickClip;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var gameObject = new GameObject("UI Button Click Audio");
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<UiButtonClickAudio>();
        }

        private void Awake()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = ClickVolume;
            audioSource.ignoreListenerPause = true;

            clickClip = Resources.Load<AudioClip>(ClipResourcePath);
            if (clickClip == null)
            {
                Debug.LogWarning($"[MeshUp] UI click sound not found at "
                    + $"Resources/{ClipResourcePath}.");
            }

            SceneManager.sceneLoaded += HandleSceneLoaded;
            Canvas.willRenderCanvases += BindUnregisteredButtons;
            BindUnregisteredButtons();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BindUnregisteredButtons();
        }

        private void BindUnregisteredButtons()
        {
            var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include);
            foreach (var button in buttons)
            {
                var relay = button.GetComponent<UiButtonClickAudioRelay>();
                if (relay == null)
                {
                    relay = button.gameObject.AddComponent<
                        UiButtonClickAudioRelay>();
                }
                relay.Bind(this, button);
            }
        }

        internal void PlayClick()
        {
            if (clickClip != null)
            {
                audioSource.PlayOneShot(clickClip);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Canvas.willRenderCanvases -= BindUnregisteredButtons;
        }
    }

    internal sealed class UiButtonClickAudioRelay : MonoBehaviour
    {
        private UiButtonClickAudio player;
        private Button button;

        internal void Bind(UiButtonClickAudio clickAudio, Button targetButton)
        {
            if (button != null)
            {
                return;
            }

            player = clickAudio;
            button = targetButton;
            button.onClick.AddListener(Play);
        }

        private void Play()
        {
            player?.PlayClick();
        }

        private void OnDestroy()
        {
            button?.onClick.RemoveListener(Play);
        }
    }
}
