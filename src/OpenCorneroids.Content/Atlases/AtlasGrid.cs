using OpenCorneroids.Content.Imaging;
using OpenCorneroids.Content.Vfs;

namespace OpenCorneroids.Content.Atlases;

/// <summary>How a region uses alpha, which decides the surface it is meshed into.</summary>
public enum AlphaClass { Empty, Opaque, Cutout, Translucent }

/// <summary>
/// An atlas resolved through a view: its texture, and its grid in cells. XML coordinates are cells, so the grid
/// comes from the texture the declaring pack shipped (512 px / blockSize 32 = 16 for every known atlas). A
/// higher-resolution drop-in replacement only changes <see cref="CellPixels"/>.
/// </summary>
public sealed class AtlasGrid
{
    AtlasGrid(AtlasDef def, RgbaImage image, int columns, int rows, string servedBy)
    {
        Def = def;
        Image = image;
        Columns = columns;
        Rows = rows;
        ServedBy = servedBy;
    }

    public AtlasDef Def { get; }
    public RgbaImage Image { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int CellPixels => Image.Width / Columns;
    /// <summary>Id of the pack whose PNG was used.</summary>
    public string ServedBy { get; }

    public bool Contains(CellRect r) => !r.IsEmpty && r.X >= 0 && r.Y >= 0 && r.X + r.W <= Columns && r.Y + r.H <= Rows;

    public AlphaClass Classify(CellRect r)
    {
        if (!Contains(r)) throw new ArgumentOutOfRangeException(nameof(r), r, "region is outside the atlas grid");
        int cell = CellPixels;
        bool opaque = false, partial = false, holes = false;
        for (int y = r.Y * cell; y < (r.Y + r.H) * cell; y++)
        for (int x = r.X * cell; x < (r.X + r.W) * cell; x++)
        {
            byte a = Image.AlphaAt(x, y);
            if (a == 0) holes = true;
            else if (a == 255) opaque = true;
            else partial = true;
        }
        return partial ? AlphaClass.Translucent : !opaque ? AlphaClass.Empty : holes ? AlphaClass.Cutout : AlphaClass.Opaque;
    }

    public static AtlasGrid? Load(AtlasDef def, LayeredSource view, DiagnosticBag diagnostics)
    {
        var file = $"{def.DeclaringPack}:{def.TexturePath}";
        if (view.Resolve(def.TexturePath) is not { } servedBy || !servedBy.TryOpen(def.TexturePath, out var stream))
        {
            diagnostics.Report(Severity.Error, Codes.TextureMissing, $"atlas '{def.Key}' texture not found in {view.PackId}", file);
            return null;
        }

        RgbaImage image;
        int referenceWidth;
        try
        {
            using (stream) image = PngReader.Read(stream);
            referenceWidth = image.Width;
            // Grid size comes from the declaring pack's own PNG when it ships one; an override may be higher-res.
            var declaring = view.Layers.FirstOrDefault(l => l.PackId == def.DeclaringPack);
            if (declaring is not null && declaring != servedBy && declaring.TryOpen(def.TexturePath, out var original))
                using (original) referenceWidth = PngReader.ReadSize(original).Width;
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or EndOfStreamException)
        {
            diagnostics.Report(Severity.Error, Codes.TextureUnreadable, $"atlas '{def.Key}': {e.Message}", $"{servedBy.PackId}:{def.TexturePath}");
            return null;
        }

        int columns = Math.Max(1, referenceWidth / Math.Max(1, def.BlockSize));
        int cell = image.Width / columns;
        if (referenceWidth % def.BlockSize != 0 || image.Width % columns != 0 || cell == 0 || image.Height % cell != 0)
        {
            diagnostics.Report(Severity.Warning, Codes.AtlasGridMismatch,
                $"atlas '{def.Key}' is {image.Width}x{image.Height}, which doesn't divide into {columns} columns of blockSize {def.BlockSize}",
                $"{servedBy.PackId}:{def.TexturePath}");
            if (cell == 0) return null;
        }
        return new AtlasGrid(def, image, columns, image.Height / cell, servedBy.PackId);
    }
}
