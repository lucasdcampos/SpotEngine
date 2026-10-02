using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Spot.IO;

namespace Spot.Engine.Tests.Fakes;

/// <summary>
/// Builds small, valid media files in memory (WAV, PNG) so decoding and loading paths can be tested without
/// checked-in fixtures.
/// </summary>
internal static class TestMedia
{
    /// <summary>Little-endian bytes of 16-bit samples.</summary>
    public static byte[] ToBytes(short[] pcm)
    {
        byte[] bytes = new byte[pcm.Length * 2];
        for (int i = 0; i < pcm.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), pcm[i]);
        }

        return bytes;
    }

    /// <summary>A minimal canonical WAVE file (RIFF/fmt /data) around raw sample bytes.</summary>
    public static byte[] Wav(byte[] data, ushort bitsPerSample, ushort channels, uint sampleRate)
    {
        ushort blockAlign = (ushort)(channels * (bitsPerSample / 8));
        uint byteRate = sampleRate * blockAlign;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36u + (uint)data.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16u);                 // PCM fmt chunk size
        w.Write((ushort)1);           // WAVE_FORMAT_PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write("data"u8);
        w.Write((uint)data.Length);
        w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>A 16-bit PCM WAVE file.</summary>
    public static byte[] Wav16(short[] pcm, ushort channels, uint sampleRate) =>
        Wav(ToBytes(pcm), 16, channels, sampleRate);

    /// <summary>
    /// A truecolor+alpha PNG of the given RGBA pixels, rows top-to-bottom (the file order), unfiltered.
    /// </summary>
    public static byte[] Png(int width, int height, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: RGBA
        WriteChunk(ms, "IHDR", ihdr);

        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter: none
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(z);
        }

        WriteChunk(ms, "IDAT", compressed.ToArray());
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(u32, data.Length);
        stream.Write(u32);

        byte[] typeAndData = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);
        stream.Write(typeAndData);

        BinaryPrimitives.WriteUInt32BigEndian(u32, Crc32(typeAndData));
        stream.Write(u32);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}

/// <summary>
/// An in-memory <see cref="IFileSystem"/>, standing in for the browser's fetched content store. Install with
/// <see cref="Install"/>; disposing restores the disk and clears any path resolver.
/// </summary>
internal sealed class InMemoryFileSystem : IFileSystem, IDisposable
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Reads { get; } = new();

    public static InMemoryFileSystem Install()
    {
        var fs = new InMemoryFileSystem();
        FileSystem.Current = fs;
        return fs;
    }

    public InMemoryFileSystem Add(string path, byte[] bytes)
    {
        _files[path] = bytes;
        return this;
    }

    public bool Exists(string path) => _files.ContainsKey(path);

    public byte[] ReadAllBytes(string path)
    {
        Reads.Add(path);
        return _files.TryGetValue(path, out byte[]? bytes) ? bytes : throw new FileNotFoundException(path, path);
    }

    public string ReadAllText(string path) => System.Text.Encoding.UTF8.GetString(ReadAllBytes(path));

    public void Dispose()
    {
        FileSystem.Current = null!;
        FileSystem.PathResolver = null;
    }
}
