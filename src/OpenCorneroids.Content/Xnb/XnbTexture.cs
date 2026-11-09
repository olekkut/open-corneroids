namespace OpenCorneroids.Content.Xnb;

/// <summary>
/// A <c>Texture2D</c> read from an XNA 3.x <c>.xnb</c>: the top mip level as RGBA bytes. Only the uncompressed 32-bit
/// colour format is supported, which is what the original's textures use.
/// </summary>
/// <remarks>Layout: surface format (int32), width, height and mip count (uint32), then each mip level as a byte count
/// and its bytes. XNA 3.x stores <c>Color</c> as B, G, R, A in memory.</remarks>
public sealed class XnbTexture
{
    /// <summary>XNA 3.x <c>SurfaceFormat.Color</c>.</summary>
    public const int FormatColor = 1;

    XnbTexture(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Width × height × 4 bytes, R, G, B, A, rows from the top.</summary>
    public byte[] Rgba { get; }

    public static XnbTexture Read(XnbFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var r = file.OpenPrimary();
        int reader = r.Read7BitEncodedInt();
        if (reader == 0 || file.ShortName(reader) != "Texture2DReader") throw new InvalidDataException("XNB file does not hold a texture.");
        int format = r.ReadInt32();
        uint width = r.ReadUInt32(), height = r.ReadUInt32(), mips = r.ReadUInt32();
        if (width is 0 or > 8192 || height is 0 or > 8192 || mips == 0)
            throw new InvalidDataException($"XNB texture is {width}×{height} with {mips} mip levels.");
        if (format != FormatColor) throw new InvalidDataException($"XNB texture format {format} is not supported (only Color).");
        uint size = r.ReadUInt32();
        if (size != width * height * 4) throw new InvalidDataException($"XNB texture holds {size} bytes for {width}×{height}.");
        var bgra = r.ReadBytes((int)size);
        if (bgra.Length != size) throw new InvalidDataException("XNB texture is cut short.");
        for (int i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        return new XnbTexture((int)width, (int)height, bgra);
    }
}
