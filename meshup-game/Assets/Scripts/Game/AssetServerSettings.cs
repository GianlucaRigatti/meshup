using System;
using System.Net;
using UnityEngine;

namespace Meshup.Game
{
    public static class AssetServerSettings
    {
        public const string PreferenceKey = "Meshup.AssetServerBaseUrl";
        public const string DefaultBaseUrl = "http://127.0.0.1:8000";

        [Serializable]
        private sealed class Configuration
        {
            public string assetServerBaseUrl;
        }

        public static string Resolve(string fallback = DefaultBaseUrl)
        {
            var saved = PlayerPrefs.GetString(PreferenceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(saved)) return saved;
            var config = Resources.Load<TextAsset>("Game/meshup_game_config");
            if (config == null) return fallback;
            try
            {
                var value = JsonUtility.FromJson<Configuration>(config.text);
                return string.IsNullOrWhiteSpace(value?.assetServerBaseUrl)
                    ? fallback : value.assetServerBaseUrl.Trim();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[MeshUp] Invalid game configuration: " + exception.Message);
                return fallback;
            }
        }

        public static bool TryBuildUrl(string ip, string port, string scheme,
            out string url, out string error)
        {
            url = string.Empty;
            if (!IPAddress.TryParse(ip?.Trim(), out var address))
            {
                error = "Enter a valid server IP address.";
                return false;
            }
            if (!int.TryParse(port, out var number) || number < 1 || number > 65535)
            {
                error = "Enter a port between 1 and 65535.";
                return false;
            }
            url = new UriBuilder(scheme == "https" ? "https" : "http",
                address.ToString(), number).Uri.GetLeftPart(UriPartial.Authority);
            error = string.Empty;
            return true;
        }

        public static void Save(string url)
        {
            PlayerPrefs.SetString(PreferenceKey, url);
            PlayerPrefs.Save();
        }
    }
}
