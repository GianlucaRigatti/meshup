using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupAssetGeneratorClient : MonoBehaviour
    {
        [Serializable]
        private sealed class GenerateResponse
        {
            public string url;
            public string asset_id;
        }

        private MeshupGameCoordinator coordinator;
        private XRSimpleInteractable interactable;
        private AudioClip recording;
        private string microphoneDevice;
        private bool recordingActive;

        public void Configure(MeshupGameCoordinator owner)
        {
            coordinator = owner;
            interactable = GetComponent<XRSimpleInteractable>()
                ?? gameObject.AddComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(HandlePressed);
            interactable.selectExited.AddListener(HandleReleased);
        }

        public void UploadAuthorized(string requestId, byte[] wav,
            string serverBaseUrl)
        {
            StartCoroutine(Upload(requestId, wav, serverBaseUrl));
        }

        private void HandlePressed(SelectEnterEventArgs args)
        {
            if (recordingActive || coordinator == null
                || !coordinator.CanRecordGeneratorLocally)
            {
                return;
            }
            if (Microphone.devices.Length == 0)
            {
                coordinator.ReportLocalMessage("No microphone is available.");
                return;
            }
            microphoneDevice = Microphone.devices[0];
            recording = Microphone.Start(microphoneDevice, false, 60, 16000);
            recordingActive = recording != null;
            if (recordingActive)
            {
                coordinator.ReportLocalMessage("Recording object description…");
            }
        }

        private void HandleReleased(SelectExitEventArgs args)
        {
            if (!recordingActive)
            {
                return;
            }
            var samplePosition = Microphone.GetPosition(microphoneDevice);
            Microphone.End(microphoneDevice);
            recordingActive = false;
            if (recording == null || samplePosition <= 0)
            {
                coordinator.ReportLocalMessage("No audio was recorded.");
                return;
            }
            var wav = EncodeWav(recording, samplePosition);
            Destroy(recording);
            recording = null;
            coordinator.ReportLocalMessage("Sending description…");
            coordinator.RequestGeneration(wav);
        }

        private IEnumerator Upload(string requestId, byte[] wav,
            string serverBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(serverBaseUrl))
            {
                coordinator.CompleteGeneration(requestId, string.Empty,
                    "The asset server base URL is not configured.");
                yield break;
            }
            var endpoint = serverBaseUrl.TrimEnd('/')
                + "/generate_asset_from_audio";
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                || (endpointUri.Scheme != Uri.UriSchemeHttp
                    && endpointUri.Scheme != Uri.UriSchemeHttps))
            {
                coordinator.CompleteGeneration(requestId, string.Empty,
                    $"Invalid asset server URL: {endpoint}");
                yield break;
            }
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("audio", wav,
                    "description.wav", "audio/wav")
            };
            using var request = UnityWebRequest.Post(endpoint, form);
            request.timeout = 2100;
            UnityWebRequestAsyncOperation operation;
            try
            {
                operation = request.SendWebRequest();
            }
            catch (InvalidOperationException exception)
            {
                coordinator.CompleteGeneration(requestId, string.Empty,
                    exception.Message);
                yield break;
            }
            yield return operation;
            if (request.result != UnityWebRequest.Result.Success)
            {
                coordinator.CompleteGeneration(requestId, string.Empty,
                    request.error ?? "Asset generation failed.");
                yield break;
            }
            GenerateResponse response;
            try
            {
                response = JsonUtility.FromJson<GenerateResponse>(
                    request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                coordinator.CompleteGeneration(requestId, string.Empty,
                    exception.Message);
                yield break;
            }
            if (response == null || string.IsNullOrWhiteSpace(response.url))
            {
                coordinator.CompleteGeneration(requestId, string.Empty,
                    "The asset server returned no GLB URL.");
                yield break;
            }
            coordinator.CompleteGeneration(requestId, response.url, string.Empty);
        }

        public static byte[] EncodeWav(AudioClip clip, int sampleFrames)
        {
            sampleFrames = Mathf.Clamp(sampleFrames, 0, clip.samples);
            var sampleCount = sampleFrames * clip.channels;
            var samples = new float[sampleCount];
            clip.GetData(samples, 0);
            const int headerSize = 44;
            var bytes = new byte[headerSize + sampleCount * 2];
            using var stream = new MemoryStream(bytes);
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(bytes.Length - 8);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)clip.channels);
            writer.Write(clip.frequency);
            writer.Write(clip.frequency * clip.channels * 2);
            writer.Write((short)(clip.channels * 2));
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(sampleCount * 2);
            foreach (var sample in samples)
            {
                writer.Write((short)Mathf.RoundToInt(
                    Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }
            return bytes;
        }

        private void OnDestroy()
        {
            if (recordingActive)
            {
                Microphone.End(microphoneDevice);
            }
            if (interactable != null)
            {
                interactable.selectEntered.RemoveListener(HandlePressed);
                interactable.selectExited.RemoveListener(HandleReleased);
            }
        }
    }
}
