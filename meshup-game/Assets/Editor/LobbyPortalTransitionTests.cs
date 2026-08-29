using System;
using System.Linq;
using System.Reflection;
using Meshup.Lobby;
using Meshup.Multiplayer;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Meshup.EditorTests
{
    public sealed class LobbyPortalTransitionTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void LobbySceneContainsFullyWiredComfortSafePortal()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = scene.GetRootGameObjects()
                .Single(item => item.name == "Lobby Portal Transition");
            var transition = root.GetComponent<LobbyPortalTransition>();
            Assert.That(transition, Is.Not.Null);

            var serialized = new SerializedObject(transition);
            foreach (var propertyName in new[]
            {
                "visualRoot", "cyanMaterial", "goldMaterial", "veilMaterial",
                "overlayCanvas", "overlayGroup", "overlayImage",
                "traditionalController", "roomUiCanvasGroup"
            })
            {
                Assert.That(serialized.FindProperty(propertyName).objectReferenceValue,
                    Is.Not.Null, propertyName);
            }

            Assert.That(serialized.FindProperty("locomotionBehaviours").arraySize,
                Is.GreaterThan(0));
            Assert.That(serialized.FindProperty("materializeDuration").floatValue,
                Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(serialized.FindProperty("engulfDuration").floatValue,
                Is.EqualTo(1.1f).Within(0.001f));
            Assert.That(serialized.FindProperty("revealDuration").floatValue,
                Is.EqualTo(0.35f).Within(0.001f));
        }

        [Test]
        public void RoomSessionExposesCallbackBasedTransitionHandshake()
        {
            var transitionEvent = typeof(UbiqRoomSession).GetEvent(
                "GameSceneTransitionRequested", BindingFlags.Public
                | BindingFlags.Instance);

            Assert.That(transitionEvent, Is.Not.Null);
            Assert.That(transitionEvent.EventHandlerType, Is.EqualTo(typeof(Action<Action>)));
            Assert.That(Enum.GetNames(typeof(RoomSessionState)),
                Does.Not.Contain("EnteringPortal"));
        }

        [Test]
        public void LobbyKeepsUnusedUbiqSpawnManagerInactiveAcrossReloads()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var networkScene = scene.GetRootGameObjects()
                .Single(item => item.name == "Ubiq Network Scene");
            var spawnManager = networkScene.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "Spawn Manager");

            Assert.That(spawnManager.gameObject.activeSelf, Is.False,
                "An active duplicate is destroyed before Start and throws in Ubiq's OnDestroy.");
        }
    }
}
