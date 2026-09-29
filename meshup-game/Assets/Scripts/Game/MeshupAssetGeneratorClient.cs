using System;
using System.Collections;
using System.Collections.Generic;
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
        private const string PressClipResourcePath = "GenerateButtonPress";
        private const string ReleaseClipResourcePath = "GenerateButtonRelease";

        [Serializable]
        private sealed class GenerateResponse
        {
            public string url;
            public string asset_id;
        }

        [Serializable]
        private sealed class GenerateErrorResponse
        {
            public GenerateError error;
        }

        [Serializable]
        private sealed class GenerateError
        {
            public string message;
        }

        private MeshupGameCoordinator coordinator;
        private XRSimpleInteractable interactable;
        private AudioSource buttonAudioSource;
        private AudioClip pressClip;
        private AudioClip releaseClip;
        private Coroutine recordingTimeout;
        private bool recordingActive;
        private ConsoleButtonFeedback feedback;
        private GeneratedObjectSize recordingSize = GeneratedObjectSize.Medium;

        public void Configure(MeshupGameCoordinator owner)
        {
            coordinator = owner;
            feedback = GetComponent<ConsoleButtonFeedback>();
            interactable = GetComponent<XRSimpleInteractable>();
            buttonAudioSource = GetComponent<AudioSource>();
            if (interactable == null || buttonAudioSource == null)
            {
                Debug.LogError("[MeshUp] Generate button needs authored XR "
                    + "interaction and audio components.", this);
                return;
            }
            pressClip = Resources.Load<AudioClip>(PressClipResourcePath);
            releaseClip = Resources.Load<AudioClip>(ReleaseClipResourcePath);
            if (pressClip == null || releaseClip == null)
            {
                Debug.LogWarning("[MeshUp] Generate button press/release "
                    + "sounds could not be loaded from Resources.");
            }
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
            PlayButtonSound(pressClip);
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
            feedback?.SetHeld(true);
            recordingSize = coordinator.SelectedGeneratedObjectSize;
            recordingTimeout = StartCoroutine(StopAtMaximumDuration());
            coordinator.ReportLocalMessage("Recording object description…");
        }

        private void HandleReleased(SelectExitEventArgs args)
        {
            PlayButtonSound(releaseClip);
            if (!recordingActive)
            {
                return;
            }
            FinishRecording();
        }

        private void PlayButtonSound(AudioClip clip)
        {
            if (buttonAudioSource != null && clip != null)
            {
                buttonAudioSource.PlayOneShot(clip);
            }
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
            feedback?.SetHeld(false);
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
            var pcm = AudioEncoding.ConvertToMonoPcm(capture.Samples,
                capture.Channels, capture.SampleRate, UploadSampleRate);
            var wav = AudioEncoding.EncodeWav(pcm, UploadSampleRate);
            coordinator.ReportLocalMessage("Sending description…");
            coordinator.RequestGeneration(wav, recordingSize);
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
            catch (Exception exception)
            {
                coordinator.CompleteGeneration(requestId, string.Empty,
                    exception.Message);
                yield break;
            }
            yield return operation;
            if (request.result != UnityWebRequest.Result.Success)
            {
                coordinator.CompleteGeneration(requestId, string.Empty,
                    DescribeFailure(request));
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

        private static string DescribeFailure(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                return "Could not reach the asset generator. Check that the "
                    + "server is running, then try again. " + request.error;
            }

            try
            {
                var response = JsonUtility.FromJson<GenerateErrorResponse>(
                    request.downloadHandler?.text ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(response?.error?.message))
                {
                    return response.error.message;
                }
            }
            catch (ArgumentException)
            {
                // Non-JSON server responses still have an HTTP status below.
            }
            return $"Asset generation failed (HTTP {request.responseCode}). "
                + request.error;
        }

        private void OnDestroy()
        {
            if (recordingActive)
            {
                VoiceChatController.Instance?.CancelExclusiveCapture(
                    VoiceMuteReason.ObjectDescription);
                recordingActive = false;
                feedback?.SetHeld(false);
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
