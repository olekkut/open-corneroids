using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenCorneroids.Content.Tests.Support;

/// <summary>Writes small RGBA8 PNGs so tests never need the original game's images.</summary>
static class TestPng
{
    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static byte[] Encode(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        int stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var (r, g, b, a) = pixel(x, y);
            int i = y * (stride + 1) + 1 + x * 4;
            raw[i] = r; raw[i + 1] = g; raw[i + 2] = b; raw[i + 3] = a;
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;   // bit depth
        header[9] = 6;   // RGBA

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>An atlas of <paramref name="columns"/>×<paramref name="rows"/> cells, each filled with one alpha value.</summary>
    public static byte[] Atlas(int columns, int rows, int cellPixels, Func<int, int, byte> alphaOfCell) =>
        Encode(columns * cellPixels, rows * cellPixels, (x, y) => (90, 90, 90, alphaOfCell(x / cellPixels, y / cellPixels)));

    static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typeAndData);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typeAndData));
        stream.Write(crc);
    }

    static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
