using System;
using System.IO;
using UnityEngine;

namespace Meshup.Multiplayer
{
    public static class AudioEncoding
    {
        public static short[] ConvertToMonoPcm(float[] interleaved,
            int channels, int inputRate, int outputRate)
        {
            if (interleaved == null || channels <= 0 || interleaved.Length < channels
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
    }
}
