using System;
using System.Collections;
using System.Collections.Generic;
using Ubiq.Voip.Implementations.Unity;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Meshup.Multiplayer
{
    [Flags]
    public enum VoiceMuteReason
    {
        None = 0,
        Manual = 1,
        GuessRecording = 2,
        ObjectDescription = 4
    }

    public readonly struct VoiceCapture
    {
        public VoiceCapture(float[] samples, int channels, int sampleRate)
        {
            Samples = samples ?? Array.Empty<float>();
            Channels = Mathf.Max(1, channels);
            SampleRate = Mathf.Max(1, sampleRate);
        }

        public float[] Samples { get; }
        public int Channels { get; }
        public int SampleRate { get; }
        public int SampleFrames => Samples.Length / Channels;
        public float DurationSeconds => SampleFrames / (float)SampleRate;
        public bool HasAudio => Samples.Length > 0;
    }

    /// <summary>
    /// Owns the shared Ubiq microphone and outgoing mute state. Guess and object
    /// recordings tap the live microphone stream while its WebRTC track is
    /// disabled, so incoming voice remains available and the hardware device is
    /// never opened by two Unity Microphone clients at once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceChatController : MonoBehaviour
    {
        private const string ManualMutePreference = "meshup.voice.muted";

        public static VoiceChatController Instance { get; private set; }

        private PeerConnectionMicrophone microphone;
        private VoiceCaptureTap captureTap;
        private VoiceMuteReason muteReasons;
        private VoiceMuteReason exclusiveReason;
        private bool keeperRegistered;
        private bool permissionDenied;
        private bool initializationFailed;
        private Coroutine keeperCoroutine;
        private Unity.WebRTC.AudioStreamTrack appliedTrack;
        private bool? appliedTrackEnabled;

        public VoiceMuteReason MuteReasons => muteReasons;
        public bool IsMuted => muteReasons != VoiceMuteReason.None;
        public bool IsManuallyMuted =>
            (muteReasons & VoiceMuteReason.Manual) != 0;
        public bool IsCapturing => exclusiveReason != VoiceMuteReason.None;
        public bool IsAvailable => !permissionDenied && !initializationFailed;
        public bool IsReady => IsAvailable
            && microphone != null
            && microphone.state == PeerConnectionMicrophone.State.Running
            && microphone.audioStreamTrack != null;

        public event Action StateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }

            Instance = this;
            if (PlayerPrefs.GetInt(ManualMutePreference, 0) != 0)
            {
                muteReasons |= VoiceMuteReason.Manual;
            }
            captureTap = GetComponent<VoiceCaptureTap>()
                ?? gameObject.AddComponent<VoiceCaptureTap>();
        }

        private IEnumerator Start()
        {
            yield return RequestMicrophonePermission();
            if (permissionDenied)
            {
                StateChanged?.Invoke();
                yield break;
            }

            microphone = GetComponent<PeerConnectionMicrophone>()
                ?? gameObject.AddComponent<PeerConnectionMicrophone>();
            if (Microphone.devices.Length == 0)
            {
                initializationFailed = true;
                StateChanged?.Invoke();
                yield break;
            }

            keeperRegistered = true;
            keeperCoroutine = StartCoroutine(microphone.AddUser(gameObject));
            var deadline = Time.realtimeSinceStartup + 10f;
            while (microphone.state
                    != PeerConnectionMicrophone.State.Running
                && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            if (microphone.state != PeerConnectionMicrophone.State.Running)
            {
                initializationFailed = true;
                StopCoroutine(keeperCoroutine);
                keeperCoroutine = null;
                microphone.RemoveUser(gameObject);
                keeperRegistered = false;
                StateChanged?.Invoke();
                yield break;
            }
            keeperCoroutine = null;
            ApplyMuteState();
            StateChanged?.Invoke();
        }

        private void Update()
        {
            ApplyMuteState();
        }

        public void ToggleManualMute()
        {
            SetManualMuted(!IsManuallyMuted);
        }

        public void SetManualMuted(bool muted)
        {
            SetMuteReason(VoiceMuteReason.Manual, muted);
            PlayerPrefs.SetInt(ManualMutePreference, muted ? 1 : 0);
            PlayerPrefs.Save();
        }

        public bool TryBeginExclusiveCapture(VoiceMuteReason reason,
            float maximumSeconds, out string error)
        {
            if (reason is not VoiceMuteReason.GuessRecording
                and not VoiceMuteReason.ObjectDescription)
            {
                error = "That microphone capture mode is invalid.";
                return false;
            }
            if (exclusiveReason != VoiceMuteReason.None)
            {
                error = "The microphone is already recording.";
                return false;
            }
            if (!IsReady)
            {
                error = permissionDenied
                    ? "Microphone permission was denied. Enable it in system settings."
                    : initializationFailed
                        ? "No working microphone is available."
                        : "Voice microphone is still starting. Try again.";
                return false;
            }

            exclusiveReason = reason;
            // Disable the outbound track before accepting samples for the
            // private recording, so no beginning-of-recording audio can leak.
            SetMuteReason(reason, true);
            captureTap.BeginCapture(maximumSeconds);
            error = string.Empty;
            return true;
        }

        public VoiceCapture EndExclusiveCapture(VoiceMuteReason reason)
        {
            if (exclusiveReason != reason)
            {
                return new VoiceCapture(Array.Empty<float>(), 1, 16000);
            }

            var capture = captureTap.EndCapture();
            exclusiveReason = VoiceMuteReason.None;
            SetMuteReason(reason, false);
            return capture;
        }

        public void CancelExclusiveCapture(VoiceMuteReason reason)
        {
            if (exclusiveReason != reason)
            {
                return;
            }

            captureTap.CancelCapture();
            exclusiveReason = VoiceMuteReason.None;
            SetMuteReason(reason, false);
        }

        private void SetMuteReason(VoiceMuteReason reason, bool active)
        {
            var previous = muteReasons;
            muteReasons = active ? muteReasons | reason : muteReasons & ~reason;
            if (previous == muteReasons)
            {
                return;
            }

            ApplyMuteState();
            StateChanged?.Invoke();
        }

        private void ApplyMuteState()
        {
            var track = microphone?.audioStreamTrack;
            if (track == null)
            {
                appliedTrack = null;
                appliedTrackEnabled = null;
                return;
            }

            var shouldEnable = !IsMuted;
            if (ReferenceEquals(appliedTrack, track)
                && appliedTrackEnabled == shouldEnable)
            {
                return;
            }

            try
            {
                track.Enabled = shouldEnable;
                appliedTrack = track;
                appliedTrackEnabled = shouldEnable;
            }
            catch (ObjectDisposedException)
            {
                appliedTrack = null;
                appliedTrackEnabled = null;
            }
        }

        private IEnumerator RequestMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                var completed = false;
                var granted = false;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ =>
                {
                    granted = true;
                    completed = true;
                };
                callbacks.PermissionDenied += _ => completed = true;
                callbacks.PermissionDeniedAndDontAskAgain += _ =>
                    completed = true;
                Permission.RequestUserPermission(Permission.Microphone,
                    callbacks);
                while (!completed)
                {
                    yield return null;
                }
                permissionDenied = !granted;
            }
#elif (UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX)
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                yield return Application.RequestUserAuthorization(
                    UserAuthorization.Microphone);
                permissionDenied = !Application.HasUserAuthorization(
                    UserAuthorization.Microphone);
            }
#endif
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            if (exclusiveReason != VoiceMuteReason.None)
            {
                captureTap?.CancelCapture();
                exclusiveReason = VoiceMuteReason.None;
            }
            if (microphone?.audioStreamTrack != null)
            {
                try
                {
                    microphone.audioStreamTrack.Enabled = true;
                }
                catch (ObjectDisposedException)
                {
                }
            }
            if (keeperRegistered && microphone != null)
            {
                microphone.RemoveUser(gameObject);
            }
            if (keeperCoroutine != null)
            {
                StopCoroutine(keeperCoroutine);
            }
            Instance = null;
        }
    }

    /// <summary>
    /// Copies the microphone's Unity audio-filter stream on the audio thread.
    /// </summary>
    internal sealed class VoiceCaptureTap : MonoBehaviour
    {
        private readonly object sync = new();
        private List<float> samples;
        private int channels = 1;
        private int sampleRate = 16000;
        private float maximumSeconds;
        private bool capturing;

        public void BeginCapture(float maximumSeconds)
        {
            lock (sync)
            {
                var outputRate = Mathf.Max(1, AudioSettings.outputSampleRate);
                this.maximumSeconds = Mathf.Max(0.1f, maximumSeconds);
                var initialCapacity = Mathf.CeilToInt(this.maximumSeconds
                    * outputRate);
                samples = new List<float>(initialCapacity);
                channels = 1;
                sampleRate = outputRate;
                capturing = true;
            }
        }

        public VoiceCapture EndCapture()
        {
            lock (sync)
            {
                capturing = false;
                var result = new VoiceCapture(samples?.ToArray(), channels,
                    sampleRate);
                samples = null;
                return result;
            }
        }

        public void CancelCapture()
        {
            lock (sync)
            {
                capturing = false;
                samples = null;
            }
        }

        private void OnAudioFilterRead(float[] data, int channelCount)
        {
            lock (sync)
            {
                if (!capturing || samples == null || data == null)
                {
                    return;
                }

                var sourceChannels = channelCount > 0 ? channelCount : 1;
                var availableFrames = data.Length / sourceChannels;
                var limit = (int)(maximumSeconds * sampleRate);
                var remainingFrames = limit - samples.Count;
                var frames = remainingFrames < availableFrames
                    ? remainingFrames
                    : availableFrames;
                for (var frame = 0; frame < frames; frame++)
                {
                    var sum = 0f;
                    var offset = frame * sourceChannels;
                    for (var channel = 0; channel < sourceChannels; channel++)
                    {
                        sum += data[offset + channel];
                    }
                    samples.Add(sum / sourceChannels);
                }
            }
        }
    }
}
