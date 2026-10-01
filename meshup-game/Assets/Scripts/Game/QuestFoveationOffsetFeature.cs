using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;
using UnityEngine;
using UnityEngine.XR.OpenXR;
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
        Desc = "Moves the fixed foveation center upward on Quest.",
        DocumentationLink = "",
        OpenxrExtensionStrings = "XR_FB_foveation XR_FB_foveation_configuration XR_FB_swapchain_update_state XR_FB_foveation_vulkan",
        Version = "1.0.0",
        Priority = -100,
        FeatureId = FeatureId)]
#endif
    public sealed class QuestFoveationOffsetFeature : OpenXRFeature
    {
        public const string FeatureId = "com.meshup.openxr.questfoveationoffset";
        private const string LogPrefix = "[QuestFoveation]";
        // Degrees. Positive moves the full-resolution center upward.
        private const float VerticalOffsetDegrees = 10f;
        private const int LevelProfileType = 1000115000;
        private const int SwapchainFoveationType = 1000114002;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetProcAddress(ulong instance, IntPtr name, out IntPtr function);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CreateProfile(ulong session, IntPtr info, out ulong profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int UpdateSwapchain(ulong swapchain, IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DestroyProfile(ulong profile);

        // Keep reverse-P/Invoke delegates alive for the lifetime of the native hooks.
        private static readonly GetProcAddress GetProcHook = InterceptGetProcAddress;
        private static readonly CreateProfile CreateHook = InterceptCreateProfile;
        private static readonly UpdateSwapchain UpdateHook = InterceptUpdateSwapchain;
        private static readonly DestroyProfile DestroyHook = InterceptDestroyProfile;
        private static GetProcAddress originalGetProc;
        private static CreateProfile originalCreate;
        private static UpdateSwapchain originalUpdate;
        private static DestroyProfile originalDestroy;
        private static readonly HashSet<ulong> OffsetProfiles = new();
        private static int createdProfiles;
        private static int appliedProfiles;
        private static int unsupportedChainReported;

        protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
        {
            originalGetProc = Marshal.GetDelegateForFunctionPointer<GetProcAddress>(func);
            originalCreate = null;
            originalUpdate = null;
            originalDestroy = null;
            lock (OffsetProfiles) OffsetProfiles.Clear();
            createdProfiles = appliedProfiles = unsupportedChainReported = 0;
            // Lower priority puts this hook underneath Unity's foveation hook, so it
            // sees profiles created by Unity's renderer as well as application calls.
            return Marshal.GetFunctionPointerForDelegate(GetProcHook);
        }

        protected override bool OnInstanceCreate(ulong instance)
        {
            var supported = OpenXRRuntime.IsExtensionEnabled("XR_FB_foveation_configuration");
            Debug.Log($"{LogPrefix} Profile hook initialized; configuration extension enabled={supported}; requested offset={VerticalOffsetDegrees} degrees.");
            return supported;
        }

        [MonoPInvokeCallback(typeof(GetProcAddress))]
        private static int InterceptGetProcAddress(ulong instance, IntPtr name, out IntPtr function)
        {
            var result = originalGetProc(instance, name, out function);
            if (result < 0 || function == IntPtr.Zero) return result;
            switch (Marshal.PtrToStringAnsi(name))
            {
                case "xrCreateFoveationProfileFB":
                    originalCreate = Marshal.GetDelegateForFunctionPointer<CreateProfile>(function);
                    function = Marshal.GetFunctionPointerForDelegate(CreateHook);
                    break;
                case "xrUpdateSwapchainFB":
                    originalUpdate = Marshal.GetDelegateForFunctionPointer<UpdateSwapchain>(function);
                    function = Marshal.GetFunctionPointerForDelegate(UpdateHook);
                    break;
                case "xrDestroyFoveationProfileFB":
                    originalDestroy = Marshal.GetDelegateForFunctionPointer<DestroyProfile>(function);
                    function = Marshal.GetFunctionPointerForDelegate(DestroyHook);
                    break;
            }
            return result;
        }

        [MonoPInvokeCallback(typeof(CreateProfile))]
        private static int InterceptCreateProfile(ulong session, IntPtr info, out ulong profile)
        {
            var createInfo = Marshal.PtrToStructure<ProfileCreateInfo>(info);
            // Unity supplies the level configuration as the first next-chain node.
            // Leave unknown chains intact and report them instead of rewriting them.
            if (createInfo.next == IntPtr.Zero || Marshal.ReadInt32(createInfo.next) != LevelProfileType)
            {
                if (Interlocked.Exchange(ref unsupportedChainReported, 1) == 0)
                    Debug.LogWarning($"{LogPrefix} Profile has no leading fixed-foveation level configuration; offset was not changed.");
                return originalCreate(session, info, out profile);
            }

            var levelInfo = Marshal.PtrToStructure<LevelProfileCreateInfo>(createInfo.next);
            levelInfo.verticalOffset = VerticalOffsetDegrees;
            var levelCopy = Marshal.AllocHGlobal(Marshal.SizeOf<LevelProfileCreateInfo>());
            var infoCopy = Marshal.AllocHGlobal(Marshal.SizeOf<ProfileCreateInfo>());
            try
            {
                // Copy both structures: never mutate Unity's caller-owned memory.
                Marshal.StructureToPtr(levelInfo, levelCopy, false);
                createInfo.next = levelCopy;
                Marshal.StructureToPtr(createInfo, infoCopy, false);
                var result = originalCreate(session, infoCopy, out profile);
                if (result >= 0 && levelInfo.level > 0)
                {
                    lock (OffsetProfiles) OffsetProfiles.Add(profile);
                    if (Interlocked.Increment(ref createdProfiles) == 1)
                        Debug.Log($"{LogPrefix} Runtime accepted profile {profile}: level={levelInfo.level}, verticalOffset={levelInfo.verticalOffset} degrees, result={result}.");
                }
                else if (result < 0)
                    Debug.LogError($"{LogPrefix} Runtime rejected offset profile: result={result}.");
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(infoCopy);
                Marshal.FreeHGlobal(levelCopy);
            }
        }

        [MonoPInvokeCallback(typeof(UpdateSwapchain))]
        private static int InterceptUpdateSwapchain(ulong swapchain, IntPtr state)
        {
            var result = originalUpdate(swapchain, state);
            if (state == IntPtr.Zero || Marshal.ReadInt32(state) != SwapchainFoveationType)
                return result;
            var foveation = Marshal.PtrToStructure<SwapchainFoveationState>(state);
            bool hasOffset;
            lock (OffsetProfiles) hasOffset = OffsetProfiles.Contains(foveation.profile);
            if (hasOffset)
            {
                if (result >= 0)
                {
                    if (Interlocked.Increment(ref appliedProfiles) == 1)
                        Debug.Log($"{LogPrefix} Runtime applied offset profile {foveation.profile} to swapchain {swapchain}: verticalOffset={VerticalOffsetDegrees} degrees, result={result}.");
                }
                else
                    Debug.LogError($"{LogPrefix} Runtime rejected swapchain foveation update: result={result}.");
            }
            return result;
        }

        [MonoPInvokeCallback(typeof(DestroyProfile))]
        private static int InterceptDestroyProfile(ulong profile)
        {
            var result = originalDestroy(profile);
            if (result >= 0)
                lock (OffsetProfiles) OffsetProfiles.Remove(profile);
            return result;
        }

        public void LogStatus()
        {
            var created = Volatile.Read(ref createdProfiles);
            var applied = Volatile.Read(ref appliedProfiles);
            var status = $"{LogPrefix} Offset={VerticalOffsetDegrees} degrees; accepted profiles={created}; accepted swapchain updates={applied}.";
            if (applied == 0) Debug.LogWarning(status + " Offset application is NOT confirmed.");
            else Debug.Log(status);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProfileCreateInfo
        {
            public int type;
            public IntPtr next;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LevelProfileCreateInfo
        {
            public int type;
            public IntPtr next;
            public int level;
            public float verticalOffset;
            public int dynamic;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SwapchainFoveationState
        {
            public int type;
            public IntPtr next;
            public ulong flags;
            public ulong profile;
        }
    }
}
