using System.Collections;
using UnityEngine;

namespace Meshup.Game
{
    /// <summary>
    /// Provides a calm, spatial ambience while the object generator is working.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratorActivityAudio : MonoBehaviour
    {
        private const float ActivityVolume = 0.28f;
        private const float FadeDuration = 1.25f;

        [SerializeField] private AudioSource activitySource;
        [SerializeField] private AudioSource cueSource;
        [SerializeField] private AudioClip completionClip;
        [SerializeField] private AudioClip failureClip;
        private Coroutine fadeRoutine;

        public bool IsGenerating { get; private set; }

        public void SetGenerating(bool active)
        {
            if (IsGenerating == active)
            {
                return;
            }

            IsGenerating = active;
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }

            if (active)
            {
                if (activitySource == null || activitySource.clip == null)
                {
                    return;
                }

                activitySource.volume = 0f;
                activitySource.Play();
                if (Application.isPlaying && isActiveAndEnabled)
                {
                    fadeRoutine = StartCoroutine(FadeActivity(
                        ActivityVolume, false));
                }
                else
                {
                    activitySource.volume = ActivityVolume;
                }
            }
            else
            {
                if (activitySource == null)
                {
                    return;
                }

                if (Application.isPlaying && isActiveAndEnabled
                    && activitySource.isPlaying)
                {
                    fadeRoutine = StartCoroutine(FadeActivity(0f, true));
                }
                else
                {
                    activitySource.Stop();
                    activitySource.volume = 0f;
                }
            }
        }

        public void PlayCompletion()
        {
            PlayCue(completionClip);
        }

        public void PlayFailure()
        {
            PlayCue(failureClip);
        }

        private IEnumerator FadeActivity(float targetVolume, bool stopAfterFade)
        {
            var startVolume = activitySource.volume;
            var elapsed = 0f;
            while (elapsed < FadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                activitySource.volume = Mathf.Lerp(startVolume, targetVolume,
                    Mathf.Clamp01(elapsed / FadeDuration));
                yield return null;
            }

            activitySource.volume = targetVolume;
            if (stopAfterFade)
            {
                activitySource.Stop();
            }
            fadeRoutine = null;
        }

        private void PlayCue(AudioClip clip)
        {
            if (cueSource != null && clip != null)
            {
                cueSource.pitch = 1f;
                cueSource.PlayOneShot(clip);
            }
        }

        private void OnDisable()
        {
            SetGenerating(false);
        }
    }
}
