using System.Runtime.InteropServices;
using UnityEngine.XR.OpenXR.Features;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace Meshup.Game
{
#if UNITY_EDITOR
    [OpenXRFeature(
        UiName = "Quest Foveation Vertical Offset",
        BuildTargetGroups = new[] { BuildTargetGroup.Android },
        Company = "Meshup",
        Desc = "Moves the fixed foveation center slightly upward on Quest.",
        DocumentationLink = "",
        OpenxrExtensionStrings = "XR_FB_foveation XR_FB_foveation_configuration XR_FB_swapchain_update_state XR_FB_foveation_vulkan",
        Version = "1.0.0",
        FeatureId = FeatureId)]
#endif
    public sealed class QuestFoveationOffsetFeature : OpenXRFeature
    {
        public const string FeatureId = "com.meshup.openxr.questfoveationoffset";

        // Degrees. A positive offset moves the full-resolution center upward.
        private const float VerticalOffsetDegrees = 3f;
        private const uint MediumFoveationLevel = 2;
        private ulong session;

        protected override void OnSessionCreate(ulong xrSession)
        {
            session = xrSession;
        }

        protected override void OnSessionDestroy(ulong xrSession)
        {
            session = 0;
        }

        public void ApplyVerticalOffset()
        {
            if (session != 0)
            {
                FBSetFoveationLevel(session, MediumFoveationLevel, VerticalOffsetDegrees, 0);
            }
        }

        [DllImport("UnityOpenXR", EntryPoint = "FBSetFoveationLevel")]
        private static extern void FBSetFoveationLevel(
            ulong xrSession, uint level, float verticalOffset, uint dynamic);
    }
}
