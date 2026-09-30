using System.Linq;
using Meshup.EditorTools;
using Meshup.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Meshup.Editor.Tests
{
    public sealed class AuthoredPresentationTests
    {
        [Test]
        public void SessionMenuHasAuthoredVoiceControlAndXrRaycaster()
        {
            using var scope = new SceneValidationScope("Assets/Scenes/GameScene.unity");
            var menu = scope.Scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GameSessionMenu>(true))
                .Single();
            var serialized = new SerializedObject(menu);
            var voice = serialized.FindProperty("voiceButton").objectReferenceValue as Button;
            var label = serialized.FindProperty("voiceButtonLabel").objectReferenceValue as Text;
            Assert.That(voice, Is.Not.Null);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.transform.IsChildOf(voice.transform), Is.True);
            Assert.That(menu.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
        }

        [Test]
        public void DoorAndPoseidonAudioAreAuthoredInTheScene()
        {
            using var scope = new SceneValidationScope("Assets/Scenes/GameScene.unity");
            var roots = scope.Scene.GetRootGameObjects();
            var door = roots.SelectMany(root => root.GetComponentsInChildren<GameStartDoorController>(true)).Single();
            var doorSource = new SerializedObject(door).FindProperty("openingAudioSource")
                .objectReferenceValue as AudioSource;
            Assert.That(doorSource, Is.Not.Null);
            Assert.That(doorSource.clip, Is.Not.Null);
            Assert.That(doorSource.playOnAwake, Is.False);

            var start = roots.SelectMany(root => root.GetComponentsInChildren<GameStartCoordinator>(true)).Single();
            var serialized = new SerializedObject(start);
            var poseidonSource = serialized.FindProperty("poseidonAudioSource")
                .objectReferenceValue as AudioSource;
            var light = serialized.FindProperty("poseidonSpeakingLight")
                .objectReferenceValue as Light;
            Assert.That(poseidonSource, Is.Not.Null);
            Assert.That(poseidonSource.clip, Is.Not.Null);
            Assert.That(poseidonSource.playOnAwake, Is.False);
            Assert.That(light, Is.Not.Null);
            Assert.That(light.transform, Is.EqualTo(poseidonSource.transform));
            Assert.That(light.intensity, Is.Zero);
        }

    }
}
