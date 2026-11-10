using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Items;

/// <summary>
/// The size rules of container blocks (chests), as in the original: the itemset gives a grid of <c>width</c> by
/// <c>height</c> slots, a missing or zero value is 1, and the grid is held within 1..7 by 1..5, so no container has
/// more than 35 slots. Slots are numbered row by row (<see cref="SlotIndex"/>).
/// </summary>
public static class ContainerRules
{
    public const int MaxWidth = ContainerComponent.MaxWidth;
    public const int MaxHeight = ContainerComponent.MaxHeight;

    /// <summary>The grid of a container block, clamped as in the original.</summary>
    public static (int Width, int Height) Size(ContainerComponent container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return (Math.Clamp(container.Width, 1, MaxWidth), Math.Clamp(container.Height, 1, MaxHeight));
    }

    /// <summary>How many slots a container block has.</summary>
    public static int SlotCount(ContainerComponent container)
    {
        var (width, height) = Size(container);
        return width * height;
    }

    /// <summary>Row by row, as in the original: <c>y * width + x</c>.</summary>
    public static int SlotIndex(int x, int y, int width) => y * width + x;
}

/// <summary>A stack with the cell of the grid it belongs next to: what a broken or no longer fitting chest gives back.</summary>
public sealed record CellStack(Int3 Cell, ItemStack Stack);

/// <summary>What restoring a grid's saved chests did.</summary>
/// <param name="Dropped">Saved chests that were not restored: their cell no longer holds a container block, or the
/// same cell was saved twice. Their contents are in <paramref name="Leftovers"/>.</param>
/// <param name="Leftovers">Stacks that found no slot (a chest that became smaller, or one that is gone), with the cell
/// they were saved at, so the game can hand them back (into the inventory, or floating there) instead of deleting them.
/// Ours: the original empties a chest whose size no longer matches.</param>
public sealed record ContainerLoad(int Dropped, IReadOnlyList<CellStack> Leftovers)
{
    public static readonly ContainerLoad None = new(0, []);
}

/// <summary>
/// The contents of every container block on one grid (the static world, a ship or an asteroid), keyed by the block's
/// cell (its origin for a multi-cell block). Anyone can open any chest; there is no owner or lock, as in the original.
/// A chest's state must be forgotten whenever its cell changes, or old contents would come back in a new block; a
/// launch moves it to the new ship instead (<see cref="ExtractAndShift"/>). Contents add no mass, as in the original.
/// </summary>
public sealed class GridContainers
{
    readonly Dictionary<Int3, Inventory> _containers = [];

    /// <summary>Every chest that has been opened or loaded, empty ones included.</summary>
    public IReadOnlyDictionary<Int3, Inventory> All => _containers;

    public int Count => _containers.Count;

    public Inventory? TryGet(Int3 cell) => _containers.TryGetValue(cell, out var inventory) ? inventory : null;

    /// <summary>The chest at <paramref name="cell"/>, made empty with <paramref name="slots"/> slots the first time.</summary>
    /// <param name="maxStackOf">Stack limit per item id (the itemset's).</param>
    public Inventory GetOrCreate(Int3 cell, int slots, Func<int, int> maxStackOf)
    {
        ArgumentNullException.ThrowIfNull(maxStackOf);
        if (_containers.TryGetValue(cell, out var existing)) return existing;
        var created = new Inventory(Math.Max(1, slots), maxStackOf);
        _containers[cell] = created;
        return created;
    }

    /// <summary>
    /// Takes the chest at <paramref name="cell"/> away (its block was broken) and returns what it held, in slot order,
    /// for spilling. Empty when there was no chest or it was empty.
    /// </summary>
    public IReadOnlyList<ItemStack> Remove(Int3 cell)
    {
        if (!_containers.Remove(cell, out var inventory)) return [];
        return NonEmpty(inventory);
    }

    /// <summary>Forgets the chest at <paramref name="cell"/> without returning its contents (its cell changed).</summary>
    public void Forget(Int3 cell) => _containers.Remove(cell);

    /// <summary>
    /// Moves the chests at <paramref name="cells"/> into a new set, each at <c>cell - shift</c>, the same way console
    /// bindings move when a structure is launched as a ship. The very same inventories move, so nothing is copied.
    /// </summary>
    public GridContainers ExtractAndShift(IReadOnlySet<Int3> cells, Int3 shift)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var extracted = new GridContainers();
        foreach (var cell in _containers.Keys.Where(cells.Contains).ToList())
        {
            extracted._containers[cell - shift] = _containers[cell];
            _containers.Remove(cell);
        }
        return extracted;
    }

    /// <summary>The chests for a save, in cell order. Empty chests are left out, as in the original.</summary>
    public IReadOnlyList<SavedContainer> ToSnapshot() =>
        [.. _containers
            .Where(c => c.Value.Slots.Any(s => s is not null))
            .OrderBy(c => c.Key.Z).ThenBy(c => c.Key.Y).ThenBy(c => c.Key.X)
            .Select(c => new SavedContainer(c.Key, [.. c.Value.Slots]))];

    /// <summary>
    /// Restores saved chests onto the blocks a grid has now, replacing what was there. Every saved chest comes back,
    /// unlike the original, which restores only one chest per ship or asteroid. Each stack goes back into its own slot
    /// as far as it fits; what does not (a chest that became smaller, a smaller stack limit, a cell that no longer holds
    /// a container block) is added to the chest's other slots, and anything still left over is returned for the game to
    /// hand back.
    /// </summary>
    /// <param name="slotsAt">How many slots the container block at a cell has now, or null when it holds none.</param>
    /// <param name="maxStackOf">Stack limit per item id (the itemset's).</param>
    public ContainerLoad LoadSnapshot(IEnumerable<SavedContainer>? saved, Func<Int3, int?> slotsAt, Func<int, int> maxStackOf)
    {
        ArgumentNullException.ThrowIfNull(slotsAt);
        ArgumentNullException.ThrowIfNull(maxStackOf);
        _containers.Clear();
        if (saved is null) return ContainerLoad.None;
        int dropped = 0;
        var leftovers = new List<CellStack>();
        foreach (var entry in saved)
        {
            if (entry is null) continue;
            var stacks = entry.Slots ?? [];
            if (_containers.ContainsKey(entry.Cell) || slotsAt(entry.Cell) is not { } slots || slots < 1)
            {
                dropped++;
                foreach (var stack in stacks)
                    if (stack is { Quantity: > 0 }) leftovers.Add(new CellStack(entry.Cell, stack));
                continue;
            }
            var inventory = GetOrCreate(entry.Cell, slots, maxStackOf);
            var overflow = new List<ItemStack>();
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i] is not { Quantity: > 0 } stack) continue;
                int put = i < slots ? inventory.PutIntoSlot(i, stack.ItemId, stack.Quantity) : 0;
                if (put < stack.Quantity) overflow.Add(stack with { Quantity = stack.Quantity - put });
            }
            foreach (var stack in overflow)
            {
                int left = inventory.Add(stack.ItemId, stack.Quantity);
                if (left > 0) leftovers.Add(new CellStack(entry.Cell, stack with { Quantity = left }));
            }
        }
        return dropped == 0 && leftovers.Count == 0 ? ContainerLoad.None : new ContainerLoad(dropped, leftovers);
    }

    static IReadOnlyList<ItemStack> NonEmpty(Inventory inventory) =>
        [.. inventory.Slots.Where(s => s is { Quantity: > 0 }).Select(s => s!)];
}

/// <summary>
/// How a broken chest throws out its contents, as in the original: every non-empty slot becomes one floating item at
/// the block, moving in a random direction (three uniform values in -0.5..0.5, normalised) at a uniform speed between
/// <c>sv_container_spill_speed_min</c> and <c>_max</c> (1..4 m/s), out of reach of pickup for
/// <c>sv_container_spill_pickup_delay</c> (1 s).
/// </summary>
public static class ContainerSpill
{
    /// <summary>
    /// One spilled stack's velocity. The direction is zero in the (practically impossible) case that all three random
    /// values are zero. A maximum below the minimum is read the other way round.
    /// </summary>
    public static Vector3 Velocity(Random rng, float minSpeed, float maxSpeed)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var direction = new Vector3(
            (float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5));
        float low = Math.Max(0, Math.Min(minSpeed, maxSpeed)), high = Math.Max(0, Math.Max(minSpeed, maxSpeed));
        float speed = low + (float)rng.NextDouble() * (high - low);
        float length = direction.Length();
        return length <= 0 ? Vector3.Zero : direction / length * speed;
    }
}
