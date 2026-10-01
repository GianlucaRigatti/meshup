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

            SpaceWarpFeature.SetAppSpacePosition(camera.transform.position);
            SpaceWarpFeature.SetAppSpaceRotation(camera.transform.rotation);
        }
    }
}
