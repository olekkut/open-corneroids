using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Atlases;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Dense runtime block indices: 0 is air, 1.. map to itemset blocks. Original blockIds can't be used directly
/// because blockId 0 is a real block (Light rock). This prototype only handles 1x1x1 blocks.
/// </summary>
public sealed class BlockPalette
{
    public const ushort Air = 0;

    readonly List<Entry> _entries = [default];   // index 0 = air
    readonly Dictionary<int, ushort> _byItemId = new();
    readonly Dictionary<int, ushort> _byBlockId = new();

    readonly record struct Entry(ItemDef Item, Rect2 Walls, Rect2 Top, Rect2 Bottom, bool Cutout, CellRect IconCell);

    public int Count => _entries.Count - 1;

    public ItemDef Item(ushort index) => _entries[index].Item;

    public bool IsCutout(ushort index) => _entries[index].Cutout;

    public CellRect IconCell(ushort index) => _entries[index].IconCell;

    /// <param name="face">0 = walls, 1 = top, 2 = bottom.</param>
    public Rect2 Uv(ushort index, int face) => face switch
    {
        1 => _entries[index].Top,
        2 => _entries[index].Bottom,
        _ => _entries[index].Walls,
    };

    /// <summary>The runtime index of an item's block, or null when the item is not a placeable 1x1x1 block here
    /// (tools, ingots, multi-block machines, blocks whose texture cells are missing).</summary>
    public ushort? IndexOf(int itemId) => _byItemId.TryGetValue(itemId, out var index) ? index : null;

    /// <summary>The original itemset blockId of a runtime index; what saves store, since runtime indices depend on
    /// which blocks this build could texture and would shift whenever that changes.</summary>
    public int BlockIdOf(ushort index) => Item(index).Block!.BlockId;

    /// <summary>The runtime index for an original blockId, or null when this palette has no such block.</summary>
    public ushort? IndexOfBlockId(int blockId) => _byBlockId.TryGetValue(blockId, out var index) ? index : null;

    public IEnumerable<ushort> Indices => Enumerable.Range(1, Count).Select(i => (ushort)i);

    public IEnumerable<ushort> Where(Func<ItemDef, bool> predicate) => Indices.Where(i => predicate(Item(i)));

    public static BlockPalette Build(ItemsetDef itemset, BlockTextures textures)
    {
        var palette = new BlockPalette();
        var grid = textures.Grid;
        foreach (var item in itemset.Blocks)
        {
            var block = item.Block!;
            if (block.Size != System.Numerics.Vector3.One || block.VisualSize != System.Numerics.Vector3.One) continue;
            var faces = block.Active;
            if (faces.Faces.Any(f => !grid.Contains(f.Rect) || grid.Classify(f.Rect) == AlphaClass.Empty)) continue;
            bool cutout = faces.Faces.Any(f => grid.Classify(f.Rect) is AlphaClass.Cutout or AlphaClass.Translucent);
            palette._byItemId[item.ItemId] = (ushort)palette._entries.Count;
            palette._byBlockId[block.BlockId] = (ushort)palette._entries.Count;
            palette._entries.Add(new Entry(item,
                textures.Uv(faces.Walls), textures.Uv(faces.Top), textures.Uv(faces.Bottom), cutout, faces.Walls));
        }
        return palette;
    }
}
