using System;

namespace Meshup.Game
{
    /// <summary>Applies local visual and audio effects of a host snapshot.</summary>
    internal sealed class MeshupGameSnapshotEffects
    {
        private readonly GeneratedObjectManager generatedObjects;
        private readonly GeneratedObjectSizeSelector sizeSelector;
        private readonly GeneratorActivityAudio generatorAudio;
        private readonly CorrectGuessAudio correctGuessAudio;
        private readonly MeshupVictoryFireworks fireworks;
        private readonly Action<bool> setParticles;

        public MeshupGameSnapshotEffects(GeneratedObjectManager generatedObjects,
            GeneratedObjectSizeSelector sizeSelector,
            GeneratorActivityAudio generatorAudio,
            CorrectGuessAudio correctGuessAudio,
            MeshupVictoryFireworks fireworks, Action<bool> setParticles)
        {
            this.generatedObjects = generatedObjects;
            this.sizeSelector = sizeSelector;
            this.generatorAudio = generatorAudio;
            this.correctGuessAudio = correctGuessAudio;
            this.fireworks = fireworks;
            this.setParticles = setParticles;
        }

        public void Apply(MeshupMatchSnapshot previous,
            MeshupMatchSnapshot next, bool hasPrevious,
            MeshupGamePhase previousPhase)
        {
            var nextPhase = (MeshupGamePhase)next.phase;
            var enteredFinished = hasPrevious
                && previousPhase != MeshupGamePhase.Finished
                && nextPhase == MeshupGamePhase.Finished;
            var enteredCorrectResult = CorrectGuessAudio.ShouldPlayForTransition(
                hasPrevious, previousPhase, next);
            var animateSize = hasPrevious
                && next.roundNumber == previous.roundNumber
                && next.sizeSelectionRevision > previous.sizeSelectionRevision;

            setParticles(next.generationPending);
            generatorAudio?.SetGenerating(next.generationPending);
            if (previous.generationPending && !next.generationPending)
            {
                var oldCount = previous.generatedObjects?.Length ?? 0;
                var newCount = next.generatedObjects?.Length ?? 0;
                if (newCount > oldCount)
                {
                    generatorAudio?.PlayCompletion();
                }
                else
                {
                    generatorAudio?.PlayFailure();
                }
            }
            generatedObjects.Reconcile(next.generatedObjects);
            sizeSelector?.ApplySelectedSize(next.selectedSize, animateSize);
            if (enteredCorrectResult)
            {
                correctGuessAudio?.Play();
            }
            if (enteredFinished)
            {
                fireworks?.Play();
            }
        }
    }
}
