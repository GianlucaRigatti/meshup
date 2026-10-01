using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

public sealed class QuestControllerConfigurationTests
{
    [TestCase(BuildTargetGroup.Android)]
    [TestCase(BuildTargetGroup.Standalone)]
    public void QuestControllerProfilesRemainEnabled(BuildTargetGroup target)
    {
        var settings = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/XR/Settings/OpenXR Package Settings.asset")
            .OfType<OpenXRSettings>().SingleOrDefault(item => item.name == target.ToString());
        Assert.That(settings, Is.Not.Null, $"Missing OpenXR settings for {target}.");

        // These profiles register controller poses as well as button bindings.
        // Rendering feature changes must not remove Quest controller input.
        Assert.That(settings.GetFeature<OculusTouchControllerProfile>()?.enabled,
            Is.True, $"Oculus Touch input is disabled for {target}.");
        Assert.That(settings.GetFeature<MetaQuestTouchPlusControllerProfile>()?.enabled,
            Is.True, $"Quest Touch Plus input is disabled for {target}.");
        Assert.That(settings.GetFeature<MetaQuestTouchProControllerProfile>()?.enabled,
            Is.True, $"Quest Touch Pro input is disabled for {target}.");
    }
}
