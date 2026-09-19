using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Spot.Net;

/// <summary>
/// Writes primitives and common math types into a growable little-endian byte buffer, following the same
/// binary convention as the engine's cooked asset formats. Paired with <see cref="NetReader"/>. Not
/// thread-safe; build a message on one thread and hand the bytes to the transport.
/// </summary>
public sealed class NetWriter
{
    private byte[] _buffer;
    private int _length;

    /// <summary>Creates a writer with an initial capacity.</summary>
    /// <param name="capacity">The initial buffer size in bytes.</param>
    public NetWriter(int capacity = 64)
    {
        _buffer = new byte[Math.Max(capacity, 4)];
        _length = 0;
    }

    /// <summary>The number of bytes written so far.</summary>
    public int Length => _length;

    /// <summary>The bytes written so far, as a span. Valid until the next write.</summary>
    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);

    /// <summary>Copies the written bytes into a new array.</summary>
    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

    /// <summary>Resets the writer to empty, keeping its buffer for reuse.</summary>
    public void Reset() => _length = 0;

    /// <summary>Writes a message-type tag byte.</summary>
    public void WriteMessageType(MessageType type) => WriteByte((byte)type);

    /// <summary>Writes a single byte.</summary>
    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    /// <summary>Writes a boolean as one byte.</summary>
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Writes a 16-bit unsigned integer.</summary>
    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length), value);
        _length += 2;
    }

    /// <summary>Writes a 32-bit signed integer.</summary>
    public void WriteInt(int value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_length), value);
        _length += 4;
    }

    /// <summary>Writes a 32-bit unsigned integer.</summary>
    public void WriteUInt(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_length), value);
        _length += 4;
    }

    /// <summary>Writes a single-precision float.</summary>
    public void WriteFloat(float value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_length), value);
        _length += 4;
    }

    /// <summary>Writes a length-prefixed UTF-8 string (null treated as empty).</summary>
    public void WriteString(string? value)
    {
        value ??= string.Empty;
        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteUInt16((ushort)Math.Min(byteCount, ushort.MaxValue));
        EnsureCapacity(byteCount);
        Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_length));
        _length += byteCount;
    }

    /// <summary>Writes a network id as a 32-bit unsigned integer.</summary>
    public void WriteNetworkId(NetworkId id) => WriteUInt(id.Value);

    /// <summary>Writes a 3-component vector.</summary>
    public void WriteVector3(Vector3 v)
    {
        WriteFloat(v.X);
        WriteFloat(v.Y);
        WriteFloat(v.Z);
    }

    /// <summary>Writes a quaternion (x, y, z, w).</summary>
    public void WriteQuaternion(Quaternion q)
    {
        WriteFloat(q.X);
        WriteFloat(q.Y);
        WriteFloat(q.Z);
        WriteFloat(q.W);
    }

    /// <summary>Writes a raw block of bytes (no length prefix).</summary>
    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
    }

    private void EnsureCapacity(int extra)
    {
        int required = _length + extra;
        if (required <= _buffer.Length)
        {
            return;
        }

        int newSize = _buffer.Length * 2;
        while (newSize < required)
        {
            newSize *= 2;
        }

        Array.Resize(ref _buffer, newSize);
    }
}
