using System.Linq;
using System.Reflection;
using Meshup.EditorTools;
using Meshup.Game;
using Meshup.Lobby;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class LobbyTrackedSpawnTests
    {
        private GameObject root;
        private Transform camera;
        private CharacterController controller;
        private LobbyTrackedSpawn spawn;
        private readonly Vector3 target = new(0f, 0.02f, -1.5f);

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Tracked lobby spawn test");
            root.SetActive(false);
            root.transform.position = target;
            var origin = root.AddComponent<XROrigin>();
            origin.Origin = root;
            camera = new GameObject("Tracked camera", typeof(Camera)).transform;
            camera.SetParent(root.transform, false);
            camera.localPosition = new Vector3(4f, 1.7f, -3f);
            origin.Camera = camera.GetComponent<Camera>();
            controller = root.AddComponent<CharacterController>();
            spawn = root.AddComponent<LobbyTrackedSpawn>();
            typeof(LobbyTrackedSpawn).GetMethod("Awake",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(spawn, null);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        public void LargeTrackedOffsetLandsAtSpawnWithoutChangingEyeHeight(float yaw)
        {
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // Gravity or movement can change the origin while tracking initializes.
            root.transform.position += new Vector3(0.5f, -0.2f, 1f);
            var eyeOffset = camera.position.y - root.transform.position.y;
            var cameraLocalPosition = camera.localPosition;

            Tick(true);
            Tick(true);

            Assert.That(Vector3.Distance(PlayerMovementAuthority.GetBodyPosition(
                root.transform, camera), target), Is.LessThan(0.0001f));
            Assert.That(camera.position.y, Is.EqualTo(target.y + eyeOffset).Within(0.0001f));
            Assert.That(camera.localPosition, Is.EqualTo(cameraLocalPosition));
            Assert.That(Quaternion.Angle(root.transform.rotation,
                Quaternion.Euler(0f, yaw, 0f)), Is.LessThan(0.001f));
            Assert.That(controller.enabled, Is.True);
            Assert.That(controller.center.x, Is.EqualTo(cameraLocalPosition.x).Within(0.0001f));
            Assert.That(controller.center.z, Is.EqualTo(cameraLocalPosition.z).Within(0.0001f));
        }

        [Test]
        public void MissingTrackingAndFirstValidFrameDoNotMoveThePlayer()
        {
            for (var i = 0; i < 120; i++)
            {
                Tick(false);
            }
            Tick(true);
            Assert.That(root.transform.position, Is.EqualTo(target));
            // Losing tracking before a complete pose frame restarts the wait.
            Tick(false);
            Tick(true);
            Assert.That(root.transform.position, Is.EqualTo(target));
            Tick(true);
            Assert.That(Vector3.Distance(PlayerMovementAuthority.GetBodyPosition(
                root.transform, camera), target), Is.LessThan(0.0001f));
        }

        [Test]
        public void CompletedSpawnDoesNotUndoWalkingOrTrackingRecovery()
        {
            controller.enabled = false;
            Tick(true);
            Tick(true);
            Assert.That(controller.enabled, Is.False);
            root.transform.position += Vector3.right;
            camera.localPosition += Vector3.forward;
            var positionAfterWalking = root.transform.position;
            Tick(false);
            Tick(true);
            Tick(true);
            Assert.That(root.transform.position, Is.EqualTo(positionAfterWalking));
        }

        [Test]
        public void ReloadedLobbyHasTheSpawnCorrectionOnItsActiveRig()
        {
            // Open twice to check that the correction is authored, not a one-off
            // runtime installation that disappears when returning from a game.
            for (var i = 0; i < 2; i++)
            {
                using var validation = new SceneValidationScope("Assets/Scenes/SampleScene.unity");
                var correction = validation.Scene.GetRootGameObjects()
                    .SelectMany(item => item.GetComponentsInChildren<LobbyTrackedSpawn>(true)).Single();
                Assert.That(correction.enabled, Is.True);
                Assert.That(correction.gameObject.activeInHierarchy, Is.True);
                Assert.That(correction.GetComponent<XROrigin>(), Is.Not.Null);
            }
        }

        private void Tick(bool tracked) => typeof(LobbyTrackedSpawn)
            .GetMethod("TryPlaceAtSpawn", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(spawn, new object[] { tracked });
    }
}
