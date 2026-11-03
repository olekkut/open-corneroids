using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenCorneroids.Content.Imaging;

/// <summary>8-bit RGBA pixels, straight alpha, rows top to bottom.</summary>
public sealed class RgbaImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;

    public byte AlphaAt(int x, int y) => Pixels[(y * Width + x) * 4 + 3];
}

/// <summary>
/// Minimal PNG decoder for what the original atlases use: 8-bit, non-interlaced, any colour type
/// (the vanilla sprite atlas is palette + tRNS). Godot decodes PNGs itself at runtime; this exists so
/// validation, tools and tests don't need Godot.
/// </summary>
public static class PngReader
{
    const int MaxDimension = 16384;
    static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static RgbaImage Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.ToArray());
    }

    /// <summary>Width and height from the IHDR chunk, without decoding pixels.</summary>
    public static (int Width, int Height) ReadSize(Stream stream)
    {
        Span<byte> head = stackalloc byte[24];
        stream.ReadExactly(head);
        if (!head[..8].SequenceEqual(Signature) || !head[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("not a PNG file");
        return (BinaryPrimitives.ReadInt32BigEndian(head[16..]), BinaryPrimitives.ReadInt32BigEndian(head[20..]));
    }

    public static RgbaImage Read(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("not a PNG file");

        int width = 0, height = 0, colorType = -1;
        byte[]? palette = null, transparency = null;
        using var compressed = new MemoryStream();

        for (int pos = 8; pos + 12 <= data.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            if (length < 0 || pos + 12 + length > data.Length)
                throw new InvalidDataException("truncated PNG chunk");
            var type = Encoding.ASCII.GetString(data, pos + 4, 4);
            var body = data.AsSpan(pos + 8, length);
            pos += 12 + length;

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    colorType = body[9];
                    if (body[8] != 8) throw new NotSupportedException($"{body[8]}-bit PNGs are not supported");
                    if (body[12] != 0) throw new NotSupportedException("interlaced PNGs are not supported");
                    break;
                case "PLTE": palette = body.ToArray(); break;
                case "tRNS": transparency = body.ToArray(); break;
                case "IDAT": compressed.Write(body); break;
                case "IEND": pos = data.Length; break;
            }
        }

        if (width is <= 0 or > MaxDimension || height is <= 0 or > MaxDimension)
            throw new InvalidDataException($"bad PNG size {width}x{height}");
        int channels = colorType switch
        {
            0 or 3 => 1,
            4 => 2,
            2 => 3,
            6 => 4,
            _ => throw new NotSupportedException($"PNG colour type {colorType} is not supported"),
        };

        int stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
            zlib.ReadExactly(raw);

        var samples = Unfilter(raw, stride, height, channels);
        return new RgbaImage(width, height, ToRgba(samples, width * height, colorType, palette, transparency));
    }

    static byte[] Unfilter(byte[] raw, int stride, int height, int bytesPerPixel)
    {
        var output = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            var src = raw.AsSpan(y * (stride + 1) + 1, stride);
            var dst = output.AsSpan(y * stride, stride);
            ReadOnlySpan<byte> prev = y > 0 ? output.AsSpan((y - 1) * stride, stride) : default;
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bytesPerPixel ? dst[i - bytesPerPixel] : 0;
                int b = y > 0 ? prev[i] : 0;
                int c = y > 0 && i >= bytesPerPixel ? prev[i - bytesPerPixel] : 0;
                dst[i] = (byte)(src[i] + filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"bad PNG filter type {filter}"),
                });
            }
        }
        return output;
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static byte[] ToRgba(byte[] s, int pixelCount, int colorType, byte[]? palette, byte[]? trns)
    {
        var rgba = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            int d = i * 4;
            switch (colorType)
            {
                case 0:
                {
                    byte g = s[i];
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = g;
                    // 8-bit tRNS samples are stored as 16-bit big-endian values; the low byte is the sample.
                    rgba[d + 3] = trns is { Length: >= 2 } && g == trns[1] ? (byte)0 : (byte)255;
                    break;
                }
                case 2:
                {
                    byte r = s[i * 3], g = s[i * 3 + 1], b = s[i * 3 + 2];
                    rgba[d] = r; rgba[d + 1] = g; rgba[d + 2] = b;
                    rgba[d + 3] = trns is { Length: >= 6 } && r == trns[1] && g == trns[3] && b == trns[5] ? (byte)0 : (byte)255;
                    break;
                }
                case 3:
                {
                    int index = s[i];
                    if (palette is null || index * 3 + 2 >= palette.Length)
                        throw new InvalidDataException($"PNG palette index {index} out of range");
                    rgba[d] = palette[index * 3]; rgba[d + 1] = palette[index * 3 + 1]; rgba[d + 2] = palette[index * 3 + 2];
                    rgba[d + 3] = trns is not null && index < trns.Length ? trns[index] : (byte)255;
                    break;
                }
                case 4:
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = s[i * 2];
                    rgba[d + 3] = s[i * 2 + 1];
                    break;
                default:
                    Buffer.BlockCopy(s, i * 4, rgba, d, 4);
                    break;
            }
        }
        return rgba;
    }
}
