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
        private const string ActivityClipResourcePath = "HolyAuraResonance";
        private const int SampleRate = 24000;
        private const float ActivityVolume = 0.22f;
        private const float CueVolume = 0.45f;
        private const float FadeDuration = 1.25f;

        private AudioSource activitySource;
        private AudioSource cueSource;
        private AudioClip completionClip;
        private AudioClip failureClip;
        private Coroutine fadeRoutine;

        public bool IsGenerating { get; private set; }

        private void Awake()
        {
            activitySource = CreateSource(0f);
            activitySource.loop = true;
            activitySource.clip = Resources.Load<AudioClip>(ActivityClipResourcePath);
            if (activitySource.clip == null)
            {
                Debug.LogWarning(
                    $"Generator ambience was not found at Resources/{ActivityClipResourcePath}.",
                    this);
            }

            cueSource = CreateSource(CueVolume);
            cueSource.minDistance = 2.5f;
            completionClip = CreateCompletionCue();
            failureClip = CreateFailureCue();
        }

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
            if (clip != null)
            {
                cueSource.pitch = 1f;
                cueSource.PlayOneShot(clip);
            }
        }

        private AudioSource CreateSource(float volume)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.panStereo = 0f;
            source.spatialBlend = 1f;
            source.spatialize = true;
            source.spread = 0f;
            source.dopplerLevel = 0f;
            source.volume = volume;
            source.minDistance = 1f;
            source.maxDistance = 14f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.2f, 0.9f),
                    new Keyframe(0.45f, 0.55f),
                    new Keyframe(0.7f, 0.2f),
                    new Keyframe(1f, 0f)));
            return source;
        }

        private static AudioClip CreateCompletionCue()
        {
            const float duration = 1.6f;
            var samples = new float[Mathf.CeilToInt(SampleRate * duration)];
            for (var index = 0; index < samples.Length; index++)
            {
                var time = index / (float)SampleRate;
                var envelope = Mathf.Sin(Mathf.PI * time / duration);
                envelope *= envelope * Mathf.Exp(-0.7f * time);
                var first = Mathf.Sin(2f * Mathf.PI * 392f * time);
                var secondEnvelope = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.28f, 0.72f, time));
                var second = Mathf.Sin(2f * Mathf.PI * 523.25f * time)
                    * secondEnvelope;
                samples[index] = envelope * (first * 0.2f + second * 0.16f);
            }
            return CreateClip("Generator Complete", samples);
        }

        private static AudioClip CreateFailureCue()
        {
            const float duration = 1.15f;
            var samples = new float[Mathf.CeilToInt(SampleRate * duration)];
            var phase = 0f;
            for (var index = 0; index < samples.Length; index++)
            {
                var time = index / (float)SampleRate;
                var progress = time / duration;
                var frequency = Mathf.Lerp(293.66f, 220f, progress);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                var envelope = Mathf.Sin(Mathf.PI * progress);
                envelope *= envelope;
                samples[index] = Mathf.Sin(phase) * envelope * 0.24f;
            }
            return CreateClip("Generator Failed", samples);
        }

        private static AudioClip CreateClip(string clipName, float[] samples)
        {
            var clip = AudioClip.Create(clipName, samples.Length, 1,
                SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDisable()
        {
            SetGenerating(false);
        }

        private void OnDestroy()
        {
            if (completionClip != null)
            {
                DestroyClip(completionClip);
            }
            if (failureClip != null)
            {
                DestroyClip(failureClip);
            }
        }

        private static void DestroyClip(AudioClip clip)
        {
            if (Application.isPlaying)
            {
                Destroy(clip);
            }
            else
            {
                DestroyImmediate(clip);
            }
        }
    }
}
