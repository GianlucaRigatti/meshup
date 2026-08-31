using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
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

            Assert.That(words.VerbCount, Is.EqualTo(200));
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

        [Test]
        public void AudioConversionDownmixesAndResamples()
        {
            var result = VoskGuessTranscriber.ConvertToMonoPcm(new[]
            {
                1f, -1f,
                0.5f, 0.5f,
                -0.5f, -0.5f,
                0f, 0f
            }, 2, 32000, 16000);

            Assert.That(result, Has.Length.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(0));
            Assert.That(result[1], Is.LessThan(0));
        }

        [Test, Timeout(120000)]
        public void BundledModelContainsEveryGrammarToken()
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
                foreach (var verb in MimeWordService.LoadDefault().Verbs)
                {
                    var spoken = verb == "high-five" ? "high five" : verb;
                    foreach (var token in spoken.Split(' '))
                    {
                        Assert.That(model.vosk_model_find_word(token),
                            Is.GreaterThanOrEqualTo(0), token);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(extractionRoot))
                {
                    Directory.Delete(extractionRoot, true);
                }
            }
        }
    }
}
