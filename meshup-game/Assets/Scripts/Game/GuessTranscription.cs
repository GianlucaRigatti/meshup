using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Meshup.Game
{
    public interface IGuessTranscriber
    {
        event Action<string> TranscriptionReceived;
        event Action<string> ErrorOccurred;
        event Action<bool> ListeningChanged;
        bool IsAvailable { get; }
        void Activate();
        void Deactivate();
    }

    /// <summary>
    /// Keeps gameplay independent of Meta Voice SDK while binding at runtime to
    /// Oculus.Voice.AppVoiceExperience when com.meta.xr.sdk.voice is installed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MetaGuessTranscriber : MonoBehaviour, IGuessTranscriber
    {
        private readonly string[] voiceTypeNames =
        {
            "Oculus.Voice.AppVoiceExperience",
            "Meta.WitAi.AppVoiceExperience"
        };

        private Component voiceExperience;
        private MethodInfo activateMethod;
        private MethodInfo deactivateMethod;
        private object transcriptionEvent;
        private MethodInfo removeListenerMethod;
        private UnityAction<string> transcriptionListener;
        private object startListeningEvent;
        private object stopListeningEvent;
        private object errorEvent;
        private MethodInfo removeStartListenerMethod;
        private MethodInfo removeStopListenerMethod;
        private MethodInfo removeErrorListenerMethod;
        private UnityAction startListeningListener;
        private UnityAction stopListeningListener;
        private UnityAction<string, string> errorListener;
        private InputAction pushToTalk;
        private Func<bool> canRecord;
        private Coroutine activationWatchdog;
        private Coroutine permissionRequest;
        private bool isListening;
        private bool activationRequested;

        public event Action<string> TranscriptionReceived;
        public event Action<string> ErrorOccurred;
        public event Action<bool> ListeningChanged;
        public bool IsAvailable => voiceExperience != null
            && activateMethod != null && deactivateMethod != null;

        public void Configure(Func<bool> recordingAllowed)
        {
            canRecord = recordingAllowed;
        }

        private void Awake()
        {
            BindVoiceExperience();
            pushToTalk = new InputAction("MeshUp Push To Talk",
                InputActionType.Button);
            pushToTalk.AddBinding("<XRController>{LeftHand}/primaryButton");
            pushToTalk.AddBinding("<XRController>{RightHand}/primaryButton");
            pushToTalk.AddBinding("<Keyboard>/g");
            pushToTalk.started += HandlePress;
            pushToTalk.canceled += HandleRelease;
            pushToTalk.Enable();
        }

        public void Activate()
        {
            if (!IsAvailable)
            {
                ErrorOccurred?.Invoke(
                    "Meta Voice SDK or its configured AppVoiceExperience is unavailable.");
                return;
            }
            if (IsAppleDesktop
                && !Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                activationRequested = true;
                if (permissionRequest == null)
                {
                    permissionRequest = StartCoroutine(
                        RequestMicrophonePermission());
                }
                return;
            }
            BeginSdkActivation();
        }

        private void BeginSdkActivation()
        {
            try
            {
                activationRequested = true;
                activateMethod.Invoke(voiceExperience, null);
                if (activationWatchdog != null)
                {
                    StopCoroutine(activationWatchdog);
                }
                activationWatchdog = StartCoroutine(WatchActivation());
            }
            catch (Exception exception)
            {
                activationRequested = false;
                ErrorOccurred?.Invoke(exception.GetBaseException().Message);
            }
        }

        private IEnumerator RequestMicrophonePermission()
        {
            yield return Application.RequestUserAuthorization(
                UserAuthorization.Microphone);
            permissionRequest = null;
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                activationRequested = false;
                ErrorOccurred?.Invoke("Microphone permission was denied. Enable "
                    + "it in System Settings > Privacy & Security > Microphone, "
                    + "then restart MeshUp.");
                yield break;
            }
            if (activationRequested && pushToTalk?.IsPressed() == true)
            {
                BeginSdkActivation();
            }
            else
            {
                activationRequested = false;
            }
        }

        public void Deactivate()
        {
            if (!IsAvailable)
            {
                return;
            }
            try
            {
                activationRequested = false;
                deactivateMethod.Invoke(voiceExperience, null);
                SetListening(false);
            }
            catch (Exception exception)
            {
                ErrorOccurred?.Invoke(exception.GetBaseException().Message);
            }
        }

        private void BindVoiceExperience()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(GetTypesSafely)
                .FirstOrDefault(candidate => voiceTypeNames.Contains(
                    candidate.FullName, StringComparer.Ordinal));
            if (type == null)
            {
                return;
            }

            voiceExperience = FindObjectsByType<Component>(
                    FindObjectsInactive.Include)
                .FirstOrDefault(component => component != null
                    && type.IsInstanceOfType(component));
            if (voiceExperience == null)
            {
                // The component still needs a WitConfiguration assigned in the
                // Inspector or through Meta's setup window before use.
                voiceExperience = gameObject.AddComponent(type);
            }
            activateMethod = type.GetMethod("Activate",
                BindingFlags.Instance | BindingFlags.Public,
                null, Type.EmptyTypes, null);
            deactivateMethod = type.GetMethod("Deactivate",
                BindingFlags.Instance | BindingFlags.Public,
                null, Type.EmptyTypes, null);

            var voiceEvents = type.GetProperty("VoiceEvents",
                BindingFlags.Instance | BindingFlags.Public)?.GetValue(voiceExperience);
            transcriptionEvent = voiceEvents?.GetType().GetProperty(
                "OnFullTranscription", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(voiceEvents);
            var addListener = transcriptionEvent?.GetType().GetMethod(
                "AddListener", new[] { typeof(UnityAction<string>) });
            removeListenerMethod = transcriptionEvent?.GetType().GetMethod(
                "RemoveListener", new[] { typeof(UnityAction<string>) });
            if (addListener != null)
            {
                transcriptionListener = HandleTranscription;
                addListener.Invoke(transcriptionEvent,
                    new object[] { transcriptionListener });
            }

            BindVoiceEvent(voiceEvents, "OnStartListening",
                HandleStartedListening, out startListeningEvent,
                out removeStartListenerMethod, out startListeningListener);
            BindVoiceEvent(voiceEvents, "OnStoppedListening",
                HandleStoppedListening, out stopListeningEvent,
                out removeStopListenerMethod, out stopListeningListener);

            errorEvent = voiceEvents?.GetType().GetProperty("OnError",
                BindingFlags.Instance | BindingFlags.Public)?.GetValue(voiceEvents);
            var addErrorListener = errorEvent?.GetType().GetMethod(
                "AddListener", new[] { typeof(UnityAction<string, string>) });
            removeErrorListenerMethod = errorEvent?.GetType().GetMethod(
                "RemoveListener", new[] { typeof(UnityAction<string, string>) });
            if (addErrorListener != null)
            {
                errorListener = HandleVoiceError;
                addErrorListener.Invoke(errorEvent, new object[] { errorListener });
            }
        }

        private static void BindVoiceEvent(object voiceEvents,
            string propertyName, UnityAction listener, out object unityEvent,
            out MethodInfo removeMethod, out UnityAction storedListener)
        {
            unityEvent = voiceEvents?.GetType().GetProperty(propertyName,
                BindingFlags.Instance | BindingFlags.Public)?.GetValue(voiceEvents);
            var addMethod = unityEvent?.GetType().GetMethod("AddListener",
                new[] { typeof(UnityAction) });
            removeMethod = unityEvent?.GetType().GetMethod("RemoveListener",
                new[] { typeof(UnityAction) });
            storedListener = listener;
            addMethod?.Invoke(unityEvent, new object[] { storedListener });
        }

        private static Type[] GetTypesSafely(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null).ToArray();
            }
        }

        private void HandlePress(InputAction.CallbackContext context)
        {
            if (canRecord?.Invoke() == true)
            {
                Activate();
            }
        }

        private void HandleRelease(InputAction.CallbackContext context)
        {
            if (isListening || activationRequested
                || canRecord?.Invoke() == true)
            {
                Deactivate();
            }
        }

        private void HandleTranscription(string transcription)
        {
            if (!string.IsNullOrWhiteSpace(transcription))
            {
                TranscriptionReceived?.Invoke(transcription);
            }
        }

        private void HandleStartedListening()
        {
            activationRequested = false;
            SetListening(true);
        }

        private void HandleStoppedListening()
        {
            activationRequested = false;
            SetListening(false);
        }

        private void HandleVoiceError(string error, string message)
        {
            activationRequested = false;
            SetListening(false);
            var detail = string.Join(": ", new[] { error, message }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            ErrorOccurred?.Invoke(string.IsNullOrWhiteSpace(detail)
                ? VoiceUnavailableMessage()
                : $"Voice input failed: {detail}");
        }

        private IEnumerator WatchActivation()
        {
            yield return new WaitForSecondsRealtime(1f);
            activationWatchdog = null;
            if (activationRequested && pushToTalk?.IsPressed() == true
                && !isListening)
            {
                var message = VoiceUnavailableMessage();
                Deactivate();
                ErrorOccurred?.Invoke(message);
            }
        }

        private string VoiceUnavailableMessage()
        {
            var details = new[]
            {
                InvokeStatusMethod("GetActivateAudioError"),
                InvokeStatusMethod("GetSendError")
            }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct();
            var suffix = string.Join(" ", details);
            var guidance = IsAppleDesktop
                ? "Check System Settings > Privacy & Security > Microphone, "
                    + "the selected input device, and the Wit configuration."
                : "Check microphone privacy access, the selected recording "
                    + "device, and the Wit configuration.";
            return "Voice input could not start. " + guidance
                + (string.IsNullOrWhiteSpace(suffix) ? string.Empty : $" {suffix}");
        }

        private static bool IsAppleDesktop => Application.platform is
            RuntimePlatform.OSXEditor or RuntimePlatform.OSXPlayer;

        private string InvokeStatusMethod(string methodName)
        {
            try
            {
                return voiceExperience?.GetType().GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.Public)?.Invoke(
                        voiceExperience, null) as string;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void SetListening(bool value)
        {
            if (isListening == value)
            {
                return;
            }
            isListening = value;
            ListeningChanged?.Invoke(value);
        }

        private void OnDestroy()
        {
            if (pushToTalk != null)
            {
                pushToTalk.started -= HandlePress;
                pushToTalk.canceled -= HandleRelease;
                pushToTalk.Dispose();
            }
            if (removeListenerMethod != null && transcriptionListener != null)
            {
                removeListenerMethod.Invoke(transcriptionEvent,
                    new object[] { transcriptionListener });
            }
            RemoveVoiceListener(startListeningEvent, removeStartListenerMethod,
                startListeningListener);
            RemoveVoiceListener(stopListeningEvent, removeStopListenerMethod,
                stopListeningListener);
            if (removeErrorListenerMethod != null && errorListener != null)
            {
                removeErrorListenerMethod.Invoke(errorEvent,
                    new object[] { errorListener });
            }
        }

        private static void RemoveVoiceListener(object unityEvent,
            MethodInfo removeMethod, UnityAction listener)
        {
            if (removeMethod != null && listener != null)
            {
                removeMethod.Invoke(unityEvent, new object[] { listener });
            }
        }
    }
}
