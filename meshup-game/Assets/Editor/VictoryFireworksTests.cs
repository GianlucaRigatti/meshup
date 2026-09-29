using System.Collections;
using System.Linq;
using Meshup.EditorTools;
using Meshup.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Meshup.Editor.Tests
{
    public sealed class VictoryFireworksTests
    {
        private const string PrefabPath = "Assets/Prefabs/Fireworks/Firework_BlueNeon.prefab";
        private const string ClipPath = "Assets/Sounds/Fireworks/SFX_firework1.wav";

        [Test]
        public void SceneUsesSavedParticleFireworksWithAuthoredMaterialsAndAudio()
        {
            using var scope = new SceneValidationScope("Assets/Scenes/GameScene.unity");
            var fireworks = scope.Scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<MeshupVictoryFireworks>(true)).Single();
            var data = new SerializedObject(fireworks);
            var prefabs = data.FindProperty("fireworks");
            Assert.That(prefabs.arraySize, Is.GreaterThan(0));
            for (var i = 0; i < prefabs.arraySize; i++)
            {
                var burst = (ParticleSystem)prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.That(burst, Is.Not.Null);
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(burst), Is.True);
                Assert.That(burst.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty,
                    "Fireworks should contain native particle/audio components without VFX scripts.");
                var particles = burst.GetComponentsInChildren<ParticleSystem>();
                Assert.That(particles, Has.Length.EqualTo(2));
                var rocket = particles.Single(item => item != burst);
                Assert.That(burst.main.loop, Is.False);
                Assert.That(burst.main.playOnAwake, Is.False);
                Assert.That(burst.emission.burstCount, Is.GreaterThan(0));
                Assert.That(rocket.main.loop, Is.True);
                Assert.That(rocket.emission.rateOverTime.constant, Is.GreaterThan(0f));
                var renderer = burst.GetComponent<ParticleSystemRenderer>();
                Assert.That(AssetDatabase.Contains(renderer.sharedMaterial), Is.True);
                Assert.That(renderer.trailMaterial, Is.SameAs(renderer.sharedMaterial));
                Assert.That(rocket.GetComponent<ParticleSystemRenderer>().sharedMaterial,
                    Is.SameAs(renderer.sharedMaterial));
                var audio = burst.GetComponent<AudioSource>();
                Assert.That(audio, Is.Not.Null);
                Assert.That(audio.playOnAwake, Is.False);
                Assert.That(audio.volume, Is.GreaterThan(0f));
            }
            var clips = data.FindProperty("explosionClips");
            Assert.That(clips.arraySize, Is.GreaterThan(0));
            for (var i = 0; i < clips.arraySize; i++)
                Assert.That(clips.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator DesktopCelebrationLaunchesTrailThenPlaysBurstAndSound()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefabAsset, Is.Not.Null, PrefabPath);
            var burstPrefab = prefabAsset.GetComponent<ParticleSystem>();
            Assert.That(burstPrefab, Is.Not.Null, "The prefab root must own the burst.");
            var explosionClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
            Assert.That(explosionClip, Is.Not.Null, ClipPath);
            var owner = new GameObject("Test fireworks");
            var fireworks = owner.AddComponent<MeshupVictoryFireworks>();
            var data = new SerializedObject(fireworks);
            data.FindProperty("guesserScreen").objectReferenceValue = owner.transform;
            var prefabs = data.FindProperty("fireworks");
            prefabs.arraySize = 1;
            prefabs.GetArrayElementAtIndex(0).objectReferenceValue = burstPrefab;
            var clips = data.FindProperty("explosionClips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = explosionClip;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);

            yield return new EnterPlayMode();
            var runtimeOwner = GameObject.Find("Test fireworks");
            Assert.That(runtimeOwner, Is.Not.Null, "The authored test object must survive entering Play Mode.");
            fireworks = runtimeOwner.GetComponent<MeshupVictoryFireworks>();
            var runtimeData = new SerializedObject(fireworks);
            Assert.That(runtimeData.FindProperty("fireworks").GetArrayElementAtIndex(0).objectReferenceValue,
                Is.Not.Null, "The saved prefab reference must survive entering Play Mode.");

            fireworks.Play();
            fireworks.Play();
            var spawned = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            var burst = spawned.Single(particles => !particles.main.loop);
            var rocket = burst.GetComponentsInChildren<ParticleSystem>().Single(particles => particles.main.loop);
            Assert.That(burst.isPlaying, Is.False, "The burst waits for the rocket's apex.");
            Assert.That(rocket.isPlaying, Is.True);
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!burst.isPlaying && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(burst.transform.position.y, Is.EqualTo(12.25f).Within(0.01f));
            Assert.That(rocket.isEmitting, Is.False);
            Assert.That(burst.isPlaying, Is.True);
            Assert.That(burst.GetComponent<AudioSource>().clip.name, Is.EqualTo("SFX_firework1"));
            Object.Destroy(fireworks.gameObject);
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
