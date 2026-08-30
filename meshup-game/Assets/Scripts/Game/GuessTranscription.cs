using System;
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
        private InputAction pushToTalk;
        private Func<bool> canRecord;

        public event Action<string> TranscriptionReceived;
        public event Action<string> ErrorOccurred;
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
            try
            {
                activateMethod.Invoke(voiceExperience, null);
            }
            catch (Exception exception)
            {
                ErrorOccurred?.Invoke(exception.GetBaseException().Message);
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
                deactivateMethod.Invoke(voiceExperience, null);
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
            if (canRecord?.Invoke() == true)
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
        }
    }
}
