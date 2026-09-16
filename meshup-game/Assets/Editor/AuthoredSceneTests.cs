using System.Linq;
using System.Reflection;
using Meshup.EditorTools;
using Meshup.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Meshup.Editor.Tests
{
    public sealed class AuthoredSceneTests
    {
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string LobbyScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void SavedGameRuntimeReferencesTheExistingPropsAndSurvivesRenames()
        {
            using var validation = new SceneValidationScope(GameScenePath);
            var scene = validation.Scene;
            MeshupGameSceneValidation.Validate(scene);
            var transforms = scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<Transform>(true)).ToArray();
            var coordinator = transforms.Select(item =>
                item.GetComponent<MeshupGameCoordinator>())
                .Single(item => item != null);
            var serialized = new SerializedObject(coordinator);
            var references = new (string Field, string ObjectName)[]
            {
                ("gameStart", "Game Start Sequence"),
                ("localPlayer", "Ubiq Demo Player"),
                ("invisibleWall", "Invisible_wall_game_area"),
                ("guesserMonitor", "guesser_monitor"),
                ("mimeTerminal", "mime_terminal"),
                ("generatorAnchor", "generator_particle_system"),
                ("generatorButton", "geneartor_button"),
                ("generatorParticles", "generator_particle_system"),
                ("smallSizeButton", "SmallButton"),
                ("mediumSizeButton", "MediumButton"),
                ("extraLargeSizeButton", "LargeButton")
            };
            foreach (var (field, objectName) in references)
            {
                var reference = serialized.FindProperty(field).objectReferenceValue;
                var target = reference is Component component
                    ? component.gameObject : (GameObject)reference;
                Assert.That(target, Is.SameAs(transforms.Single(item =>
                    item.name == objectName).gameObject), field);
            }

            var monitor = (Transform)serialized.FindProperty("guesserMonitor")
                .objectReferenceValue;
            var originalName = monitor.name;
            try
            {
                monitor.name = "Manually renamed monitor";
                MeshupGameSceneValidation.Validate(scene);
                Assert.That(serialized.FindProperty("guesserMonitor")
                    .objectReferenceValue, Is.SameAs(monitor));
            }
            finally
            {
                monitor.name = originalName;
            }
        }

        [Test]
        public void SavedGameDoesNotAutoplayAuthoredFireworks()
        {
            using var validation = new SceneValidationScope(GameScenePath);
            var fireworks = validation.Scene.GetRootGameObjects()
                .Where(root => root.name.StartsWith("Firework_",
                    System.StringComparison.Ordinal))
                .ToArray();

            Assert.That(fireworks, Is.Not.Empty);
            Assert.That(fireworks.All(firework => !firework.activeSelf), Is.True,
                "Authored firework prefabs must remain inactive; victory "
                + "fireworks are spawned by MeshupVictoryFireworks.");
        }

        [TestCase(GameScenePath)]
        [TestCase(LobbyScenePath)]
        public void AuthoredScenesIncludeAnFpsCounter(string scenePath)
        {
            using var validation = new SceneValidationScope(scenePath);
            var counters = validation.Scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<FpsCounter>(true))
                .ToArray();

            Assert.That(counters, Has.Length.EqualTo(1));
            Assert.That(counters[0].isActiveAndEnabled, Is.True);
        }

        [Test]
        public void QuestBuildUsesVulkanRequiredByVictoryVfx()
        {
            var graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);

            Assert.That(graphicsApis, Is.EqualTo(new[] { GraphicsDeviceType.Vulkan }),
                "The authored VFX Graph fireworks require Vulkan on Quest. "
                + "OpenGL ES does not provide the compute/SSBO path they use.");
        }

        [Test]
        public void LobbyValidationPreservesAnOpenSceneWithUnsavedEdits()
        {
            var scene = EditorSceneManager.OpenScene(LobbyScenePath,
                OpenSceneMode.Single);
            var marker = new GameObject("Unsaved manual scene edit");
            SceneManager.MoveGameObjectToScene(marker, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            try
            {
                LobbyDualModePlayerValidation.Validate();
                Assert.That(scene.isLoaded, Is.True);
                Assert.That(scene.isDirty, Is.True);
                Assert.That(marker != null, Is.True);
                Assert.That(marker.scene, Is.EqualTo(scene));
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(scene));
            }
            finally
            {
                Object.DestroyImmediate(marker);
            }
        }

        [Test]
        public void LobbyHasNoPortalTransitionAndKeepsSpawnManagerInactive()
        {
            using var validation = new SceneValidationScope(LobbyScenePath);
            var roots = validation.Scene.GetRootGameObjects();

            Assert.That(roots.Any(root => root.name == "Lobby Portal Transition"),
                Is.False);

            var networkScene = roots.Single(root => root.name == "Ubiq Network Scene");
            var spawnManager = networkScene.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "Spawn Manager");
            Assert.That(spawnManager.gameObject.activeSelf, Is.False,
                "An active duplicate is destroyed before Start and throws in Ubiq's OnDestroy.");
        }

        [Test]
        public void TemporaryValidationClosesOnlyTheSceneItOpenedEvenOnFailure()
        {
            var lobby = EditorSceneManager.OpenScene(LobbyScenePath,
                OpenSceneMode.Single);
            EditorSceneManager.MarkSceneDirty(lobby);
            var sceneCount = SceneManager.sceneCount;
            Assert.Throws<System.InvalidOperationException>(() =>
            {
                using var validation = new SceneValidationScope(GameScenePath);
                Assert.That(validation.Scene.isLoaded, Is.True);
                throw new System.InvalidOperationException("A validation failed.");
            });
            Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCount));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(lobby));
            Assert.That(lobby.isLoaded && lobby.isDirty, Is.True);
            Assert.That(SceneManager.GetSceneByPath(GameScenePath).isLoaded, Is.False);
        }

        [Test]
        public void CoordinatorTeardownBeforeStartPreservesTheAuthoredWall()
        {
            var root = new GameObject("Uninitialized game runtime");
            var wallObject = new GameObject("Authored wall");
            try
            {
                var wall = wallObject.AddComponent<BoxCollider>();
                var coordinator = root.AddComponent<MeshupGameCoordinator>();
                var serialized = new SerializedObject(coordinator);
                serialized.FindProperty("invisibleWall").objectReferenceValue = wall;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                // Simulate teardown before Start, including an early startup failure.
                typeof(MeshupGameCoordinator).GetMethod("OnDestroy",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(coordinator, null);
                Assert.That(wall.enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(wallObject);
            }
        }
    }
}
