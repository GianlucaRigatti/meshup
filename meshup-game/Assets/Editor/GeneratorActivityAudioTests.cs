using Meshup.Game;
using Meshup.EditorTools;
using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class GeneratorActivityAudioTests
    {
        [Test]
        public void GenerationStateCanBeStartedAndStopped()
        {
            var owner = new GameObject("Generator audio test");
            try
            {
                var audio = owner.AddComponent<GeneratorActivityAudio>();

                audio.SetGenerating(true);
                Assert.That(audio.IsGenerating, Is.True);

                audio.SetGenerating(false);
                Assert.That(audio.IsGenerating, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void SceneAuthorsPointSpatializedSourcesAtTheGenerator()
        {
            using var scene = new SceneValidationScope("Assets/Scenes/GameScene.unity");
            var audio = scene.Scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GeneratorActivityAudio>(true))
                .Single();
            Assert.That(audio, Is.Not.Null);
            var serialized = new SerializedObject(audio);
            var activitySource = serialized.FindProperty("activitySource")
                .objectReferenceValue as AudioSource;
            var cueSource = serialized.FindProperty("cueSource")
                .objectReferenceValue as AudioSource;
            Assert.That(activitySource, Is.Not.Null);
            Assert.That(cueSource, Is.Not.Null);
            Assert.That(activitySource, Is.Not.SameAs(cueSource));
            var sources = audio.GetComponents<AudioSource>();
            Assert.That(sources, Has.Length.EqualTo(2));
            Assert.That(sources.All(source =>
                Mathf.Approximately(source.spatialBlend, 1f)), Is.True);
            Assert.That(sources.All(source => source.spatialize), Is.True);
            Assert.That(sources.All(source =>
                Mathf.Approximately(source.spread, 0f)), Is.True);
            Assert.That(sources.All(source =>
                Mathf.Approximately(source.dopplerLevel, 0f)), Is.True);
            Assert.That(sources.All(source =>
                Mathf.Approximately(source.maxDistance, 14f)), Is.True);
            Assert.That(activitySource.loop && activitySource.clip != null, Is.True);
            Assert.That(cueSource.loop, Is.False);
            Assert.That(cueSource.volume, Is.GreaterThan(0.4f));
            Assert.That(cueSource.minDistance, Is.EqualTo(2.5f));
        }
    }
}
