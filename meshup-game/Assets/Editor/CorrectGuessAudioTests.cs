using Meshup.Game;
using NUnit.Framework;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class CorrectGuessAudioTests
    {
        [Test]
        public void LoadsTheDownloadedClipAndUsesPositionalAudio()
        {
            var owner = new GameObject("Correct guess audio test");
            try
            {
                var cue = owner.AddComponent<CorrectGuessAudio>();
                cue.Configure();
                var source = owner.GetComponent<AudioSource>();

                Assert.That(cue.IsReady, Is.True);
                Assert.That(source, Is.Not.Null);
                Assert.That(source.playOnAwake, Is.False);
                Assert.That(source.loop, Is.False);
                Assert.That(source.spatialBlend, Is.EqualTo(1f));
                Assert.That(source.spatialize, Is.True);
                Assert.That(source.maxDistance, Is.EqualTo(18f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void PlaysOnlyWhenTimedGuessingEndsWithACorrectGuess()
        {
            var correctResult = new MeshupMatchSnapshot
            {
                phase = (int)MeshupGamePhase.Result,
                resultMessage = "Ada guessed"
            };
            var timedOutResult = new MeshupMatchSnapshot
            {
                phase = (int)MeshupGamePhase.Result,
                resultMessage = "Time's up"
            };

            Assert.That(CorrectGuessAudio.ShouldPlayForTransition(true,
                MeshupGamePhase.TimedGuessing, correctResult), Is.True);
            Assert.That(CorrectGuessAudio.ShouldPlayForTransition(true,
                MeshupGamePhase.TimedGuessing, timedOutResult), Is.False);
            Assert.That(CorrectGuessAudio.ShouldPlayForTransition(false,
                MeshupGamePhase.TimedGuessing, correctResult), Is.False);
            Assert.That(CorrectGuessAudio.ShouldPlayForTransition(true,
                MeshupGamePhase.Result, correctResult), Is.False);
            Assert.That(CorrectGuessAudio.ShouldPlayForTransition(true,
                MeshupGamePhase.TimedGuessing, new MeshupMatchSnapshot
                {
                    phase = (int)MeshupGamePhase.Result
                }), Is.False);
        }
    }
}
