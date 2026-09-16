using System;
using UnityEngine;

namespace Meshup.Game
{
    /// <summary>Plays the shared success cue when a guess ends the round.</summary>
    [DisallowMultipleComponent]
    public sealed class CorrectGuessAudio : MonoBehaviour
    {
        private const string ClipResourcePath = "SuccessNotification";
        private const float CueVolume = 0.55f;

        private AudioSource audioSource;
        private AudioClip successClip;

        public bool IsReady => audioSource != null && successClip != null;

        private void Awake()
        {
            Configure();
        }

        public void Configure()
        {
            if (audioSource != null)
            {
                return;
            }

            successClip = Resources.Load<AudioClip>(ClipResourcePath);
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;
            audioSource.spatialize = true;
            audioSource.spread = 0f;
            audioSource.dopplerLevel = 0f;
            audioSource.volume = CueVolume;
            audioSource.minDistance = 2.5f;
            audioSource.maxDistance = 18f;

            if (successClip == null)
            {
                Debug.LogWarning(
                    $"Correct-guess sound was not found at Resources/{ClipResourcePath}.",
                    this);
            }
        }

        public void Play()
        {
            if (IsReady)
            {
                audioSource.PlayOneShot(successClip);
            }
        }

        public static bool ShouldPlayForTransition(bool hasPreviousSnapshot,
            MeshupGamePhase previousPhase, MeshupMatchSnapshot nextSnapshot)
        {
            return hasPreviousSnapshot && nextSnapshot != null
                && previousPhase == MeshupGamePhase.TimedGuessing
                && (MeshupGamePhase)nextSnapshot.phase == MeshupGamePhase.Result
                && !string.IsNullOrWhiteSpace(nextSnapshot.resultMessage)
                && !string.Equals(nextSnapshot.resultMessage, "Time's up",
                    StringComparison.Ordinal);
        }
    }
}
