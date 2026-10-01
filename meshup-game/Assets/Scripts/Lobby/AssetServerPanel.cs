using System;
using System.Collections;
using Meshup.Game;
using Ubiq.Samples;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Meshup.Lobby
{
    public sealed class AssetServerPanel : MonoBehaviour
    {
        [SerializeField] private PanelSwitcher panelSwitcher;
        [SerializeField] private GameObject ipPanel;
        [SerializeField] private GameObject portPanel;
        [SerializeField] private TextEntry ipEntry;
        [SerializeField] private TextEntry portEntry;
        [SerializeField] private Text ipMessage;
        [SerializeField] private Text portMessage;
        [SerializeField] private Text healthIndicator;
        private Coroutine healthCheck;
        private UnityWebRequest activeRequest;
        private string scheme = "http";
        private static readonly Color Checking = new(1f, 0.7f, 0.15f);
        private static readonly Color Ready = new(0.2f, 0.85f, 0.35f);
        private static readonly Color Unavailable = new(0.95f, 0.25f, 0.25f);

        private void OnEnable() => RestartHealthCheck();

        public void Open()
        {
            if (!Uri.TryCreate(AssetServerSettings.Resolve(), UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https"))
                uri = new Uri(AssetServerSettings.DefaultBaseUrl);
            scheme = uri.Scheme;
            SetEntry(ipEntry, uri.Host.Trim('[', ']'));
            SetEntry(portEntry, uri.Port.ToString());
            ipMessage.text = "Server IP address (1/2)";
            portMessage.text = "Server port (2/2)";
            panelSwitcher.SwitchPanel(ipPanel);
        }

        private static void SetEntry(TextEntry entry, string value)
        {
            // TextEntry.Start also initializes the value when first shown.
            entry.defaultText = value;
            entry.defaultTextColor = entry.entryTextColor;
            entry.SetText(value, entry.entryTextColor, true);
        }

        public void EnterPeriod() => ipEntry.Enter('.');

        public void Next()
        {
            if (!AssetServerSettings.TryBuildUrl(ipEntry.text.text, "8000", scheme,
                    out _, out var error))
            {
                ipMessage.text = error;
                return;
            }
            panelSwitcher.SwitchPanel(portPanel);
        }

        public void BackToIp() => panelSwitcher.SwitchPanel(ipPanel);

        public void Save()
        {
            if (!AssetServerSettings.TryBuildUrl(ipEntry.text.text, portEntry.text.text,
                    scheme, out var url, out var error))
            {
                portMessage.text = error;
                return;
            }
            AssetServerSettings.Save(url);
            panelSwitcher.SwitchPanelToDefault();
            RestartHealthCheck();
        }

        private void RestartHealthCheck()
        {
            StopHealthCheck();
            healthIndicator.color = Checking;
            healthCheck = StartCoroutine(CheckHealth());
        }

        private IEnumerator CheckHealth()
        {
            while (true)
            {
                using (var request = UnityWebRequest.Get(
                    AssetServerSettings.Resolve().TrimEnd('/') + "/readyz"))
                {
                    activeRequest = request;
                    request.timeout = 5;
                    yield return request.SendWebRequest();
                    healthIndicator.color = request.result == UnityWebRequest.Result.Success
                        ? Ready : Unavailable;
                    activeRequest = null;
                }
                yield return new WaitForSecondsRealtime(10f);
            }
        }

        private void StopHealthCheck()
        {
            activeRequest?.Abort();
            if (healthCheck != null) StopCoroutine(healthCheck);
            activeRequest?.Dispose();
            activeRequest = null;
            healthCheck = null;
        }

        private void OnDisable() => StopHealthCheck();
    }
}
