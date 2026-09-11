using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Meshup.Multiplayer;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Meshup.Game
{
    [DisallowMultipleComponent]
    public sealed class MeshupAssetGeneratorClient : MonoBehaviour
    {
        private const float MaximumRecordingSeconds = 60f;
        private const float MinimumRecordingSeconds = 0.1f;
        private const int UploadSampleRate = 16000;

        [Serializable]
        private sealed class GenerateResponse
        {
            public string url;
            public string asset_id;
        }

        private MeshupGameCoordinator coordinator;
        private XRSimpleInteractable interactable;
        private Coroutine recordingTimeout;
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
            var voice = VoiceChatController.Instance;
            var error = string.Empty;
            if (voice == null || !voice.TryBeginExclusiveCapture(
                    VoiceMuteReason.ObjectDescription,
                    MaximumRecordingSeconds, out error))
            {
                coordinator.ReportLocalMessage(string.IsNullOrEmpty(error)
                    ? "Voice chat is unavailable."
                    : error);
                return;
            }
            recordingActive = true;
            recordingTimeout = StartCoroutine(StopAtMaximumDuration());
            coordinator.ReportLocalMessage("Recording object description…");
        }

        private void HandleReleased(SelectExitEventArgs args)
        {
            if (!recordingActive)
            {
                return;
            }
            FinishRecording();
        }

        private IEnumerator StopAtMaximumDuration()
        {
            yield return new WaitForSecondsRealtime(MaximumRecordingSeconds);
            recordingTimeout = null;
            if (recordingActive)
            {
                FinishRecording();
            }
        }

        private void FinishRecording()
        {
            if (recordingTimeout != null)
            {
                StopCoroutine(recordingTimeout);
                recordingTimeout = null;
            }
            recordingActive = false;
            var capture = VoiceChatController.Instance?.EndExclusiveCapture(
                VoiceMuteReason.ObjectDescription)
                ?? new VoiceCapture(Array.Empty<float>(), 1,
                    UploadSampleRate);
            if (!capture.HasAudio
                || capture.DurationSeconds < MinimumRecordingSeconds)
            {
                coordinator.ReportLocalMessage("No audio was recorded.");
                return;
            }
            var pcm = VoskGuessTranscriber.ConvertToMonoPcm(capture.Samples,
                capture.Channels, capture.SampleRate, UploadSampleRate);
            var wav = EncodeWav(pcm, UploadSampleRate);
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

        public static byte[] EncodeWav(short[] monoSamples, int sampleRate)
        {
            monoSamples ??= Array.Empty<short>();
            sampleRate = Mathf.Max(1, sampleRate);
            const int headerSize = 44;
            var bytes = new byte[headerSize + monoSamples.Length * 2];
            using var stream = new MemoryStream(bytes);
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(bytes.Length - 8);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(monoSamples.Length * 2);
            foreach (var sample in monoSamples)
            {
                writer.Write(sample);
            }
            return bytes;
        }

        private void OnDestroy()
        {
            if (recordingActive)
            {
                VoiceChatController.Instance?.CancelExclusiveCapture(
                    VoiceMuteReason.ObjectDescription);
                recordingActive = false;
            }
            if (recordingTimeout != null)
            {
                StopCoroutine(recordingTimeout);
                recordingTimeout = null;
            }
            if (interactable != null)
            {
                interactable.selectEntered.RemoveListener(HandlePressed);
                interactable.selectExited.RemoveListener(HandleReleased);
            }
        }
    }
}
