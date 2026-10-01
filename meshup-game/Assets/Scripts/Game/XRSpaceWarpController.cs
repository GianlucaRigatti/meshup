using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Meshup.Game
{
    public sealed class XRSpaceWarpController : MonoBehaviour
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var controller = new GameObject("XR SpaceWarp Controller");
            DontDestroyOnLoad(controller);
            controller.AddComponent<XRSpaceWarpController>();
        }
#endif

        private void LateUpdate()
        {
            var settings = OpenXRSettings.Instance;
            var feature = settings != null ? settings.GetFeature<QuestSpaceWarpFeature>() : null;
            if (feature == null || !feature.SpaceWarpActive)
                return;

            // Resolve the active camera again after lobby/game scene transitions.
            var camera = Camera.main;
            if (camera == null || !camera.stereoEnabled)
                return;

            var appSpace = GetAppSpacePose(camera);
            SpaceWarpFeature.SetAppSpacePosition(appSpace.position);
            SpaceWarpFeature.SetAppSpaceRotation(appSpace.rotation);
        }

        internal static Pose GetAppSpacePose(Camera camera)
        {
            // OpenXR app-space deltas describe artificial locomotion, not the
            // tracked head pose. Including the camera's local tracking motion
            // compensates for head movement twice and makes the world slide.
            // In our XRI rigs the camera parent is Camera Offset: its world pose
            // includes both the XR Origin's locomotion and the floor offset.
            var trackingSpace = camera.transform.parent;
            return trackingSpace != null
                ? new Pose(trackingSpace.position, trackingSpace.rotation)
                : Pose.identity;
        }
    }
}
