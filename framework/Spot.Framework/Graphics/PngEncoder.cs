using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Spot.Framework.Graphics;

// A minimal PNG writer for 8-bit RGBA: one IDAT of zlib-compressed scanlines, each filtered with "Sub", which
// keeps flat and gradient images small without any per-row heuristics.
internal static class PngEncoder
{
    private static readonly byte[] Signature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly uint[] CrcTable = BuildCrcTable();

    // Encodes top-down RGBA rows.
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgbaTopDown)
    {
        using var png = new MemoryStream();
        png.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // color type: RGBA
        header[10] = 0; // compression: deflate
        header[11] = 0; // filter method
        header[12] = 0; // no interlace
        WriteChunk(png, "IHDR", header);

        int stride = width * 4;
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                var row = new byte[stride + 1];
                for (int y = 0; y < height; y++)
                {
                    ReadOnlySpan<byte> source = rgbaTopDown.Slice(y * stride, stride);
                    row[0] = 1; // filter: Sub (each byte minus the same channel of the pixel to its left)
                    for (int i = 0; i < stride; i++)
                    {
                        row[i + 1] = (byte)(source[i] - (i >= 4 ? source[i - 4] : 0));
                    }

                    zlib.Write(row);
                }
            }

            WriteChunk(png, "IDAT", compressed.ToArray());
        }

        WriteChunk(png, "IEND", ReadOnlySpan<byte>.Empty);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
