using System.Numerics;
using Spot.Core;

namespace Spot.Net;

/// <summary>
/// Serializes the small set of value types that RPC arguments and <see cref="SyncVar{T}"/> values may use.
/// Each value is tagged with its type so the reader is self-describing (a receiver can read a value even
/// before it has resolved the target method or field). Unsupported types are reported and skipped.
/// </summary>
internal static class RpcSerializer
{
    private enum Tag : byte
    {
        Int,
        UInt,
        Float,
        Bool,
        String,
        Vector3,
        Quaternion,
        NetworkId,
        Byte,
        UShort,
        Unsupported = 255,
    }

    /// <summary>Writes a tagged value. Returns false (and logs) if the type is unsupported.</summary>
    public static bool WriteValue(NetWriter writer, object? value)
    {
        switch (value)
        {
            case int v: writer.WriteByte((byte)Tag.Int); writer.WriteInt(v); return true;
            case uint v: writer.WriteByte((byte)Tag.UInt); writer.WriteUInt(v); return true;
            case float v: writer.WriteByte((byte)Tag.Float); writer.WriteFloat(v); return true;
            case bool v: writer.WriteByte((byte)Tag.Bool); writer.WriteBool(v); return true;
            case string v: writer.WriteByte((byte)Tag.String); writer.WriteString(v); return true;
            case Vector3 v: writer.WriteByte((byte)Tag.Vector3); writer.WriteVector3(v); return true;
            case Quaternion v: writer.WriteByte((byte)Tag.Quaternion); writer.WriteQuaternion(v); return true;
            case NetworkId v: writer.WriteByte((byte)Tag.NetworkId); writer.WriteNetworkId(v); return true;
            case byte v: writer.WriteByte((byte)Tag.Byte); writer.WriteByte(v); return true;
            case ushort v: writer.WriteByte((byte)Tag.UShort); writer.WriteUInt16(v); return true;
            default:
                Log.CoreError("Spot.Net: unsupported RPC/SyncVar value type '{0}'.", value?.GetType().Name ?? "null");
                return false;
        }
    }

    /// <summary>Reads a tagged value. Returns false on overflow or an unknown tag.</summary>
    public static bool ReadValue(ref NetReader reader, out object? value)
    {
        value = null;
        var tag = (Tag)reader.ReadByte();
        if (reader.Overflow)
        {
            return false;
        }

        switch (tag)
        {
            case Tag.Int: value = reader.ReadInt(); break;
            case Tag.UInt: value = reader.ReadUInt(); break;
            case Tag.Float: value = reader.ReadFloat(); break;
            case Tag.Bool: value = reader.ReadBool(); break;
            case Tag.String: value = reader.ReadString(); break;
            case Tag.Vector3: value = reader.ReadVector3(); break;
            case Tag.Quaternion: value = reader.ReadQuaternion(); break;
            case Tag.NetworkId: value = reader.ReadNetworkId(); break;
            case Tag.Byte: value = reader.ReadByte(); break;
            case Tag.UShort: value = reader.ReadUInt16(); break;
            default:
                return false;
        }

        return !reader.Overflow;
    }

    /// <summary>Writes an argument list (count-prefixed, each tagged). Returns false if any arg is unsupported.</summary>
    public static bool WriteArgs(NetWriter writer, object[] args)
    {
        writer.WriteByte((byte)Math.Min(args.Length, byte.MaxValue));
        foreach (object arg in args)
        {
            if (!WriteValue(writer, arg))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads an argument list written by <see cref="WriteArgs"/>. Returns false on any malformed value.</summary>
    public static bool ReadArgs(ref NetReader reader, out object?[] args)
    {
        byte count = reader.ReadByte();
        if (reader.Overflow)
        {
            args = Array.Empty<object?>();
            return false;
        }

        var result = new object?[count];
        for (int i = 0; i < count; i++)
        {
            if (!ReadValue(ref reader, out object? value))
            {
                args = Array.Empty<object?>();
                return false;
            }

            result[i] = value;
        }

        args = result;
        return true;
    }
}
