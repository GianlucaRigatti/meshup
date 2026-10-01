using Meshup.Game;
using NUnit.Framework;
using UnityEngine;

namespace Meshup.Editor.Tests
{
    public sealed class XRSpaceWarpControllerTests
    {
        private GameObject origin;
        private Transform trackingSpace;
        private Camera camera;

        [SetUp]
        public void SetUp()
        {
            origin = new GameObject("XR Origin");
            trackingSpace = new GameObject("Camera Offset").transform;
            trackingSpace.SetParent(origin.transform, false);
            trackingSpace.localPosition = Vector3.up * 1.36f;
            camera = new GameObject("Tracked Camera").AddComponent<Camera>();
            camera.transform.SetParent(trackingSpace, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (camera != null)
                Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(origin);
        }

        [Test]
        public void HeadTrackingDoesNotMoveAppSpace()
        {
            var before = XRSpaceWarpController.GetAppSpacePose(camera);
            camera.transform.localPosition = new Vector3(0.3f, 0.2f, -0.5f);
            camera.transform.localRotation = Quaternion.Euler(20f, 70f, 5f);

            AssertPose(XRSpaceWarpController.GetAppSpacePose(camera), before);
        }

        [Test]
        public void LocomotionAndFloorOffsetMoveAppSpace()
        {
            origin.transform.SetPositionAndRotation(new Vector3(12f, 2f, -8f),
                Quaternion.Euler(0f, 90f, 0f));
            camera.transform.localPosition = new Vector3(0.4f, 0.8f, 0.2f);
            camera.transform.localRotation = Quaternion.Euler(10f, 30f, 0f);

            AssertPose(XRSpaceWarpController.GetAppSpacePose(camera),
                new Pose(new Vector3(12f, 3.36f, -8f), Quaternion.Euler(0f, 90f, 0f)));
        }

        [Test]
        public void UnparentedTrackedCameraUsesIdentityAppSpace()
        {
            camera.transform.SetParent(null);
            camera.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f),
                Quaternion.Euler(15f, 60f, 0f));

            AssertPose(XRSpaceWarpController.GetAppSpacePose(camera), Pose.identity);
        }

        private static void AssertPose(Pose actual, Pose expected)
        {
            Assert.That(Vector3.Distance(actual.position, expected.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(actual.rotation, expected.rotation), Is.LessThan(0.001f));
        }
    }
}
