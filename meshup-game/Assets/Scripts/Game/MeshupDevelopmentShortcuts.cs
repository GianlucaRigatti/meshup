using UnityEngine;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupDevelopmentShortcuts : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private MeshupGameCoordinator coordinator;
        [SerializeField] private MeshupVictoryFireworks victoryFireworks;
        [SerializeField] private GeneratorActivityAudio generatorActivityAudio;
        [SerializeField] private ParticleSystem generatorParticles;
        private bool audioPreview;

        private void Update()
        {
            if (coordinator == null || !coordinator.isActiveAndEnabled) return;
            if (Input.GetKeyDown(KeyCode.F7)) ToggleGeneratorAudioPreview();
            if (Input.GetKeyDown(KeyCode.F8)) victoryFireworks?.PlayForTesting();
            if (Input.GetKeyDown(KeyCode.F9)) PreviewGeneratorFailure();
        }

        private void ToggleGeneratorAudioPreview()
        {
            if (generatorActivityAudio == null)
            {
                return;
            }

            audioPreview = !audioPreview;
            if (audioPreview)
            {
                SetParticleState(true);
                generatorActivityAudio.SetGenerating(true);
            }
            else
            {
                var generationPending = coordinator.CurrentSnapshot?.generationPending == true;
                SetParticleState(generationPending);
                generatorActivityAudio.SetGenerating(generationPending);
                if (!generationPending)
                {
                    generatorActivityAudio.PlayCompletion();
                }
            }
        }

        private void PreviewGeneratorFailure()
        {
            if (generatorActivityAudio == null)
            {
                return;
            }

            audioPreview = false;
            var generationPending = coordinator.CurrentSnapshot?.generationPending == true;
            SetParticleState(generationPending);
            generatorActivityAudio.SetGenerating(generationPending);
            if (!generationPending)
            {
                generatorActivityAudio.PlayFailure();
            }
        }

        private void SetParticleState(bool active)
        {
            if (generatorParticles == null)
            {
                return;
            }
            if (active && !generatorParticles.isPlaying)
            {
                generatorParticles.Play(true);
            }
            else if (!active && generatorParticles.isPlaying)
            {
                generatorParticles.Stop(true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
#endif
    }
}
