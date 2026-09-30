using System.Linq;
using Meshup.EditorTools;
using Meshup.Game;
using Meshup.Multiplayer;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ubiq.Voip;

namespace Meshup.Editor.Tests
{
    public sealed class AuthoredSceneTests
    {
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string LobbyScenePath = "Assets/Scenes/SampleScene.unity";

        [Test]
        public void MimeZoneDividerDoesNotBlockTheOpening()
        {
            using var validation = new SceneValidationScope(GameScenePath);
            var coordinator = validation.Scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MeshupGameCoordinator>(true)).Single();
            var divider = (Transform)new SerializedObject(coordinator)
                .FindProperty("mimeZoneDivider").objectReferenceValue;

            Assert.That(divider.GetComponent<Renderer>(), Is.Not.Null);
            Assert.That(divider.GetComponents<Collider>(), Is.Empty);
        }

        [Test]
        public void MonitorUiMountsFitInsideTheAuthoredScreen()
        {
            using var validation = new SceneValidationScope(GameScenePath);
            var view = validation.Scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MeshupGameView>(true)).Single();
            var serialized = new SerializedObject(view);
            var canvas = (Transform)serialized.FindProperty("monitorCanvas").objectReferenceValue;
            var size = canvas.GetComponent<RectTransform>().sizeDelta;
            foreach (var field in new[] { "monitorFrontMount", "monitorBackMount" })
            {
                var mount = (Transform)serialized.FindProperty(field)
                    .objectReferenceValue;
                var screen = mount.parent.GetComponent<Renderer>();
                Assert.That(screen, Is.Not.Null, field);
                Assert.That(mount.lossyScale.x * size.x,
                    Is.LessThan(screen.bounds.size.x), field + " width");
                Assert.That(mount.lossyScale.y * size.y,
                    Is.LessThan(screen.bounds.size.y), field + " height");
            }
        }

        [Test]
        public void SavedGameReferencesSurvivePropRenames()
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
            var fireworks = (MeshupVictoryFireworks)serialized.FindProperty("victoryFireworks").objectReferenceValue;
            var props = new[] { "gameStart", "localPlayer", "invisibleWall",
                    "mimeZoneDivider", "generatorAnchor", "generatorParticles" }
                .Select(field => ((Component)serialized.FindProperty(field).objectReferenceValue).gameObject)
                .Append(((Transform)new SerializedObject(fireworks)
                    .FindProperty("guesserScreen").objectReferenceValue).gameObject)
                .Distinct().ToArray();
            var names = props.Select(prop => prop.name).ToArray();
            try
            {
                for (var index = 0; index < props.Length; index++)
                    props[index].name = $"Renamed prop {index}";
                MeshupGameSceneValidation.Validate(scene);
            }
            finally
            {
                for (var index = 0; index < props.Length; index++) props[index].name = names[index];
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

            Assert.That(fireworks, Is.Empty,
                "Victory fireworks are spawned from saved particle prefabs at runtime.");
        }

        [Test]
        public void GameSceneFishAndBubblesUseTheSamePlayerAreaVolumes()
        {
            using var validation = new SceneValidationScope(GameScenePath);
            var roots = validation.Scene.GetRootGameObjects();
            var school = roots.SelectMany(root => root.GetComponentsInChildren<
                FishSchoolController>(true)).Single();
            var playerArea = school.GetComponent<PlayerAreaVolumes>();
            var spawner = roots.SelectMany(root => root.GetComponentsInChildren<
                TerrainBubbleSpawner>(true)).Single();

            Assert.That(playerArea, Is.Not.Null);
            Assert.That(new SerializedObject(spawner).FindProperty("playerArea")
                .objectReferenceValue, Is.SameAs(playerArea));
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
        public void LobbyKeepsRoomVoiceAndAnInactiveSpawnManager()
        {
            using var validation = new SceneValidationScope(LobbyScenePath);
            var roots = validation.Scene.GetRootGameObjects();

            var session = roots.SelectMany(root => root.GetComponentsInChildren<UbiqRoomSession>(true)).Single();
            Assert.That(session.GetComponentInChildren<VoipPeerConnectionManager>(true), Is.Not.Null);
            var spawnManager = session.GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "Spawn Manager");
            Assert.That(spawnManager.gameObject.activeSelf, Is.False,
                "An active duplicate is destroyed before Start and throws in Ubiq's OnDestroy.");
        }

        [Test]
        public void BuildAllowsMicrophoneCaptureAndAssetDownloads()
        {
            Assert.That(PlayerSettings.WSA.GetCapability(
                PlayerSettings.WSACapability.Microphone), Is.True);
            Assert.That(PlayerSettings.insecureHttpOption,
                Is.EqualTo(InsecureHttpOption.AlwaysAllowed));
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

    }
}
