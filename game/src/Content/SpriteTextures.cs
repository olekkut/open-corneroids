using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Atlases;

namespace OpenCorneroids.Game.Content;

/// <summary>
/// GPU side of an itemset's sprite atlas: the icons of items that are not blocks (tools, ingots, weapons).
/// Separate from <see cref="BlockTextures"/> because the two atlases have their own grids and texture packs may
/// replace one without the other.
/// </summary>
public sealed class SpriteTextures
{
    SpriteTextures(AtlasGrid grid, ImageTexture texture)
    {
        Grid = grid;
        Texture = texture;
    }

    public AtlasGrid Grid { get; }
    public ImageTexture Texture { get; }

    /// <summary>Null when the itemset has no usable sprite atlas; item icons then fall back to a text badge.</summary>
    public static SpriteTextures? Create(LoadedItemset loaded) =>
        loaded.SpriteAtlas is { } grid ? new SpriteTextures(grid, ImageTexture.CreateFromImage(BlockTextures.ToGodot(grid.Image))) : null;

    public AtlasTexture Icon(CellRect cell)
    {
        int px = Grid.CellPixels;
        return new AtlasTexture
        {
            Atlas = Texture,
            Region = new Rect2(cell.X * px, cell.Y * px, cell.W * px, cell.H * px),
            FilterClip = true,
        };
    }
}
