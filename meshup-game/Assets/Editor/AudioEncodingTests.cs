using System;
using System.IO;
using Meshup.Multiplayer;
using NUnit.Framework;

namespace Meshup.Game.Editor.Tests
{
    public sealed class AudioEncodingTests
    {
        [Test]
        public void SharedVoiceCaptureReportsFramesAndDuration()
        {
            var capture = new VoiceCapture(new float[32000], 2, 16000);

            Assert.That(capture.SampleFrames, Is.EqualTo(16000));
            Assert.That(capture.DurationSeconds, Is.EqualTo(1f));
            Assert.That(capture.HasAudio, Is.True);
        }

        [Test]
        public void AudioConversionDownmixesAndResamples()
        {
            var result = AudioEncoding.ConvertToMonoPcm(new[]
            {
                1f, -1f,
                0.5f, 0.5f,
                -0.5f, -0.5f,
                0f, 0f
            }, 2, 32000, 16000);

            Assert.That(result, Is.EqualTo(new short[] { 0, -16384 }));
        }

        [Test]
        public void UpsamplingInterpolatesAndClampsSamples()
        {
            var result = AudioEncoding.ConvertToMonoPcm(
                new[] { -2f, 0f, 2f }, 1, 8000, 16000);

            Assert.That(result, Is.EqualTo(new short[]
                { -32767, -32767, 0, 32767, 32767, 32767 }));
        }

        [TestCase(null, 1, 16000, 16000)]
        [TestCase(new float[0], 1, 16000, 16000)]
        [TestCase(new float[] { 1f }, 2, 16000, 16000)]
        [TestCase(new float[] { 1f }, 0, 16000, 16000)]
        [TestCase(new float[] { 1f }, 1, 0, 16000)]
        [TestCase(new float[] { 1f }, 1, 16000, 0)]
        public void InvalidOrIncompleteAudioProducesNoPcm(float[] samples,
            int channels, int inputRate, int outputRate)
        {
            Assert.That(AudioEncoding.ConvertToMonoPcm(samples, channels,
                inputRate, outputRate), Is.Empty);
        }

        [Test]
        public void ConvertedDescriptionHasCorrectWavMetadataAndPcmPayload()
        {
            // One second of the shared 48 kHz stereo capture becomes 16 kHz mono.
            var samples = new float[48000 * 2];
            for (var frame = 0; frame < 48000; frame++)
            {
                samples[frame * 2] = 0.75f;
                samples[frame * 2 + 1] = 0.25f;
            }
            var pcm = AudioEncoding.ConvertToMonoPcm(samples, 2, 48000, 16000);
            var wav = AudioEncoding.EncodeWav(pcm, 16000);

            Assert.That(System.Text.Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(System.Text.Encoding.ASCII.GetString(wav, 8, 4), Is.EqualTo("WAVE"));
            Assert.That(pcm, Has.Length.EqualTo(16000));
            Assert.That(wav, Has.Length.EqualTo(44 + 32000));
            Assert.That(BitConverter.ToInt32(wav, 4), Is.EqualTo(wav.Length - 8));
            Assert.That(BitConverter.ToInt16(wav, 20), Is.EqualTo(1), "PCM format");
            Assert.That(BitConverter.ToInt16(wav, 22), Is.EqualTo(1), "Mono");
            Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(16000));
            Assert.That(BitConverter.ToInt32(wav, 28), Is.EqualTo(32000), "Bytes per second");
            Assert.That(BitConverter.ToInt16(wav, 32), Is.EqualTo(2), "Block alignment");
            Assert.That(BitConverter.ToInt16(wav, 34), Is.EqualTo(16));
            Assert.That(BitConverter.ToInt32(wav, 40), Is.EqualTo(32000));
            using var reader = new BinaryReader(new MemoryStream(wav));
            reader.BaseStream.Position = 44;
            for (var frame = 0; frame < pcm.Length; frame++)
            {
                Assert.That(reader.ReadInt16(), Is.EqualTo(16384), $"Sample {frame}");
            }
        }

        [Test]
        public void WavPayloadPreservesSignedSamplesInLittleEndian()
        {
            var wav = AudioEncoding.EncodeWav(
                new short[] { short.MinValue, -1, 0, short.MaxValue }, 16000);

            Assert.That(wav.AsSpan(44).ToArray(), Is.EqualTo(new byte[]
                { 0x00, 0x80, 0xff, 0xff, 0x00, 0x00, 0xff, 0x7f }));
        }

        [Test]
        public void EmptyWavHasAValidHeaderAndNoPayload()
        {
            var wav = AudioEncoding.EncodeWav(null, 16000);

            Assert.That(wav, Has.Length.EqualTo(44));
            Assert.That(BitConverter.ToInt32(wav, 4), Is.EqualTo(36));
            Assert.That(BitConverter.ToInt32(wav, 40), Is.Zero);
        }
    }
}
