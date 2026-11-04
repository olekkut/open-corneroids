using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Atlases;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Dense runtime block indices: 0 is air, 1.. map to itemset blocks. Original blockIds can't be used directly
/// because blockId 0 is a real block (Light rock).
/// </summary>
/// <remarks>
/// Every block item whose face textures are usable gets an index, whatever its size. Single-cell blocks (one cell of
/// bounds, model filling it) live in the chunk grid and have no <see cref="Shape"/>; everything else (doors, thrusters,
/// guns, big windows, and 1×1×1 blocks whose model is smaller or shifted) is placed as a whole world object
/// (<see cref="WorldObject"/>) and carries its <see cref="BlockShape"/>. One index space for both keeps the hotbar,
/// the creative palette and the save palette simple.
/// </remarks>
public sealed class BlockPalette
{
    public const ushort Air = 0;

    readonly List<Entry> _entries = [default];   // index 0 = air
    readonly Dictionary<int, ushort> _byItemId = new();
    readonly Dictionary<int, ushort> _byBlockId = new();

    readonly record struct Entry(ItemDef Item, Rect2 Walls, Rect2 Top, Rect2 Bottom, bool Cutout, CellRect IconCell, BlockShape? Shape);

    public int Count => _entries.Count - 1;

    public ItemDef Item(ushort index) => _entries[index].Item;

    /// <summary>Whether any of the block's active face regions has see-through pixels (windows); such faces go
    /// through the cutout material.</summary>
    public bool IsCutout(ushort index) => _entries[index].Cutout;

    public CellRect IconCell(ushort index) => _entries[index].IconCell;

    /// <summary>The block's shape when it is placed as a world object, or null for a single-cell grid block.</summary>
    public BlockShape? Shape(ushort index) => _entries[index].Shape;

    /// <summary>True for blocks placed as world objects rather than into the chunk grid.</summary>
    public bool IsObject(ushort index) => _entries[index].Shape is not null;

    /// <param name="face">0 = walls, 1 = top, 2 = bottom.</param>
    public Rect2 Uv(ushort index, int face) => face switch
    {
        1 => _entries[index].Top,
        2 => _entries[index].Bottom,
        _ => _entries[index].Walls,
    };

    /// <summary>The runtime index of an item's block, or null when the item is not a placeable block here
    /// (tools, ingots, blocks whose texture cells are missing).</summary>
    public ushort? IndexOf(int itemId) => _byItemId.TryGetValue(itemId, out var index) ? index : null;

    /// <summary>The original itemset blockId of a runtime index; what saves store, since runtime indices depend on
    /// which blocks this build could texture and would shift whenever that changes.</summary>
    public int BlockIdOf(ushort index) => Item(index).Block!.BlockId;

    /// <summary>The runtime index for an original blockId, or null when this palette has no such block.</summary>
    public ushort? IndexOfBlockId(int blockId) => _byBlockId.TryGetValue(blockId, out var index) ? index : null;

    public IEnumerable<ushort> Indices => Enumerable.Range(1, Count).Select(i => (ushort)i);

    /// <summary>Blocks that go into the chunk grid (single-cell ones).</summary>
    public IEnumerable<ushort> GridIndices => Indices.Where(i => !IsObject(i));

    /// <summary>Blocks placed as world objects (multi-cell or not filling their cell).</summary>
    public IEnumerable<ushort> ObjectIndices => Indices.Where(IsObject);

    public IEnumerable<ushort> Where(Func<ItemDef, bool> predicate) => Indices.Where(i => predicate(Item(i)));

    public static BlockPalette Build(ItemsetDef itemset, BlockTextures textures)
    {
        var palette = new BlockPalette();
        var grid = textures.Grid;
        foreach (var item in itemset.Blocks)
        {
            var block = item.Block!;
            var faces = block.Active;
            if (faces.Faces.Any(f => !grid.Contains(f.Rect) || grid.Classify(f.Rect) == AlphaClass.Empty)) continue;
            bool cutout = faces.Faces.Any(f => grid.Classify(f.Rect) is AlphaClass.Cutout or AlphaClass.Translucent);
            var shape = BlockShape.Build(block);
            if (shape.Problems.Count > 0)
                GD.Print($"{item.Name} (block {block.BlockId}): shape corrected: {string.Join("; ", shape.Problems)}");
            palette._byItemId[item.ItemId] = (ushort)palette._entries.Count;
            palette._byBlockId[block.BlockId] = (ushort)palette._entries.Count;
            palette._entries.Add(new Entry(item,
                textures.Uv(faces.Walls), textures.Uv(faces.Top), textures.Uv(faces.Bottom), cutout, faces.Walls,
                shape.IsSingleCell ? null : shape));
        }
        return palette;
    }
}
