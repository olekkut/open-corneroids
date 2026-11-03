using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Atlases;
using OpenCorneroids.Content.Imaging;

namespace OpenCorneroids.Game.Content;

/// <summary>
/// GPU side of one block atlas: the albedo texture, its light (emission) mask, and the two shared block materials.
/// Pixels come from the content library's decoded image, so what is validated is exactly what is drawn.
/// </summary>
public sealed class BlockTextures
{
    BlockTextures(AtlasGrid grid, ImageTexture albedo, ImageTexture? light)
    {
        Grid = grid;
        Albedo = albedo;
        Light = light;
        var shader = GD.Load<Shader>("res://shaders/block.gdshader");
        Opaque = CreateMaterial(shader, cutout: false);
        Cutout = CreateMaterial(shader, cutout: true);
    }

    public AtlasGrid Grid { get; }
    public ImageTexture Albedo { get; }
    public ImageTexture? Light { get; }
    public ShaderMaterial Opaque { get; }
    public ShaderMaterial Cutout { get; }

    public static BlockTextures Create(LoadedItemset loaded)
    {
        var grid = loaded.BlockAtlas ?? throw new InvalidOperationException("itemset has no block atlas");
        var albedo = ImageTexture.CreateFromImage(ToGodot(grid.Image));

        ImageTexture? light = null;
        if (grid.Def.LightTexturePath is { } lightPath && loaded.View.TryOpen(lightPath, out var stream))
        {
            try
            {
                using (stream) light = ImageTexture.CreateFromImage(ToGodot(PngReader.Read(stream)));
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or EndOfStreamException)
            {
                GD.PushWarning($"Light atlas {lightPath} unreadable, blocks won't glow: {e.Message}");
            }
        }
        return new BlockTextures(grid, albedo, light);
    }

    /// <summary>A cell region as an icon, e.g. a block's wall texture in the hotbar.</summary>
    public AtlasTexture Icon(CellRect cell)
    {
        int px = Grid.CellPixels;
        return new AtlasTexture
        {
            Atlas = Albedo,
            Region = new Rect2(cell.X * px, cell.Y * px, cell.W * px, cell.H * px),
            FilterClip = true,
        };
    }

    /// <summary>Normalised UV rectangle of a cell region, inset a hair so nearest sampling never picks a neighbour.</summary>
    public Rect2 Uv(CellRect cell)
    {
        float inset = 0.01f / Grid.CellPixels;
        float x = (cell.X + inset) / Grid.Columns, y = (cell.Y + inset) / Grid.Rows;
        return new Rect2(x, y, (cell.W - 2 * inset) / Grid.Columns, (cell.H - 2 * inset) / Grid.Rows);
    }

    ShaderMaterial CreateMaterial(Shader shader, bool cutout)
    {
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo_atlas", Albedo);
        if (Light is not null) material.SetShaderParameter("light_atlas", Light);
        material.SetShaderParameter("cutout", cutout);
        return material;
    }

    static Image ToGodot(RgbaImage image) =>
        Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Pixels);
}
