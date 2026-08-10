using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Spot.Animation;
using Spot.Rendering;

namespace Spot.Assets;

/// <summary>
/// Reads and writes <c>.spmesh</c>, the engine-native cooked mesh format: a serialization of the interleaved
/// vertex/index data the importers produce (<see cref="MeshData"/>) together with any skinning bones and
/// animation clips (<see cref="CookedModel"/>). Parsing is pure computation with no GL calls, so a cooked
/// model can be read on a background thread and turned into a drawable <see cref="Model"/> on the render
/// thread — exactly the split the async model path already uses.
/// </summary>
/// <remarks>
/// Layout (little-endian): magic <c>'S','P','M','H'</c>, <c>u32 version</c>, <c>u32 submeshCount</c>, then per
/// submesh <c>u32 floatsPerVertex</c>, <c>u32 vertexFloatCount</c>, <c>u32 indexCount</c>, the vertex floats,
/// the index uints, <c>u32 boneCount</c> and, per bone, a length-prefixed UTF-8 name plus its 16-float
/// inverse-bind matrix; then <c>u32 clipCount</c> and, per clip, a name, <c>float duration</c>,
/// <c>u32 channelCount</c> and per channel a node name and its position/rotation/scale key arrays. Version 1
/// omits <c>floatsPerVertex</c>, bones and clips (rigid meshes only) and is still read for older cooked assets.
/// </remarks>
public static class SpMesh
{
    private static ReadOnlySpan<byte> Magic => "SPMH"u8;

    /// <summary>The format version this build writes.</summary>
    public const uint Version = 2;

    /// <summary>Serializes cooked submeshes (rigid only) to a <c>.spmesh</c> byte blob.</summary>
    /// <param name="submeshes">The CPU geometry to write, one entry per submesh.</param>
    /// <returns>The encoded bytes, ready to write to disk.</returns>
    public static byte[] Write(IReadOnlyList<MeshData> submeshes) => Write(new CookedModel(submeshes, null));

    /// <summary>Serializes a cooked model (geometry + skinning + clips) to a <c>.spmesh</c> byte blob.</summary>
    /// <param name="model">The CPU model data to write.</param>
    /// <returns>The encoded bytes, ready to write to disk.</returns>
    public static byte[] Write(CookedModel model)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(Magic);
        w.Write(Version);
        w.Write((uint)model.Submeshes.Count);

        foreach (MeshData submesh in model.Submeshes)
        {
            int floatsPerVertex = submesh.Skinned ? Mesh.SkinnedFloatsPerVertex : Mesh.FloatsPerVertex;
            w.Write((uint)floatsPerVertex);
            w.Write((uint)submesh.Vertices.Length);
            w.Write((uint)submesh.Indices.Length);
            w.Write(MemoryMarshal.AsBytes(submesh.Vertices.AsSpan()));
            w.Write(MemoryMarshal.AsBytes(submesh.Indices.AsSpan()));

            IReadOnlyList<BoneInfo> bones = submesh.Bones ?? Array.Empty<BoneInfo>();
            w.Write((uint)bones.Count);
            foreach (BoneInfo bone in bones)
            {
                WriteString(w, bone.Name);
                WriteMatrix(w, bone.InverseBind);
            }
        }

        w.Write((uint)model.Animations.Count);
        foreach (AnimationClip clip in model.Animations)
        {
            WriteString(w, clip.Name);
            w.Write(clip.Duration);
            w.Write((uint)clip.Channels.Count);
            foreach (AnimationChannel channel in clip.Channels)
            {
                WriteString(w, channel.NodeName);
                WriteVectorKeys(w, channel.PositionKeys);
                WriteQuaternionKeys(w, channel.RotationKeys);
                WriteVectorKeys(w, channel.ScaleKeys);
            }
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Parses a <c>.spmesh</c> blob back into CPU geometry. Safe to call off the render thread.</summary>
    /// <param name="bytes">The encoded bytes.</param>
    /// <returns>The decoded submeshes.</returns>
    /// <exception cref="InvalidDataException">The blob is truncated or not a supported <c>.spmesh</c>.</exception>
    public static IReadOnlyList<MeshData> Read(ReadOnlySpan<byte> bytes) => ReadModel(bytes).Submeshes;

    /// <summary>Parses a <c>.spmesh</c> blob into a full cooked model. Safe to call off the render thread.</summary>
    /// <param name="bytes">The encoded bytes.</param>
    /// <returns>The decoded model (geometry, skinning and clips).</returns>
    /// <exception cref="InvalidDataException">The blob is truncated or not a supported <c>.spmesh</c>.</exception>
    public static CookedModel ReadModel(ReadOnlySpan<byte> bytes)
    {
        var cursor = new Cursor(bytes);
        cursor.ExpectMagic(Magic, "spmesh");

        uint version = cursor.ReadUInt32();
        if (version != 1 && version != 2)
        {
            throw new InvalidDataException($"Unsupported .spmesh version {version}; expected 1 or 2.");
        }

        uint submeshCount = cursor.ReadUInt32();
        var submeshes = new List<MeshData>((int)submeshCount);
        for (uint i = 0; i < submeshCount; i++)
        {
            // Version 1 has no per-submesh floatsPerVertex and no bones — every mesh is rigid.
            uint floatsPerVertex = version >= 2 ? cursor.ReadUInt32() : (uint)Mesh.FloatsPerVertex;
            uint vertexFloatCount = cursor.ReadUInt32();
            uint indexCount = cursor.ReadUInt32();
            float[] vertices = cursor.ReadFloats(vertexFloatCount);
            uint[] indices = cursor.ReadUInts(indexCount);

            IReadOnlyList<BoneInfo>? bones = null;
            if (version >= 2)
            {
                uint boneCount = cursor.ReadUInt32();
                if (boneCount > 0)
                {
                    var list = new BoneInfo[boneCount];
                    for (uint b = 0; b < boneCount; b++)
                    {
                        string name = cursor.ReadString();
                        Matrix4x4 inverseBind = cursor.ReadMatrix4x4();
                        list[b] = new BoneInfo(name, inverseBind);
                    }

                    bones = list;
                }
            }

            bool skinned = floatsPerVertex == (uint)Mesh.SkinnedFloatsPerVertex;
            submeshes.Add(new MeshData(vertices, indices, skinned, bones));
        }

        IReadOnlyList<AnimationClip> animations = Array.Empty<AnimationClip>();
        if (version >= 2)
        {
            uint clipCount = cursor.ReadUInt32();
            if (clipCount > 0)
            {
                var clips = new AnimationClip[clipCount];
                for (uint i = 0; i < clipCount; i++)
                {
                    string name = cursor.ReadString();
                    float duration = cursor.ReadSingle();
                    uint channelCount = cursor.ReadUInt32();
                    var channels = new AnimationChannel[channelCount];
                    for (uint c = 0; c < channelCount; c++)
                    {
                        string node = cursor.ReadString();
                        Keyframe<Vector3>[] positionKeys = ReadVectorKeys(ref cursor);
                        Keyframe<Quaternion>[] rotationKeys = ReadQuaternionKeys(ref cursor);
                        Keyframe<Vector3>[] scaleKeys = ReadVectorKeys(ref cursor);
                        channels[c] = new AnimationChannel(node, positionKeys, rotationKeys, scaleKeys);
                    }

                    clips[i] = new AnimationClip(name, duration, channels);
                }

                animations = clips;
            }
        }

        return new CookedModel(submeshes, animations);
    }

    /// <summary>Reads and parses a <c>.spmesh</c> file into CPU geometry. Safe to call off the render thread.</summary>
    /// <param name="path">The absolute path to the cooked mesh file.</param>
    /// <returns>The decoded submeshes.</returns>
    public static IReadOnlyList<MeshData> ReadFile(string path) => Read(File.ReadAllBytes(path));

    /// <summary>Reads and parses a <c>.spmesh</c> file into a full cooked model. Safe to call off the render thread.</summary>
    /// <param name="path">The absolute path to the cooked mesh file.</param>
    /// <returns>The decoded model (geometry, skinning and clips).</returns>
    public static CookedModel ReadModelFile(string path) => ReadModel(File.ReadAllBytes(path));

    private static void WriteString(BinaryWriter w, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        w.Write((uint)bytes.Length);
        w.Write(bytes);
    }

    private static void WriteMatrix(BinaryWriter w, Matrix4x4 m)
    {
        w.Write(m.M11); w.Write(m.M12); w.Write(m.M13); w.Write(m.M14);
        w.Write(m.M21); w.Write(m.M22); w.Write(m.M23); w.Write(m.M24);
        w.Write(m.M31); w.Write(m.M32); w.Write(m.M33); w.Write(m.M34);
        w.Write(m.M41); w.Write(m.M42); w.Write(m.M43); w.Write(m.M44);
    }

    private static void WriteVectorKeys(BinaryWriter w, IReadOnlyList<Keyframe<Vector3>> keys)
    {
        w.Write((uint)keys.Count);
        foreach (Keyframe<Vector3> key in keys)
        {
            w.Write(key.Time);
            w.Write(key.Value.X);
            w.Write(key.Value.Y);
            w.Write(key.Value.Z);
        }
    }

    private static void WriteQuaternionKeys(BinaryWriter w, IReadOnlyList<Keyframe<Quaternion>> keys)
    {
        w.Write((uint)keys.Count);
        foreach (Keyframe<Quaternion> key in keys)
        {
            w.Write(key.Time);
            w.Write(key.Value.X);
            w.Write(key.Value.Y);
            w.Write(key.Value.Z);
            w.Write(key.Value.W);
        }
    }

    private static Keyframe<Vector3>[] ReadVectorKeys(ref Cursor cursor)
    {
        uint count = cursor.ReadUInt32();
        var keys = new Keyframe<Vector3>[count];
        for (uint i = 0; i < count; i++)
        {
            float time = cursor.ReadSingle();
            var value = new Vector3(cursor.ReadSingle(), cursor.ReadSingle(), cursor.ReadSingle());
            keys[i] = new Keyframe<Vector3>(time, value);
        }

        return keys;
    }

    private static Keyframe<Quaternion>[] ReadQuaternionKeys(ref Cursor cursor)
    {
        uint count = cursor.ReadUInt32();
        var keys = new Keyframe<Quaternion>[count];
        for (uint i = 0; i < count; i++)
        {
            float time = cursor.ReadSingle();
            var value = new Quaternion(cursor.ReadSingle(), cursor.ReadSingle(), cursor.ReadSingle(), cursor.ReadSingle());
            keys[i] = new Keyframe<Quaternion>(time, value);
        }

        return keys;
    }
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

/// <summary>The decoded contents of a <c>.spaudio</c>: interleaved 16-bit PCM plus its channel and rate.</summary>
public readonly struct SpAudioData
{
    /// <summary>Initializes decoded PCM audio.</summary>
    /// <param name="pcm">Interleaved signed 16-bit PCM samples (one short per sample, channels interleaved).</param>
    /// <param name="channels">Channel count (1 = mono, 2 = stereo).</param>
    /// <param name="sampleRate">Sample rate in frames per second (e.g. 44100).</param>
    public SpAudioData(short[] pcm, int channels, int sampleRate)
    {
        Pcm = pcm;
        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>Gets the interleaved signed 16-bit PCM samples.</summary>
    public short[] Pcm { get; }

    /// <summary>Gets the channel count (1 = mono, 2 = stereo).</summary>
    public int Channels { get; }

    /// <summary>Gets the sample rate in frames per second.</summary>
    public int SampleRate { get; }
}

/// <summary>
/// Reads and writes <c>.spaudio</c>, the engine-native cooked audio format: interleaved 16-bit PCM decoded
/// once at import time so the runtime uploads it straight to an OpenAL buffer with no audio decoder present.
/// Parsing is pure computation, so a cooked clip can be read off the render thread, mirroring the split the
/// texture and mesh formats already use.
/// </summary>
/// <remarks>
/// Layout (little-endian): magic <c>'S','P','A','U'</c>, <c>u32 version</c>, <c>u16 channels</c>,
/// <c>u16 bitsPerSample</c> (16), <c>u32 sampleRate</c>, <c>u32 sampleCount</c> (total interleaved shorts),
/// then <c>sampleCount</c> signed 16-bit samples.
/// </remarks>
public static class SpAudio
{
    private static ReadOnlySpan<byte> Magic => "SPAU"u8;

    /// <summary>The format version this build writes and is able to read.</summary>
    public const uint Version = 1;

    private const ushort BitsPerSample = 16;

    /// <summary>Serializes decoded PCM to a <c>.spaudio</c> byte blob.</summary>
    /// <param name="channels">Channel count (1 = mono, 2 = stereo).</param>
    /// <param name="sampleRate">Sample rate in frames per second.</param>
    /// <param name="pcm">Interleaved signed 16-bit PCM samples.</param>
    /// <returns>The encoded bytes, ready to write to disk.</returns>
    public static byte[] Write(int channels, int sampleRate, ReadOnlySpan<short> pcm)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(Magic);
        w.Write(Version);
        w.Write((ushort)channels);
        w.Write(BitsPerSample);
        w.Write((uint)sampleRate);
        w.Write((uint)pcm.Length);
        w.Write(MemoryMarshal.AsBytes(pcm));

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Parses a <c>.spaudio</c> blob back into PCM. Safe to call off the render thread.</summary>
    /// <param name="bytes">The encoded bytes.</param>
    /// <returns>The decoded audio.</returns>
    /// <exception cref="InvalidDataException">The blob is truncated or not a supported <c>.spaudio</c>.</exception>
    public static SpAudioData Read(ReadOnlySpan<byte> bytes)
    {
        var cursor = new Cursor(bytes);
        cursor.ExpectMagic(Magic, "spaudio");

        uint version = cursor.ReadUInt32();
        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported .spaudio version {version}; expected {Version}.");
        }

        ushort channels = cursor.ReadUInt16();
        ushort bits = cursor.ReadUInt16();
        if (bits != BitsPerSample)
        {
            throw new InvalidDataException($"Unsupported .spaudio sample depth {bits}; expected {BitsPerSample}-bit.");
        }

        uint sampleRate = cursor.ReadUInt32();
        uint sampleCount = cursor.ReadUInt32();
        short[] pcm = cursor.ReadShorts(sampleCount);
        return new SpAudioData(pcm, channels, (int)sampleRate);
    }

    /// <summary>Reads and parses a <c>.spaudio</c> file. Safe to call off the render thread.</summary>
    /// <param name="path">The absolute path to the cooked audio file.</param>
    /// <returns>The decoded audio.</returns>
    public static SpAudioData ReadFile(string path) => Read(File.ReadAllBytes(path));
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

    public ushort ReadUInt16()
    {
        ReadOnlySpan<byte> slice = Take(sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(slice);
    }

    public float ReadSingle()
    {
        ReadOnlySpan<byte> slice = Take(sizeof(float));
        return BinaryPrimitives.ReadSingleLittleEndian(slice);
    }

    public string ReadString()
    {
        uint length = ReadUInt32();
        ReadOnlySpan<byte> slice = Take(checked((int)length));
        return Encoding.UTF8.GetString(slice);
    }

    public Matrix4x4 ReadMatrix4x4()
    {
        return new Matrix4x4(
            ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
            ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
            ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
            ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
    }

    public short[] ReadShorts(uint count)
    {
        ReadOnlySpan<byte> slice = Take(checked((int)count * sizeof(short)));
        return MemoryMarshal.Cast<byte, short>(slice).ToArray();
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
