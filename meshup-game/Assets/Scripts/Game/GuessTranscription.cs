using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using Vosk;

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
    /// Offline push-to-talk recognition constrained to the game's verb list.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoskGuessTranscriber : MonoBehaviour, IGuessTranscriber
    {
        private const string ModelName = "vosk-model-small-en-us-0.15";
        private const string ModelArchive = ModelName + ".zip";
        private const int RecognitionSampleRate = 16000;
        private const int MicrophoneBufferSeconds = 6;
        private const float MaximumRecordingSeconds = 5f;
        private const float MinimumRecordingSeconds = 0.1f;
        public const float MinimumConfidence = 0.55f;

        private readonly ConcurrentQueue<DecodeOutcome> decodeOutcomes = new();
        private InputAction pushToTalk;
        private Func<bool> canRecord;
        private HashSet<string> vocabulary = new(StringComparer.Ordinal);
        private string grammar = string.Empty;
        private Model model;
        private Task<Model> modelLoadTask;
        private Task decodeTask;
        private AudioClip recording;
        private string microphoneDevice = string.Empty;
        private Coroutine recordingTimeout;
        private Coroutine permissionRequest;
        private bool initializationStarted;
        private bool initializationComplete;
        private bool activationRequested;
        private bool isListening;
        private bool isProcessing;
        private bool isDestroyed;
        private string initializationError = string.Empty;

        public event Action<string> TranscriptionReceived;
        public event Action<string> ErrorOccurred;
        public event Action<bool> ListeningChanged;

        public bool IsAvailable => initializationComplete && model != null
            && string.IsNullOrEmpty(initializationError);

        public void Configure(Func<bool> recordingAllowed,
            IReadOnlyList<string> allowedVocabulary)
        {
            canRecord = recordingAllowed;
            vocabulary = new HashSet<string>((allowedVocabulary
                    ?? throw new ArgumentNullException(nameof(allowedVocabulary)))
                .Select(CanonicalizeSpokenGuess)
                .Where(value => !string.IsNullOrEmpty(value)),
                StringComparer.Ordinal);
            if (vocabulary.Count == 0)
            {
                throw new ArgumentException("A recognition vocabulary is required.",
                    nameof(allowedVocabulary));
            }
            grammar = BuildGrammar(vocabulary.OrderBy(value => value,
                StringComparer.Ordinal).ToArray());
            if (!initializationStarted)
            {
                initializationStarted = true;
                StartCoroutine(Initialize());
            }
        }

        private void Awake()
        {
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
            if (canRecord?.Invoke() != true)
            {
                return;
            }
            if (!initializationComplete)
            {
                ErrorOccurred?.Invoke("Offline voice recognition is still loading.");
                return;
            }
            if (!IsAvailable)
            {
                ErrorOccurred?.Invoke(string.IsNullOrWhiteSpace(initializationError)
                    ? "Offline voice recognition is unavailable."
                    : initializationError);
                return;
            }
            if (isProcessing)
            {
                ErrorOccurred?.Invoke("Still processing the previous guess.");
                return;
            }
            if (isListening)
            {
                return;
            }

            activationRequested = true;
            RequestPermissionOrBeginRecording();
        }

        public void Deactivate()
        {
            activationRequested = false;
            if (!isListening || recording == null)
            {
                return;
            }
            FinishRecording();
        }

        private IEnumerator Initialize()
        {
            string modelPath = null;
            Exception preparationError = null;
            yield return PrepareModelPath(
                value => modelPath = value,
                exception => preparationError = exception);
            if (preparationError != null)
            {
                CompleteInitialization(preparationError);
                yield break;
            }

            modelLoadTask = Task.Run(() =>
            {
                Vosk.Vosk.SetLogLevel(-1);
                var loadedModel = new Model(modelPath);
                ValidateVocabulary(loadedModel, vocabulary);
                return loadedModel;
            });
            while (!modelLoadTask.IsCompleted)
            {
                yield return null;
            }
            if (isDestroyed)
            {
                if (modelLoadTask.Status == TaskStatus.RanToCompletion)
                {
                    modelLoadTask.Result.Dispose();
                }
                yield break;
            }
            if (modelLoadTask.IsFaulted)
            {
                CompleteInitialization(modelLoadTask.Exception);
                yield break;
            }

            model = modelLoadTask.Result;
            initializationComplete = true;
        }

        private IEnumerator PrepareModelPath(Action<string> completed,
            Action<Exception> failed)
        {
            var modelsRoot = Path.Combine(Application.persistentDataPath,
                "VoskModels");
            var installedPath = Path.Combine(modelsRoot, ModelName);
            if (IsValidModel(installedPath))
            {
                completed(installedPath);
                yield break;
            }

            Directory.CreateDirectory(modelsRoot);
            var sourcePath = Path.Combine(Application.streamingAssetsPath,
                ModelArchive);
            var archivePath = sourcePath;
            var copiedArchive = false;
            if (sourcePath.Contains("://", StringComparison.Ordinal))
            {
                archivePath = Path.Combine(modelsRoot, ModelArchive + ".download");
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }
                using var request = UnityWebRequest.Get(sourcePath);
                request.downloadHandler = new DownloadHandlerFile(archivePath);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed(new IOException("Could not read the bundled Vosk model: "
                        + request.error));
                    yield break;
                }
                copiedArchive = true;
            }

            Exception extractionError = null;
            var extractionTask = Task.Run(() =>
            {
                try
                {
                    ExtractModelArchive(archivePath, modelsRoot, installedPath);
                }
                catch (Exception exception)
                {
                    extractionError = exception;
                }
                finally
                {
                    if (copiedArchive && File.Exists(archivePath))
                    {
                        File.Delete(archivePath);
                    }
                }
            });
            while (!extractionTask.IsCompleted)
            {
                yield return null;
            }
            if (extractionError != null)
            {
                failed(extractionError);
                yield break;
            }
            completed(installedPath);
        }

        private static void ExtractModelArchive(string archivePath,
            string modelsRoot, string installedPath)
        {
            var temporaryPath = Path.Combine(modelsRoot,
                ModelName + ".extracting");
            if (Directory.Exists(temporaryPath))
            {
                Directory.Delete(temporaryPath, true);
            }
            Directory.CreateDirectory(temporaryPath);

            try
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var prefix = ModelName + "/";
                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith(prefix,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var relativePath = entry.FullName.Substring(prefix.Length);
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        continue;
                    }
                    var destination = Path.GetFullPath(Path.Combine(
                        temporaryPath, relativePath));
                    var safeRoot = Path.GetFullPath(temporaryPath)
                        + Path.DirectorySeparatorChar;
                    if (!destination.StartsWith(safeRoot,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "The bundled Vosk model contains an invalid path.");
                    }
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, true);
                }
                if (!IsValidModel(temporaryPath))
                {
                    throw new InvalidDataException(
                        "The bundled Vosk model is incomplete.");
                }
                if (Directory.Exists(installedPath))
                {
                    Directory.Delete(installedPath, true);
                }
                Directory.Move(temporaryPath, installedPath);
            }
            catch
            {
                if (Directory.Exists(temporaryPath))
                {
                    Directory.Delete(temporaryPath, true);
                }
                throw;
            }
        }

        private static bool IsValidModel(string modelPath)
        {
            return Directory.Exists(modelPath)
                && File.Exists(Path.Combine(modelPath, "am", "final.mdl"))
                && File.Exists(Path.Combine(modelPath, "conf", "model.conf"))
                && File.Exists(Path.Combine(modelPath, "graph", "HCLr.fst"));
        }

        private static void ValidateVocabulary(Model loadedModel,
            IEnumerable<string> canonicalVocabulary)
        {
            var missing = new List<string>();
            foreach (var canonical in canonicalVocabulary)
            {
                foreach (var token in SpokenForm(canonical).Split(' '))
                {
                    if (loadedModel.vosk_model_find_word(token) < 0)
                    {
                        missing.Add(token);
                    }
                }
            }
            if (missing.Count > 0)
            {
                loadedModel.Dispose();
                throw new InvalidDataException("The Vosk model does not contain: "
                    + string.Join(", ", missing.Distinct().OrderBy(value => value,
                        StringComparer.Ordinal)));
            }
        }

        private void CompleteInitialization(Exception exception)
        {
            initializationComplete = true;
            initializationError = "Offline voice recognition failed to initialize: "
                + exception?.GetBaseException().Message;
            ErrorOccurred?.Invoke(initializationError);
        }

        private void RequestPermissionOrBeginRecording()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
            {
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += HandleAndroidPermissionGranted;
                callbacks.PermissionDenied += HandleAndroidPermissionDenied;
                callbacks.PermissionDeniedAndDontAskAgain +=
                    HandleAndroidPermissionDenied;
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone, callbacks);
                return;
            }
#endif
            if (IsAppleDesktop
                && !Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                if (permissionRequest == null)
                {
                    permissionRequest = StartCoroutine(RequestApplePermission());
                }
                return;
            }
            BeginRecording();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void HandleAndroidPermissionGranted(string permission)
        {
            if (activationRequested && pushToTalk?.IsPressed() == true)
            {
                BeginRecording();
            }
        }

        private void HandleAndroidPermissionDenied(string permission)
        {
            activationRequested = false;
            ErrorOccurred?.Invoke("Microphone permission was denied. Enable it "
                + "in the Quest application permissions, then try again.");
        }
#endif

        private IEnumerator RequestApplePermission()
        {
            yield return Application.RequestUserAuthorization(
                UserAuthorization.Microphone);
            permissionRequest = null;
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                activationRequested = false;
                ErrorOccurred?.Invoke("Microphone permission was denied. Enable "
                    + "it in System Settings > Privacy & Security > Microphone.");
                yield break;
            }
            if (activationRequested && pushToTalk?.IsPressed() == true)
            {
                BeginRecording();
            }
        }

        private void BeginRecording()
        {
            if (!activationRequested || isListening || isProcessing)
            {
                return;
            }
            if (Microphone.devices.Length == 0)
            {
                activationRequested = false;
                ErrorOccurred?.Invoke("No microphone is available.");
                return;
            }

            microphoneDevice = Microphone.devices[0];
            recording = Microphone.Start(microphoneDevice, false,
                MicrophoneBufferSeconds, RecognitionSampleRate);
            if (recording == null)
            {
                activationRequested = false;
                ErrorOccurred?.Invoke("The microphone could not start.");
                return;
            }
            SetListening(true);
            recordingTimeout = StartCoroutine(StopAtMaximumDuration());
        }

        private IEnumerator StopAtMaximumDuration()
        {
            yield return new WaitForSecondsRealtime(MaximumRecordingSeconds);
            recordingTimeout = null;
            if (isListening)
            {
                activationRequested = false;
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
            var clip = recording;
            recording = null;
            var position = string.IsNullOrEmpty(microphoneDevice)
                ? 0
                : Microphone.GetPosition(microphoneDevice);
            if (!string.IsNullOrEmpty(microphoneDevice))
            {
                Microphone.End(microphoneDevice);
            }
            microphoneDevice = string.Empty;
            SetListening(false);

            if (clip == null || position <= 0)
            {
                if (clip != null)
                {
                    Destroy(clip);
                }
                ErrorOccurred?.Invoke("No speech was recorded. Try again.");
                return;
            }

            var interleaved = new float[position * clip.channels];
            clip.GetData(interleaved, 0);
            var frequency = clip.frequency;
            var channels = clip.channels;
            Destroy(clip);
            var samples = ConvertToMonoPcm(interleaved, channels, frequency,
                RecognitionSampleRate);
            if (samples.Length < RecognitionSampleRate * MinimumRecordingSeconds)
            {
                ErrorOccurred?.Invoke("The guess was too short. Try again.");
                return;
            }

            isProcessing = true;
            var activeModel = model;
            var activeGrammar = grammar;
            var activeVocabulary = new HashSet<string>(vocabulary,
                StringComparer.Ordinal);
            decodeTask = Task.Run(() => Decode(samples, activeModel,
                activeGrammar, activeVocabulary));
        }

        private void Decode(short[] samples, Model activeModel,
            string activeGrammar, HashSet<string> activeVocabulary)
        {
            try
            {
                using var recognizer = new VoskRecognizer(activeModel,
                    RecognitionSampleRate, activeGrammar);
                recognizer.SetMaxAlternatives(3);
                recognizer.AcceptWaveform(samples, samples.Length);
                var result = recognizer.FinalResult();
                if (TrySelectResult(result, activeVocabulary,
                        MinimumConfidence, out var guess))
                {
                    decodeOutcomes.Enqueue(new DecodeOutcome(guess, null));
                }
                else
                {
                    decodeOutcomes.Enqueue(new DecodeOutcome(null,
                        "Didn't catch that guess. Try again."));
                }
            }
            catch (Exception exception)
            {
                decodeOutcomes.Enqueue(new DecodeOutcome(null,
                    "Offline voice recognition failed: "
                    + exception.GetBaseException().Message));
            }
        }

        private void Update()
        {
            while (decodeOutcomes.TryDequeue(out var outcome))
            {
                isProcessing = false;
                if (!string.IsNullOrEmpty(outcome.Guess))
                {
                    TranscriptionReceived?.Invoke(outcome.Guess);
                }
                else if (!string.IsNullOrEmpty(outcome.Error))
                {
                    ErrorOccurred?.Invoke(outcome.Error);
                }
            }
        }

        private void HandlePress(InputAction.CallbackContext context)
        {
            Activate();
        }

        private void HandleRelease(InputAction.CallbackContext context)
        {
            Deactivate();
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

        private static bool IsAppleDesktop => Application.platform is
            RuntimePlatform.OSXEditor or RuntimePlatform.OSXPlayer;

        public static string BuildGrammar(IReadOnlyList<string> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            var phrases = values.Select(CanonicalizeSpokenGuess)
                .Where(value => !string.IsNullOrEmpty(value))
                .Select(SpokenForm)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Concat(new[] { "[unk]" });
            return "[" + string.Join(",", phrases.Select(value =>
                "\"" + EscapeJson(value) + "\"")) + "]";
        }

        public static string CanonicalizeSpokenGuess(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }
            var normalized = string.Join(" ", value.Trim().ToLowerInvariant()
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return normalized == "high five" ? "high-five" : normalized;
        }

        public static bool TrySelectResult(string json,
            IReadOnlyCollection<string> allowedVocabulary,
            float minimumConfidence, out string guess)
        {
            guess = string.Empty;
            if (string.IsNullOrWhiteSpace(json) || allowedVocabulary == null)
            {
                return false;
            }
            VoskResult result;
            try
            {
                result = JsonUtility.FromJson<VoskResult>(json);
            }
            catch
            {
                return false;
            }
            var allowed = allowedVocabulary as HashSet<string>
                ?? new HashSet<string>(allowedVocabulary, StringComparer.Ordinal);
            var alternatives = result?.alternatives ?? Array.Empty<VoskAlternative>();
            foreach (var alternative in alternatives
                         .OrderByDescending(value => value.confidence))
            {
                var candidate = CanonicalizeSpokenGuess(alternative.text);
                if (alternative.confidence >= minimumConfidence
                    && allowed.Contains(candidate))
                {
                    guess = candidate;
                    return true;
                }
            }
            if (alternatives.Length == 0)
            {
                var candidate = CanonicalizeSpokenGuess(result?.text);
                if (allowed.Contains(candidate))
                {
                    guess = candidate;
                    return true;
                }
            }
            return false;
        }

        public static short[] ConvertToMonoPcm(float[] interleaved,
            int channels, int inputRate, int outputRate)
        {
            if (interleaved == null || interleaved.Length == 0 || channels <= 0
                || inputRate <= 0 || outputRate <= 0)
            {
                return Array.Empty<short>();
            }
            var frameCount = interleaved.Length / channels;
            var mono = new float[frameCount];
            for (var frame = 0; frame < frameCount; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += interleaved[frame * channels + channel];
                }
                mono[frame] = sum / channels;
            }

            var outputCount = Math.Max(1, (int)Math.Round(
                frameCount * (double)outputRate / inputRate));
            var output = new short[outputCount];
            for (var index = 0; index < outputCount; index++)
            {
                var sourcePosition = index * (double)inputRate / outputRate;
                var lower = Math.Min((int)sourcePosition, frameCount - 1);
                var upper = Math.Min(lower + 1, frameCount - 1);
                var fraction = (float)(sourcePosition - lower);
                var value = Mathf.Lerp(mono[lower], mono[upper], fraction);
                output[index] = (short)Mathf.RoundToInt(Mathf.Clamp(value,
                    -1f, 1f) * short.MaxValue);
            }
            return output;
        }

        private static string SpokenForm(string canonical)
        {
            return canonical == "high-five" ? "high five" : canonical;
        }

        private static string EscapeJson(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(character switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => character.ToString()
                });
            }
            return builder.ToString();
        }

        private void OnDestroy()
        {
            isDestroyed = true;
            activationRequested = false;
            if (pushToTalk != null)
            {
                pushToTalk.started -= HandlePress;
                pushToTalk.canceled -= HandleRelease;
                pushToTalk.Disable();
                pushToTalk.Dispose();
            }
            if (recordingTimeout != null)
            {
                StopCoroutine(recordingTimeout);
            }
            if (recording != null)
            {
                if (!string.IsNullOrEmpty(microphoneDevice))
                {
                    Microphone.End(microphoneDevice);
                }
                Destroy(recording);
                recording = null;
            }
            SetListening(false);

            var modelToDispose = model;
            model = null;
            if (modelToDispose != null)
            {
                if (decodeTask != null && !decodeTask.IsCompleted)
                {
                    decodeTask.ContinueWith(_ => modelToDispose.Dispose());
                }
                else
                {
                    modelToDispose.Dispose();
                }
            }
            else if (modelLoadTask != null && !modelLoadTask.IsCompleted)
            {
                modelLoadTask.ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        task.Result.Dispose();
                    }
                });
            }
        }

        [Serializable]
        private sealed class VoskResult
        {
            public VoskAlternative[] alternatives;
            public string text;
        }

        [Serializable]
        private sealed class VoskAlternative
        {
            public float confidence;
            public string text;
        }

        private readonly struct DecodeOutcome
        {
            public DecodeOutcome(string guess, string error)
            {
                Guess = guess;
                Error = error;
            }

            public string Guess { get; }
            public string Error { get; }
        }
    }
}
