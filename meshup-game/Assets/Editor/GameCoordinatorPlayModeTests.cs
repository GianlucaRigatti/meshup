using System.Collections;
using System.Linq;
using System.Reflection;
using Meshup.Game;
using NUnit.Framework;
using Ubiq.Messaging;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Meshup.Editor.Tests
{
    public sealed class GameCoordinatorPlayModeTests
    {
        [UnityTest]
        public IEnumerator DestroyingCoordinatorRemovesItsProcessorInPlayMode()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var root = new GameObject("Test network parent");
            var networkObject = new GameObject("Test network");
            networkObject.transform.SetParent(root.transform);
            var network = networkObject.AddComponent<NetworkScene>();
            var owner = new GameObject("Test coordinator");
            owner.transform.SetParent(networkObject.transform);
            var coordinator = owner.AddComponent<MeshupGameCoordinator>();
            coordinator.enabled = false; // This test exercises registration and destruction without starting a match.
            var context = NetworkScene.Register(coordinator);
            typeof(MeshupGameCoordinator).GetField("networkContext", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(coordinator, context);
            Assert.That(network.GetProcessors().Count(), Is.EqualTo(1));
            Object.Destroy(coordinator);
            yield return null;
            Assert.That(network.GetProcessors(), Is.Empty);
            Object.Destroy(root);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator DestroyingUnstartedCoordinatorPreservesTheAuthoredWall()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            foreach (var initiallyEnabled in new[] { true, false })
            {
                var root = new GameObject("Uninitialized game runtime");
                var wallObject = new GameObject("Authored wall");
                wallObject.transform.SetParent(root.transform);
                var wall = wallObject.AddComponent<BoxCollider>();
                wall.enabled = initiallyEnabled;
                var coordinator = root.AddComponent<MeshupGameCoordinator>();
                coordinator.enabled = false;
                var data = new SerializedObject(coordinator);
                data.FindProperty("invisibleWall").objectReferenceValue = wall;
                data.ApplyModifiedPropertiesWithoutUndo();

                Object.Destroy(coordinator);
                yield return null;
                Assert.That(wall.enabled, Is.EqualTo(initiallyEnabled));
                Object.Destroy(root);
                yield return null;
            }
            yield return new ExitPlayMode();
        }
    }
}
