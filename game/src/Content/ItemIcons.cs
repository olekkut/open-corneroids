using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Atlases;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Game.Content;

/// <summary>
/// One icon per item, shared by the hotbar, the inventory and the crafting window. Blocks show their wall texture
/// from the block atlas (a block's <c>sprite</c> value is not an icon); every other item shows its sprite cell.
/// Icons are cached so redrawing a window does not create new textures.
/// </summary>
public sealed class ItemIcons
{
    readonly BlockTextures _blocks;
    readonly SpriteTextures? _sprites;
    readonly Dictionary<int, Texture2D?> _cache = new();

    public ItemIcons(BlockTextures blocks, SpriteTextures? sprites)
    {
        _blocks = blocks;
        _sprites = sprites;
    }

    /// <summary>The item's icon, or null when its cell is outside the atlas or fully transparent.</summary>
    public Texture2D? Of(ItemDef item)
    {
        if (_cache.TryGetValue(item.ItemId, out var cached)) return cached;
        Texture2D? icon = item.Block is { } block
            ? Usable(_blocks.Grid, block.Active.Walls) ? _blocks.Icon(block.Active.Walls) : null
            : _sprites is { } sprites && Usable(sprites.Grid, item.Sprite) ? sprites.Icon(item.Sprite) : null;
        _cache[item.ItemId] = icon;
        return icon;
    }

    /// <summary>A short text stand-in for items without a drawable icon: the initials of the first two words.</summary>
    public static string Badge(ItemDef item) =>
        string.Concat(item.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    static bool Usable(AtlasGrid grid, CellRect cell) => grid.Contains(cell) && grid.Classify(cell) != AlphaClass.Empty;
}
