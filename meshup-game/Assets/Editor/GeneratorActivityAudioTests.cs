using Meshup.Game;
using NUnit.Framework;
using System.Linq;
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
        public void SourcesArePointSpatializedAtTheGenerator()
        {
            var owner = new GameObject("Generator spatial audio test");
            try
            {
                var audio = owner.AddComponent<GeneratorActivityAudio>();
                typeof(GeneratorActivityAudio).GetMethod("Awake",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)?.Invoke(audio, null);
                var sources = owner.GetComponents<AudioSource>();

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
                Assert.That(sources.Count(source =>
                    source.loop && source.clip != null), Is.EqualTo(1));

                var cueSource = sources.Single(source => !source.loop);
                Assert.That(cueSource.volume, Is.GreaterThan(0.4f));
                Assert.That(cueSource.minDistance, Is.EqualTo(2.5f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
