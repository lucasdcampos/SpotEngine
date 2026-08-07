using System.Buffers.Binary;
using System.Runtime.InteropServices;
using StbVorbisSharp;

namespace Spot.Assets;

/// <summary>
/// Decodes a source audio file (<c>.wav</c> or <c>.ogg</c>) into interleaved signed 16-bit PCM — the single
/// format the engine cooks to and OpenAL uploads. WAV is parsed directly (no dependency); OGG/Vorbis is
/// decoded with the managed <c>StbVorbisSharp</c> port. Decoding is pure computation with no device calls,
/// so it runs during an import or off the render thread.
/// </summary>
internal static class AudioDecoder
{
    /// <summary>Decodes a source audio file to interleaved 16-bit PCM.</summary>
    /// <param name="path">The absolute path to a <c>.wav</c> or <c>.ogg</c> file.</param>
    /// <param name="channels">Receives the channel count (1 = mono, 2 = stereo).</param>
    /// <param name="sampleRate">Receives the sample rate in frames per second.</param>
    /// <returns>The interleaved signed 16-bit PCM samples.</returns>
    /// <exception cref="NotSupportedException">The file extension or encoding is not supported.</exception>
    /// <exception cref="InvalidDataException">The file is malformed.</exception>
    public static short[] Decode(string path, out int channels, out int sampleRate)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".wav" => DecodeWav(File.ReadAllBytes(path), out channels, out sampleRate),
            ".ogg" => DecodeOgg(File.ReadAllBytes(path), out channels, out sampleRate),
            _ => throw new NotSupportedException($"Unsupported audio format '{ext}'."),
        };
    }

    private static short[] DecodeOgg(byte[] bytes, out int channels, out int sampleRate)
    {
        Vorbis vorbis = Vorbis.FromMemory(bytes);
        try
        {
            channels = vorbis.Channels;
            sampleRate = vorbis.SampleRate;

            var pcm = new List<short>();
            while (true)
            {
                vorbis.SubmitBuffer();
                int frames = vorbis.Decoded; // frames per channel decoded into SongBuffer this call
                if (frames <= 0)
                {
                    break;
                }

                pcm.AddRange(vorbis.SongBuffer.AsSpan(0, frames * vorbis.Channels));
            }

            return pcm.ToArray();
        }
        finally
        {
            vorbis.Dispose();
        }
    }

    private static short[] DecodeWav(byte[] bytes, out int channels, out int sampleRate)
    {
        ReadOnlySpan<byte> data = bytes;
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        }

        ushort audioFormat = 0;
        ushort bitsPerSample = 0;
        channels = 0;
        sampleRate = 0;
        ReadOnlySpan<byte> pcmChunk = default;
        bool haveFmt = false;
        bool haveData = false;

        int offset = 12;
        while (offset + 8 <= data.Length)
        {
            ReadOnlySpan<byte> id = data.Slice(offset, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            int body = offset + 8;
            if (body + (int)size > data.Length)
            {
                size = (uint)(data.Length - body); // tolerate a truncated trailing chunk
            }

            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                ReadOnlySpan<byte> fmt = data.Slice(body, (int)size);
                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(fmt[..2]);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.Slice(2, 2));
                sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(fmt.Slice(4, 4));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(fmt.Slice(14, 2));
                haveFmt = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                pcmChunk = data.Slice(body, (int)size);
                haveData = true;
            }

            offset = body + (int)size + ((int)size & 1); // chunks are word-aligned
        }

        if (!haveFmt || !haveData || channels <= 0 || sampleRate <= 0)
        {
            throw new InvalidDataException("WAVE file is missing a valid 'fmt ' or 'data' chunk.");
        }

        // WAVE_FORMAT_PCM = 1, WAVE_FORMAT_IEEE_FLOAT = 3, WAVE_FORMAT_EXTENSIBLE = 0xFFFE (treated as PCM here).
        if (audioFormat == 3 && bitsPerSample == 32)
        {
            ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(pcmChunk);
            short[] result = new short[floats.Length];
            for (int i = 0; i < floats.Length; i++)
            {
                result[i] = (short)Math.Clamp((int)MathF.Round(floats[i] * short.MaxValue), short.MinValue, short.MaxValue);
            }

            return result;
        }

        if (audioFormat is 1 or 0xFFFE)
        {
            switch (bitsPerSample)
            {
                case 16:
                    return MemoryMarshal.Cast<byte, short>(pcmChunk).ToArray();
                case 8:
                {
                    // 8-bit WAV PCM is unsigned (0..255, centered at 128); scale up to signed 16-bit.
                    short[] result = new short[pcmChunk.Length];
                    for (int i = 0; i < pcmChunk.Length; i++)
                    {
                        result[i] = (short)((pcmChunk[i] - 128) << 8);
                    }

                    return result;
                }
            }
        }

        throw new NotSupportedException($"Unsupported WAV encoding (format {audioFormat}, {bitsPerSample}-bit).");
    }
}
