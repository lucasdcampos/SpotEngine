using System.Numerics;
using Spot.Net;

namespace Spot.Net.Tests;

public class SerializationTests
{
    [Fact]
    public void RoundTrips_AllPrimitiveAndMathTypes()
    {
        var writer = new NetWriter();
        writer.WriteMessageType(MessageType.Spawn);
        writer.WriteByte(200);
        writer.WriteBool(true);
        writer.WriteUInt16(40000);
        writer.WriteInt(-123456);
        writer.WriteUInt(3_000_000_000u);
        writer.WriteFloat(3.14159f);
        writer.WriteString("héllo network");
        writer.WriteNetworkId(new NetworkId(42));
        writer.WriteVector3(new Vector3(1f, -2f, 3.5f));
        writer.WriteQuaternion(new Quaternion(0.1f, 0.2f, 0.3f, 0.4f));

        var reader = new NetReader(writer.ToArray());

        Assert.Equal(MessageType.Spawn, reader.ReadMessageType());
        Assert.Equal(200, reader.ReadByte());
        Assert.True(reader.ReadBool());
        Assert.Equal(40000, reader.ReadUInt16());
        Assert.Equal(-123456, reader.ReadInt());
        Assert.Equal(3_000_000_000u, reader.ReadUInt());
        Assert.Equal(3.14159f, reader.ReadFloat(), 5);
        Assert.Equal("héllo network", reader.ReadString());
        Assert.Equal(new NetworkId(42), reader.ReadNetworkId());
        Assert.Equal(new Vector3(1f, -2f, 3.5f), reader.ReadVector3());
        Assert.Equal(new Quaternion(0.1f, 0.2f, 0.3f, 0.4f), reader.ReadQuaternion());
        Assert.False(reader.Overflow);
    }

    [Fact]
    public void EmptyString_RoundTrips()
    {
        var writer = new NetWriter();
        writer.WriteString(string.Empty);
        writer.WriteString(null);

        var reader = new NetReader(writer.ToArray());
        Assert.Equal(string.Empty, reader.ReadString());
        Assert.Equal(string.Empty, reader.ReadString());
        Assert.False(reader.Overflow);
    }

    [Fact]
    public void TruncatedBuffer_SetsOverflow_AndNeverThrows()
    {
        var writer = new NetWriter();
        writer.WriteInt(7);
        writer.WriteFloat(1.5f);

        // Only 6 of the 8 written bytes: reading past the end must degrade, not throw.
        byte[] truncated = writer.ToArray()[..6];
        var reader = new NetReader(truncated);

        Assert.Equal(7, reader.ReadInt());
        Assert.False(reader.Overflow);

        float value = reader.ReadFloat(); // only 2 bytes remain
        Assert.Equal(0f, value);
        Assert.True(reader.Overflow);

        // Further reads keep returning defaults with overflow latched.
        Assert.Equal(0, reader.ReadByte());
        Assert.True(reader.Overflow);
    }

    [Fact]
    public void ReadingEmptyBuffer_IsSafe()
    {
        var reader = new NetReader(Array.Empty<byte>());
        Assert.Equal(MessageType.Raw, reader.ReadMessageType());
        Assert.True(reader.Overflow);
    }
}
