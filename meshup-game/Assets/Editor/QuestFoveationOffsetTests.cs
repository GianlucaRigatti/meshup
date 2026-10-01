using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Meshup.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class QuestFoveationOffsetTests
{
    private static readonly Type FeatureType = typeof(QuestFoveationOffsetFeature);
    private static Delegate getProc, create, update, destroy;
    private static int createResult, updateResult;
    private static LevelInfo receivedLevel;
    private static ProfileInfo receivedInfo;
    private QuestFoveationOffsetFeature feature;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProfileInfo { public int type; public IntPtr next; }
    [StructLayout(LayoutKind.Sequential)]
    private struct LevelInfo
    {
        public int type;
        public IntPtr next;
        public int level;
        public float offset;
        public int dynamic;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SwapchainState
    {
        public int type;
        public IntPtr next;
        public ulong flags;
        public ulong profile;
    }

    [SetUp]
    public void SetUp()
    {
        createResult = updateResult = 0;
        getProc = MakeDelegate("GetProcAddress", nameof(FakeGetProc));
        create = MakeDelegate("CreateProfile", nameof(FakeCreate));
        update = MakeDelegate("UpdateSwapchain", nameof(FakeUpdate));
        destroy = MakeDelegate("DestroyProfile", nameof(FakeDestroy));
        feature = ScriptableObject.CreateInstance<QuestFoveationOffsetFeature>();
        FeatureType.GetMethod("HookGetInstanceProcAddr", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(feature, new object[] { Marshal.GetFunctionPointerForDelegate(getProc) });
    }

    [TearDown]
    public void TearDown() => UnityEngine.Object.DestroyImmediate(feature);

    [Test]
    public void ProfileHookOffsetsRuntimeInputWithoutChangingUnityInput()
    {
        var level = new LevelInfo { type = 1000115000, next = new IntPtr(1234), level = 2, offset = 0, dynamic = 1 };
        var levelPointer = Allocate(level);
        var infoPointer = Allocate(new ProfileInfo { type = 1000114000, next = levelPointer });
        try
        {
            var hook = Resolve("xrCreateFoveationProfileFB", "CreateProfile");
            var args = new object[] { 99UL, infoPointer, 0UL };
            Assert.That(hook.DynamicInvoke(args), Is.EqualTo(0));
            Assert.That(args[2], Is.EqualTo(456UL));
            Assert.That(receivedLevel.offset, Is.EqualTo(10f));
            Assert.That(receivedLevel.level, Is.EqualTo(2));
            Assert.That(receivedLevel.dynamic, Is.EqualTo(1));
            Assert.That(receivedLevel.next, Is.EqualTo(new IntPtr(1234)));
            Assert.That(receivedInfo.type, Is.EqualTo(1000114000));
            Assert.That(Marshal.PtrToStructure<LevelInfo>(levelPointer).offset, Is.Zero);
            Assert.That(Marshal.PtrToStructure<ProfileInfo>(infoPointer).next, Is.EqualTo(levelPointer));
            Assert.That(Counter("createdProfiles"), Is.EqualTo(1));
            Assert.That(Counter("appliedProfiles"), Is.Zero);
        }
        finally { Marshal.FreeHGlobal(infoPointer); Marshal.FreeHGlobal(levelPointer); }
    }

    [Test]
    public void ApplicationIsConfirmedOnlyAfterRuntimeAcceptsMatchingSwapchainProfile()
    {
        CreateOffsetProfile();
        var hook = Resolve("xrUpdateSwapchainFB", "UpdateSwapchain");
        var state = Allocate(new SwapchainState { type = 1000114002, profile = 999 });
        try
        {
            hook.DynamicInvoke(123UL, state);
            Assert.That(Counter("appliedProfiles"), Is.Zero);
            Marshal.StructureToPtr(new SwapchainState { type = 1000114002, profile = 456 }, state, false);
            updateResult = -2;
            LogAssert.Expect(LogType.Error, "[QuestFoveation] Runtime rejected swapchain foveation update: result=-2.");
            Assert.That(hook.DynamicInvoke(123UL, state), Is.EqualTo(-2));
            Assert.That(Counter("appliedProfiles"), Is.Zero);
            updateResult = 0;
            Assert.That(hook.DynamicInvoke(123UL, state), Is.EqualTo(0));
            Assert.That(Counter("appliedProfiles"), Is.EqualTo(1));
            Resolve("xrDestroyFoveationProfileFB", "DestroyProfile").DynamicInvoke(456UL);
            hook.DynamicInvoke(123UL, state);
            Assert.That(Counter("appliedProfiles"), Is.EqualTo(1));
        }
        finally { Marshal.FreeHGlobal(state); }
    }

    [Test]
    public void DisabledFoveationIsNotRecordedAsAnOffsetRenderingProfile()
    {
        var level = Allocate(new LevelInfo { type = 1000115000, level = 0 });
        var info = Allocate(new ProfileInfo { type = 1000114000, next = level });
        try
        {
            Resolve("xrCreateFoveationProfileFB", "CreateProfile").DynamicInvoke(new object[] { 99UL, info, 0UL });
            Assert.That(Counter("createdProfiles"), Is.Zero);
        }
        finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(level); }
    }

    [Test]
    public void RejectedProfileIsNotRecordedAsAccepted()
    {
        createResult = -2;
        LogAssert.Expect(LogType.Error, "[QuestFoveation] Runtime rejected offset profile: result=-2.");
        CreateOffsetProfile();
        Assert.That(Counter("createdProfiles"), Is.Zero);
    }

    [Test]
    public void ProfileWithoutLevelConfigurationIsForwardedAndReported()
    {
        var info = Allocate(new ProfileInfo { type = 1000114000 });
        try
        {
            LogAssert.Expect(LogType.Warning, "[QuestFoveation] Profile has no leading fixed-foveation level configuration; offset was not changed.");
            var args = new object[] { 99UL, info, 0UL };
            Resolve("xrCreateFoveationProfileFB", "CreateProfile").DynamicInvoke(args);
            Assert.That(receivedInfo.next, Is.EqualTo(IntPtr.Zero));
            Assert.That(Counter("createdProfiles"), Is.Zero);
        }
        finally { Marshal.FreeHGlobal(info); }
    }

    private static void CreateOffsetProfile()
    {
        var level = Allocate(new LevelInfo { type = 1000115000, level = 2 });
        var info = Allocate(new ProfileInfo { type = 1000114000, next = level });
        try { Resolve("xrCreateFoveationProfileFB", "CreateProfile").DynamicInvoke(new object[] { 99UL, info, 0UL }); }
        finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(level); }
    }

    private static Delegate Resolve(string name, string delegateName)
    {
        var namePointer = Marshal.StringToHGlobalAnsi(name);
        try
        {
            var args = new object[] { 1UL, namePointer, IntPtr.Zero };
            FeatureType.GetMethod("InterceptGetProcAddress", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
            return Marshal.GetDelegateForFunctionPointer((IntPtr)args[2], FeatureType.GetNestedType(delegateName, BindingFlags.NonPublic));
        }
        finally { Marshal.FreeHGlobal(namePointer); }
    }

    private static Delegate MakeDelegate(string type, string method) => Delegate.CreateDelegate(
        FeatureType.GetNestedType(type, BindingFlags.NonPublic),
        typeof(QuestFoveationOffsetTests).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic));
    private static int Counter(string name) => (int)FeatureType.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static IntPtr Allocate<T>(T value) where T : struct
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(value, pointer, false);
        return pointer;
    }

    private static int FakeGetProc(ulong instance, IntPtr name, out IntPtr function)
    {
        var functionName = Marshal.PtrToStringAnsi(name);
        var target = functionName == "xrCreateFoveationProfileFB" ? create : functionName == "xrUpdateSwapchainFB" ? update : destroy;
        function = Marshal.GetFunctionPointerForDelegate(target);
        return 0;
    }
    private static int FakeCreate(ulong session, IntPtr info, out ulong profile)
    {
        receivedInfo = Marshal.PtrToStructure<ProfileInfo>(info);
        if (receivedInfo.next != IntPtr.Zero) receivedLevel = Marshal.PtrToStructure<LevelInfo>(receivedInfo.next);
        profile = createResult >= 0 ? 456UL : 0UL;
        return createResult;
    }
    private static int FakeUpdate(ulong swapchain, IntPtr state) => updateResult;
    private static int FakeDestroy(ulong profile) => 0;
}
