using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Simulation.Persistence;

public enum BindingStatus
{
    /// <summary>Same itemset content as when the world was saved.</summary>
    Exact,
    /// <summary>Same itemset key but edited content; every block the world uses still exists, so it can load with a warning.</summary>
    ContentChanged,
    /// <summary>Same itemset key, but blocks the world uses were removed; loading would lose or misplace them.</summary>
    Incompatible,
    /// <summary>A different itemset altogether; its blockIds mean unrelated blocks.</summary>
    DifferentItemset,
}

/// <param name="MissingBlockIds">Used blockIds the current itemset lacks, ascending. Only non-empty for <see cref="BindingStatus.Incompatible"/>.</param>
/// <param name="Message">Explains the status to the player.</param>
public sealed record BindingResult(BindingStatus Status, IReadOnlyList<int> MissingBlockIds, string Message)
{
    /// <summary>
    /// How the player's saved items fit the itemset (see <see cref="ItemCheck"/>). It does not change
    /// <see cref="Status"/>: blocks decide whether a world can load at all, items only whether to ask first.
    /// </summary>
    public ItemProblems ItemProblems { get; init; } = ItemProblems.None;

    /// <summary>
    /// Loading should ask first because of the player's items: some would be lost, or the itemset changed and some no
    /// longer match. With the exact itemset the world was saved with, items that were already unknown then (kept from
    /// an earlier load) don't ask again.
    /// </summary>
    public bool AsksAboutItems => ItemProblems.LostItems > 0 || (Status != BindingStatus.Exact && !ItemProblems.IsClean);
}

/// <summary>
/// Decides whether a saved world can be loaded with the itemset that is active now. Worlds store ORIGINAL blockIds, so
/// an edited mod keeps working as long as the blocks actually placed survive; palette entries no cell references
/// (left over after mining) don't count. The player's items are checked as well (<see cref="BindingResult.ItemProblems"/>).
/// </summary>
public static class SaveBinding
{
    /// <param name="inventorySlots">The size of the inventory a load creates, for the item check.</param>
    /// <param name="fillOrder">Where that inventory puts added items, for the item check.</param>
    public static BindingResult Check(WorldSnapshot snapshot, ItemsetDef current, string currentPackId,
        int inventorySlots = Inventory.DefaultSlotCount, FillOrder fillOrder = FillOrder.SlotOrder)
    {
        var result = CheckBlocks(snapshot, current, currentPackId);
        return result.Status == BindingStatus.DifferentItemset
            ? result
            : result with { ItemProblems = ItemCheck.Of(snapshot, current, inventorySlots, fillOrder) };
    }

    /// <summary>The verdict from the blocks alone: a different itemset is refused before its items are looked at.</summary>
    static BindingResult CheckBlocks(WorldSnapshot snapshot, ItemsetDef current, string currentPackId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(current);
        var saved = snapshot.Itemset;

        if (!string.Equals(saved.Key, current.Header.Key, StringComparison.Ordinal))
            return new(BindingStatus.DifferentItemset, [],
                $"This world was made with itemset '{saved.Name}' ({saved.Key}) from '{saved.PackId}', but '{current.Header.Name}' ({current.Header.Key}) from '{currentPackId}' is active.");

        if (string.Equals(saved.Fingerprint, current.Fingerprint, StringComparison.OrdinalIgnoreCase))
            return new(BindingStatus.Exact, [], $"Itemset '{current.Header.Name}' matches the one this world was saved with.");

        var missing = UsedBlockIds(snapshot).Where(id => !current.ItemsByBlockId.ContainsKey(id)).Order().ToArray();
        if (missing.Length > 0)
            return new(BindingStatus.Incompatible, missing,
                $"Itemset '{current.Header.Name}' from '{currentPackId}' no longer defines {missing.Length} block type(s) this world uses (blockIds {string.Join(", ", missing)}).");

        return new(BindingStatus.ContentChanged, [],
            $"Itemset '{current.Header.Name}' from '{currentPackId}' has changed since this world was saved (it was from '{saved.PackId}'); every block the world uses still exists, but their properties may differ.");
    }

    /// <summary>Distinct blockIds referenced by at least one cell or placed object, of the world or any ship, air excluded.</summary>
    public static IReadOnlySet<int> UsedBlockIds(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var used = new HashSet<int>();
        AddUsed(used, snapshot.Palette, snapshot.Cells, snapshot.Objects, nameof(snapshot));
        foreach (var ship in snapshot.Ships) AddUsed(used, ship.Palette, ship.Cells, ship.Objects, nameof(snapshot));
        return used;
    }

    /// <summary>The blockIds one grid (the world's or a ship's) uses.</summary>
    static void AddUsed(HashSet<int> used, IReadOnlyList<int> palette, ushort[] cells, IReadOnlyList<PlacedObject> objects, string parameter)
    {
        var referenced = new bool[palette.Count];
        foreach (var cell in cells)
        {
            if (cell >= referenced.Length)
                throw new ArgumentException($"Cell value {cell} is outside the {palette.Count}-entry palette.", parameter);
            referenced[cell] = true;
        }
        for (int i = 0; i < referenced.Length; i++)
            if (referenced[i] && palette[i] != WorldSnapshot.AirBlockId) used.Add(palette[i]);
        foreach (var placed in objects) used.Add(placed.BlockId);
    }
}
