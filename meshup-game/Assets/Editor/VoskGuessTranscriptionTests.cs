using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Meshup.Multiplayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Vosk;

namespace Meshup.Game.Editor.Tests
{
    public sealed class VoskGuessTranscriptionTests
    {
        [Test]
        public void GrammarContainsEveryVerbAndUnknownFallback()
        {
            var words = MimeWordService.LoadDefault();

            var grammar = VoskGuessTranscriber.BuildGrammar(words.Verbs);

            Assert.That(words.Verbs, Is.Not.Empty);
            Assert.That(words.Verbs.Distinct().Count(), Is.EqualTo(words.VerbCount));
            foreach (var verb in words.Verbs)
            {
                var spoken = verb == "high-five" ? "high five" : verb;
                Assert.That(grammar, Does.Contain($"\"{spoken}\""), verb);
            }
            Assert.That(grammar, Does.EndWith("\"[unk]\"]"));
            Assert.That(grammar.Count(character => character == '"'),
                Is.EqualTo((words.VerbCount + 1) * 2));
        }

        [TestCase(" HIGH   FIVE ", "high-five")]
        [TestCase("Jump", "jump")]
        [TestCase("  ", "")]
        public void SpokenGuessCanonicalizationIsStable(string input,
            string expected)
        {
            Assert.That(VoskGuessTranscriber.CanonicalizeSpokenGuess(input),
                Is.EqualTo(expected));
        }

        [Test]
        public void ResultSelectsHighestConfidenceAllowedAlternative()
        {
            const string json = "{\"alternatives\":["
                + "{\"confidence\":0.71,\"text\":\"jump\"},"
                + "{\"confidence\":0.82,\"text\":\"high five\"}]}";

            var accepted = VoskGuessTranscriber.TrySelectResult(json,
                new[] { "jump", "high-five" },
                VoskGuessTranscriber.MinimumConfidence, out var guess);

            Assert.That(accepted, Is.True);
            Assert.That(guess, Is.EqualTo("high-five"));
        }

        [TestCase("{\"alternatives\":[{\"confidence\":0.54,\"text\":\"jump\"}]}")]
        [TestCase("{\"alternatives\":[{\"confidence\":0.99,\"text\":\"[unk]\"}]}")]
        [TestCase("{\"alternatives\":[{\"confidence\":0.99,\"text\":\"banana\"}]}")]
        [TestCase("")]
        public void LowConfidenceUnknownAndEmptyResultsAreRejected(string json)
        {
            Assert.That(VoskGuessTranscriber.TrySelectResult(json,
                new[] { "jump" }, VoskGuessTranscriber.MinimumConfidence,
                out var guess), Is.False);
            Assert.That(guess, Is.Empty);
        }

        [Test]
        public void PlainTextResultRemainsCompatible()
        {
            Assert.That(VoskGuessTranscriber.TrySelectResult(
                "{\"text\":\"jump\"}", new[] { "jump" },
                VoskGuessTranscriber.MinimumConfidence, out var guess), Is.True);
            Assert.That(guess, Is.EqualTo("jump"));
        }

        [Test, Timeout(120000)]
        public void BundledModelContainsEveryGrammarToken()
        {
            WithBundledModel(model =>
            {
                foreach (var verb in MimeWordService.LoadDefault().Verbs)
                {
                    var spoken = verb == "high-five" ? "high five" : verb;
                    foreach (var token in spoken.Split(' '))
                    {
                        Assert.That(model.vosk_model_find_word(token),
                            Is.GreaterThanOrEqualTo(0), token);
                    }
                }
            });
        }

        [Test, Timeout(120000)]
        public void GuessUsesSharedMicrophoneAvailabilityAndPreservesInputBindings()
        {
            WithBundledModel(model =>
            {
                var voiceObject = new GameObject("Test shared voice");
                var guessObject = new GameObject("Test guess transcription");
                var voice = voiceObject.AddComponent<VoiceChatController>();
                var transcriber = guessObject.AddComponent<VoskGuessTranscriber>();
                try
                {
                    Assert.That(VoiceChatController.Instance, Is.Null);
                    Invoke(voice, "Awake");
                    Invoke(transcriber, "Awake");
                    Set(transcriber, "canRecord", new Func<bool>(() => true));
                    Set(transcriber, "initializationComplete", true);
                    Set(transcriber, "model", model);
                    Set(voice, "muteReasons", VoiceMuteReason.Manual);
                    var pushToTalk = (InputAction)Get(transcriber, "pushToTalk");
                    Assert.That(pushToTalk.bindings.Select(binding => binding.path),
                        Is.EquivalentTo(new[]
                        {
                            "<XRController>{LeftHand}/primaryButton",
                            "<XRController>{RightHand}/primaryButton",
                            "<Keyboard>/g"
                        }));
                    var error = string.Empty;
                    var listeningChanges = 0;
                    transcriber.ErrorOccurred += message => error = message;
                    transcriber.ListeningChanged += _ => listeningChanges++;

                    transcriber.Activate();
                    Assert.That(error, Does.Contain("still starting"));
                    Set(voice, "permissionDenied", true);
                    transcriber.Activate();
                    Assert.That(error, Does.Contain("permission was denied"));
                    Set(voice, "permissionDenied", false);
                    Set(voice, "initializationFailed", true);
                    transcriber.Activate();
                    Assert.That(error, Does.Contain("No working microphone"));
                    Set(voice, "initializationFailed", false);
                    Set(voice, "exclusiveReason", VoiceMuteReason.ObjectDescription);
                    Set(voice, "muteReasons", VoiceMuteReason.Manual | VoiceMuteReason.ObjectDescription);
                    transcriber.Activate();
                    Assert.That(error, Does.Contain("already recording"));
                    transcriber.Deactivate();
                    Assert.That(listeningChanges, Is.Zero);
                    Assert.That(voice.IsCapturing, Is.True, "A rejected guess must not cancel a description.");
                    Assert.That(voice.MuteReasons,
                        Is.EqualTo(VoiceMuteReason.Manual | VoiceMuteReason.ObjectDescription));

                    // Release an empty guess through the same controller and keep manual mute.
                    Set(voice, "exclusiveReason", VoiceMuteReason.GuessRecording);
                    Set(voice, "muteReasons", VoiceMuteReason.Manual | VoiceMuteReason.GuessRecording);
                    Invoke(Get(voice, "captureTap"), "BeginCapture", 5f);
                    Set(transcriber, "isListening", true);
                    transcriber.Deactivate();
                    Assert.That(error, Does.Contain("No speech was recorded"));
                    Assert.That(listeningChanges, Is.EqualTo(1));
                    Assert.That(voice.IsCapturing, Is.False);
                    Assert.That(voice.MuteReasons, Is.EqualTo(VoiceMuteReason.Manual));
                }
                finally
                {
                    Set(transcriber, "model", null); // The helper owns this model.
                    UnityEngine.Object.DestroyImmediate(guessObject);
                    UnityEngine.Object.DestroyImmediate(voiceObject);
                }
            });
        }

        private static void WithBundledModel(Action<Model> test)
        {
            var extractionRoot = Path.Combine(Path.GetTempPath(),
                "meshup-vosk-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(Path.Combine(
                    Application.streamingAssetsPath,
                    "vosk-model-small-en-us-0.15.zip"), extractionRoot);
                using var model = new Model(Path.Combine(extractionRoot,
                    "vosk-model-small-en-us-0.15"));
                test(model);
            }
            finally
            {
                if (Directory.Exists(extractionRoot))
                {
                    Directory.Delete(extractionRoot, true);
                }
            }
        }

        private static object Get(object target, string field) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void Invoke(object target, string method, params object[] arguments) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(target, arguments);
    }
}
