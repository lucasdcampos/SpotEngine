using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Spot.Rendering;

namespace Spot.Assets;

/// <summary>
/// Reads and writes <c>.spmesh</c>, the engine-native cooked mesh format: a straight serialization of the
/// interleaved vertex/index data the importers already produce (<see cref="MeshData"/>). Parsing is pure
/// computation with no GL calls, so a cooked mesh can be read on a background thread and turned into a
/// drawable <see cref="Mesh"/> on the render thread — exactly the split the async model path already uses.
/// </summary>
/// <remarks>
/// Layout (little-endian): magic <c>'S','P','M','H'</c>, <c>u32 version</c>, <c>u32 submeshCount</c>, then per
/// submesh <c>u32 vertexFloatCount</c>, <c>u32 indexCount</c>, the vertex floats, and the index uints.
/// All multi-byte values are little-endian, matching every runtime identifier the engine ships to.
/// </remarks>
public static class SpMesh
{
    private static ReadOnlySpan<byte> Magic => "SPMH"u8;

    /// <summary>The format version this build writes and is able to read.</summary>
    public const uint Version = 1;

    /// <summary>Serializes cooked submeshes to a <c>.spmesh</c> byte blob.</summary>
    /// <param name="submeshes">The CPU geometry to write, one entry per submesh.</param>
    /// <returns>The encoded bytes, ready to write to disk.</returns>
    public static byte[] Write(IReadOnlyList<MeshData> submeshes)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(Magic);
        w.Write(Version);
        w.Write((uint)submeshes.Count);

        foreach (MeshData submesh in submeshes)
        {
            w.Write((uint)submesh.Vertices.Length);
            w.Write((uint)submesh.Indices.Length);
            w.Write(MemoryMarshal.AsBytes(submesh.Vertices.AsSpan()));
            w.Write(MemoryMarshal.AsBytes(submesh.Indices.AsSpan()));
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Parses a <c>.spmesh</c> blob back into CPU geometry. Safe to call off the render thread.</summary>
    /// <param name="bytes">The encoded bytes.</param>
    /// <returns>The decoded submeshes.</returns>
    /// <exception cref="InvalidDataException">The blob is truncated or not a supported <c>.spmesh</c>.</exception>
    public static IReadOnlyList<MeshData> Read(ReadOnlySpan<byte> bytes)
    {
        var cursor = new Cursor(bytes);
        cursor.ExpectMagic(Magic, "spmesh");

        uint version = cursor.ReadUInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported .spmesh version {version}; expected {Version}.");
        }

        uint submeshCount = cursor.ReadUInt32();
        var submeshes = new List<MeshData>((int)submeshCount);
        for (uint i = 0; i < submeshCount; i++)
        {
            uint vertexFloatCount = cursor.ReadUInt32();
            uint indexCount = cursor.ReadUInt32();
            float[] vertices = cursor.ReadFloats(vertexFloatCount);
            uint[] indices = cursor.ReadUInts(indexCount);
            submeshes.Add(new MeshData(vertices, indices));
        }

        return submeshes;
    }

    /// <summary>Reads and parses a <c>.spmesh</c> file. Safe to call off the render thread.</summary>
    /// <param name="path">The absolute path to the cooked mesh file.</param>
    /// <returns>The decoded submeshes.</returns>
    public static IReadOnlyList<MeshData> ReadFile(string path) => Read(File.ReadAllBytes(path));
}

/// <summary>The decoded contents of a <c>.sptex</c>: raw RGBA pixels plus the sampling hint.</summary>
public readonly struct SpTexData
{
    /// <summary>Initializes decoded texture pixels and their dimensions.</summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="rgba">Raw RGBA8 pixels, <c>width * height * 4</c> bytes.</param>
    /// <param name="pointFilter">Whether the texture should sample with nearest-neighbour filtering.</param>
    public SpTexData(uint width, uint height, byte[] rgba, bool pointFilter)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        PointFilter = pointFilter;
    }

    /// <summary>Gets the texture width in pixels.</summary>
    public uint Width { get; }

    /// <summary>Gets the texture height in pixels.</summary>
    public uint Height { get; }

    /// <summary>Gets the raw RGBA8 pixels (<see cref="Width"/> * <see cref="Height"/> * 4 bytes).</summary>
    public byte[] Rgba { get; }

    /// <summary>Gets whether the texture prefers nearest-neighbour (point) filtering.</summary>
    public bool PointFilter { get; }
}

/// <summary>
/// Reads and writes <c>.sptex</c>, the engine-native cooked texture format: raw RGBA pixels decoded once at
/// import time so the runtime uploads them verbatim with no image decoder. Mipmaps are generated on GPU
/// upload, as they already are for source textures.
/// </summary>
/// <remarks>
/// Layout (little-endian): magic <c>'S','P','T','X'</c>, <c>u32 version</c>, <c>u32 width</c>, <c>u32 height</c>,
/// <c>u32 format</c> (0 = RGBA8; reserved for future block-compressed / sRGB formats), <c>u32 flags</c>
/// (bit 0 = point filter), then <c>width * height * 4</c> bytes of RGBA pixels.
/// </remarks>
public static class SpTex
{
    private static ReadOnlySpan<byte> Magic => "SPTX"u8;

    /// <summary>The format version this build writes and is able to read.</summary>
    public const uint Version = 1;

    private const uint FormatRgba8 = 0;
    private const uint FlagPointFilter = 1u << 0;

    /// <summary>Serializes decoded RGBA pixels to a <c>.sptex</c> byte blob.</summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="rgba">Raw RGBA8 pixels, <c>width * height * 4</c> bytes.</param>
    /// <param name="pointFilter">Whether the texture should sample with nearest-neighbour filtering.</param>
    /// <returns>The encoded bytes, ready to write to disk.</returns>
    public static byte[] Write(uint width, uint height, ReadOnlySpan<byte> rgba, bool pointFilter)
    {
        long expected = (long)width * height * 4;
        if (rgba.Length != expected)
        {
            throw new ArgumentException($"Expected {expected} RGBA bytes for {width}x{height}, got {rgba.Length}.", nameof(rgba));
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(Magic);
        w.Write(Version);
        w.Write(width);
        w.Write(height);
        w.Write(FormatRgba8);
        w.Write(pointFilter ? FlagPointFilter : 0u);
        w.Write(rgba);

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Parses a <c>.sptex</c> blob back into RGBA pixels.</summary>
    /// <param name="bytes">The encoded bytes.</param>
    /// <returns>The decoded texture.</returns>
    /// <exception cref="InvalidDataException">The blob is truncated or not a supported <c>.sptex</c>.</exception>
    public static SpTexData Read(ReadOnlySpan<byte> bytes)
    {
        var cursor = new Cursor(bytes);
        cursor.ExpectMagic(Magic, "sptex");

        uint version = cursor.ReadUInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported .sptex version {version}; expected {Version}.");
        }

        uint width = cursor.ReadUInt32();
        uint height = cursor.ReadUInt32();
        uint format = cursor.ReadUInt32();
        if (format != FormatRgba8)
        {
            throw new InvalidDataException($"Unsupported .sptex pixel format {format}; expected RGBA8 ({FormatRgba8}).");
        }

        uint flags = cursor.ReadUInt32();
        byte[] rgba = cursor.ReadBytes(width * height * 4);
        return new SpTexData(width, height, rgba, (flags & FlagPointFilter) != 0);
    }

    /// <summary>Reads and parses a <c>.sptex</c> file.</summary>
    /// <param name="path">The absolute path to the cooked texture file.</param>
    /// <returns>The decoded texture.</returns>
    public static SpTexData ReadFile(string path) => Read(File.ReadAllBytes(path));
}

/// <summary>A forward-only reader over a cooked-asset byte blob that validates bounds as it goes.</summary>
internal ref struct Cursor
{
    private readonly ReadOnlySpan<byte> _bytes;
    private int _offset;

    public Cursor(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        _offset = 0;
    }

    public void ExpectMagic(ReadOnlySpan<byte> magic, string format)
    {
        if (_bytes.Length < magic.Length || !_bytes[..magic.Length].SequenceEqual(magic))
        {
            throw new InvalidDataException($"Not a .{format} file: bad magic header.");
        }

        _offset += magic.Length;
    }

    public uint ReadUInt32()
    {
        ReadOnlySpan<byte> slice = Take(sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(slice);
    }

    public float[] ReadFloats(uint count)
    {
        ReadOnlySpan<byte> slice = Take(checked((int)count * sizeof(float)));
        return MemoryMarshal.Cast<byte, float>(slice).ToArray();
    }

    public uint[] ReadUInts(uint count)
    {
        ReadOnlySpan<byte> slice = Take(checked((int)count * sizeof(uint)));
        return MemoryMarshal.Cast<byte, uint>(slice).ToArray();
    }

    public byte[] ReadBytes(uint count)
    {
        return Take(checked((int)count)).ToArray();
    }

    private ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || _offset + length > _bytes.Length)
        {
            throw new InvalidDataException("Cooked asset is truncated.");
        }

        ReadOnlySpan<byte> slice = _bytes.Slice(_offset, length);
        _offset += length;
        return slice;
    }
}
