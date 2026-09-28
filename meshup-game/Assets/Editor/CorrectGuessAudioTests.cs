using Meshup.Game;
using Meshup.EditorTools;
using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class CorrectGuessAudioTests
    {
        [Test]
        public void SceneAuthorsTheSuccessClipAndPositionalAudio()
        {
            using var scene = new SceneValidationScope("Assets/Scenes/GameScene.unity");
            var cue = scene.Scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CorrectGuessAudio>(true))
                .Single();
            Assert.That(cue, Is.Not.Null);
            var source = new SerializedObject(cue).FindProperty("audioSource")
                .objectReferenceValue as AudioSource;
            Assert.That(cue.IsReady, Is.True);
            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.Not.Null);
            Assert.That(source.playOnAwake, Is.False);
            Assert.That(source.loop, Is.False);
            Assert.That(source.spatialBlend, Is.EqualTo(1f));
            Assert.That(source.spatialize, Is.True);
            Assert.That(source.maxDistance, Is.EqualTo(18f));
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
