using System;
using UnityEngine;

namespace Meshup.Game
{
    /// <summary>Plays the shared success cue when a guess ends the round.</summary>
    [DisallowMultipleComponent]
    public sealed class CorrectGuessAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;

        public bool IsReady => audioSource != null && audioSource.clip != null;

        public void Play()
        {
            if (IsReady)
            {
                audioSource.PlayOneShot(audioSource.clip);
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
