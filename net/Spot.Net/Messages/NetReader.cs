using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Spot.Net;

/// <summary>
/// Reads primitives and common math types out of a little-endian byte buffer written by
/// <see cref="NetWriter"/>. Every read is bounds-checked: reading past the end never throws — it returns a
/// default value and sets <see cref="Overflow"/>. Network bytes are untrusted, so handlers should check
/// <see cref="Overflow"/> before acting on parsed data, honoring the engine's "never crash" rule.
/// </summary>
public struct NetReader
{
    private readonly ReadOnlyMemory<byte> _buffer;
    private int _position;

    /// <summary>Creates a reader over a message buffer.</summary>
    /// <param name="buffer">The bytes to read.</param>
    public NetReader(ReadOnlyMemory<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
        Overflow = false;
    }

    /// <summary>Whether a read ran past the end of the buffer. Once set, further reads return defaults.</summary>
    public bool Overflow { get; private set; }

    /// <summary>The number of unread bytes remaining.</summary>
    public readonly int Remaining => _buffer.Length - _position;

    /// <summary>Reads a message-type tag byte.</summary>
    public MessageType ReadMessageType() => (MessageType)ReadByte();

    /// <summary>Reads a single byte, or <c>0</c> on overflow.</summary>
    public byte ReadByte()
    {
        if (!Require(1))
        {
            return 0;
        }

        return _buffer.Span[_position++];
    }

    /// <summary>Reads a boolean.</summary>
    public bool ReadBool() => ReadByte() != 0;

    /// <summary>Reads a 16-bit unsigned integer, or <c>0</c> on overflow.</summary>
    public ushort ReadUInt16()
    {
        if (!Require(2))
        {
            return 0;
        }

        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Span.Slice(_position));
        _position += 2;
        return value;
    }

    /// <summary>Reads a 32-bit signed integer, or <c>0</c> on overflow.</summary>
    public int ReadInt()
    {
        if (!Require(4))
        {
            return 0;
        }

        int value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Span.Slice(_position));
        _position += 4;
        return value;
    }

    /// <summary>Reads a 32-bit unsigned integer, or <c>0</c> on overflow.</summary>
    public uint ReadUInt()
    {
        if (!Require(4))
        {
            return 0;
        }

        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Span.Slice(_position));
        _position += 4;
        return value;
    }

    /// <summary>Reads a single-precision float, or <c>0</c> on overflow.</summary>
    public float ReadFloat()
    {
        if (!Require(4))
        {
            return 0f;
        }

        float value = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Span.Slice(_position));
        _position += 4;
        return value;
    }

    /// <summary>Reads a length-prefixed UTF-8 string, or empty on overflow.</summary>
    public string ReadString()
    {
        ushort byteCount = ReadUInt16();
        if (byteCount == 0 || !Require(byteCount))
        {
            return string.Empty;
        }

        string value = Encoding.UTF8.GetString(_buffer.Span.Slice(_position, byteCount));
        _position += byteCount;
        return value;
    }

    /// <summary>Reads a network id.</summary>
    public NetworkId ReadNetworkId() => new NetworkId(ReadUInt());

    /// <summary>Reads a 3-component vector.</summary>
    public Vector3 ReadVector3()
    {
        float x = ReadFloat();
        float y = ReadFloat();
        float z = ReadFloat();
        return new Vector3(x, y, z);
    }

    /// <summary>Reads a quaternion (x, y, z, w).</summary>
    public Quaternion ReadQuaternion()
    {
        float x = ReadFloat();
        float y = ReadFloat();
        float z = ReadFloat();
        float w = ReadFloat();
        return new Quaternion(x, y, z, w);
    }

    // Returns whether count more bytes are available, latching Overflow once the buffer is exhausted so a
    // truncated or malformed message degrades to defaults instead of throwing.
    private bool Require(int count)
    {
        if (Overflow || _position + count > _buffer.Length)
        {
            Overflow = true;
            return false;
        }

        return true;
    }
}
