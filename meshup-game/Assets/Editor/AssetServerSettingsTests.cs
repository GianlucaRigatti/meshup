using Meshup.Game;
using Meshup.Lobby;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Meshup.Editor.Tests
{
    public sealed class AssetServerSettingsTests
    {
        [TestCase("192.168.1.42", "8000", "http", "http://192.168.1.42:8000")]
        [TestCase("127.0.0.1", "65535", "https", "https://127.0.0.1:65535")]
        [TestCase("::1", "8000", "http", "http://[::1]:8000")]
        public void BuildsServerEndpoint(string ip, string port, string scheme, string expected)
        {
            Assert.That(AssetServerSettings.TryBuildUrl(ip, port, scheme,
                out var url, out var error), Is.True, error);
            Assert.That(url, Is.EqualTo(expected));
        }

        [TestCase("bad-ip", "8000")]
        [TestCase("127.0.0.1", "0")]
        [TestCase("127.0.0.1", "65536")]
        [TestCase("127.0.0.1", "abc")]
        public void RejectsInvalidEndpoint(string ip, string port)
        {
            Assert.That(AssetServerSettings.TryBuildUrl(ip, port, "http",
                out _, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void SavedEndpointOverridesBuildConfiguration()
        {
            var existed = PlayerPrefs.HasKey(AssetServerSettings.PreferenceKey);
            var original = PlayerPrefs.GetString(AssetServerSettings.PreferenceKey);
            try
            {
                AssetServerSettings.Save("http://192.168.1.42:9876");
                Assert.That(AssetServerSettings.Resolve(), Is.EqualTo("http://192.168.1.42:9876"));
            }
            finally
            {
                if (existed) PlayerPrefs.SetString(AssetServerSettings.PreferenceKey, original);
                else PlayerPrefs.DeleteKey(AssetServerSettings.PreferenceKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void AuthoredMenuHasSettingsReferencesAndNonBlockingIndicator()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Ubiq Sample UI/Menu.prefab");
            try
            {
                var settings = root.GetComponentInChildren<AssetServerPanel>(true);
                Assert.That(settings, Is.Not.Null);
                var serialized = new SerializedObject(settings);
                foreach (var field in new[] { "panelSwitcher", "ipPanel", "portPanel", "ipEntry",
                    "portEntry", "ipMessage", "portMessage", "healthIndicator" })
                    Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
                var indicator = (Text)serialized.FindProperty("healthIndicator").objectReferenceValue;
                Assert.That(indicator.raycastTarget, Is.False);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
