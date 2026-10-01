using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Meshup.Game
{
#if UNITY_EDITOR
    [UnityEditor.XR.OpenXR.Features.OpenXRFeature(
        UiName = "Meshup Quest 120 Hz SpaceWarp",
        Desc = "Requests a 120 Hz display and activates Application SpaceWarp at session start.",
        Company = "Meshup",
        Version = "1.0.0",
        FeatureId = "com.meshup.openxr.quest-spacewarp",
        OpenxrExtensionStrings = "XR_FB_display_refresh_rate",
        BuildTargetGroups = new[] { UnityEditor.BuildTargetGroup.Android })]
#endif
    public sealed class QuestSpaceWarpFeature : OpenXRFeature
    {
        private const float DisplayRefreshRate = 120f;
        private RequestDisplayRefreshRate requestDisplayRefreshRate;

        public bool SpaceWarpActive { get; private set; }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetInstanceProcAddr(ulong instance,
            [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr function);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int RequestDisplayRefreshRate(ulong session, float rate);

        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            requestDisplayRefreshRate = null;
            SpaceWarpActive = false;
            if (OpenXRRuntime.IsExtensionEnabled("XR_FB_display_refresh_rate"))
            {
                var getProcAddr = Marshal.GetDelegateForFunctionPointer<GetInstanceProcAddr>(
                    xrGetInstanceProcAddr);
                if (getProcAddr(xrInstance, "xrRequestDisplayRefreshRateFB", out var function) == 0
                    && function != IntPtr.Zero)
                {
                    requestDisplayRefreshRate =
                        Marshal.GetDelegateForFunctionPointer<RequestDisplayRefreshRate>(function);
                }
            }

            // Refresh-rate control is optional; SpaceWarp can still run at the current rate.
            return true;
        }

        protected override void OnSessionBegin(ulong xrSession)
        {
            if (requestDisplayRefreshRate == null)
            {
                Debug.LogWarning("120 Hz refresh-rate control is unavailable. Keeping the headset's current refresh rate.");
            }
            else
            {
                var result = requestDisplayRefreshRate(xrSession, DisplayRefreshRate);
                if (result == 0)
                    Debug.Log("Requested 120 Hz display refresh rate (60 rendered FPS with SpaceWarp).");
                else
                    Debug.LogWarning($"120 Hz display request failed (OpenXR result {result}). Keeping the headset's current refresh rate.");
            }

            var spaceWarp = OpenXRSettings.Instance.GetFeature<SpaceWarpFeature>();
            if (spaceWarp != null && spaceWarp.enabled
                && OpenXRRuntime.IsExtensionEnabled(SpaceWarpFeature.k_OpenXRRequestedExtensions))
            {
                SpaceWarpActive = SpaceWarpFeature.SetSpaceWarp(true);
            }

            if (SpaceWarpActive)
                Debug.Log("Application SpaceWarp enabled.");
            else
                Debug.LogWarning("Application SpaceWarp could not be enabled for this XR session.");
        }

        protected override void OnSessionEnd(ulong xrSession)
        {
            if (SpaceWarpActive)
                SpaceWarpFeature.SetSpaceWarp(false);
            SpaceWarpActive = false;
        }

        protected override void OnInstanceDestroy(ulong xrInstance)
        {
            SpaceWarpActive = false;
            requestDisplayRefreshRate = null;
        }
    }
}
