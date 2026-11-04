using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

/// <summary>
/// What a new player starts with, from the itemset's <c>defaultItems</c>, as the original does it (the original's rules
/// 1.10): the first drill listed, the crafting table and the ore furnace (smelter) are <see cref="Endless"/> slots that
/// never run out, shown beside the inventory; every other entry is one item in the main inventory. One deviation: the
/// spec says 1.2.0 ignores an extractor in the list, but no blueprint makes one, so a player could never extract
/// anything; the footage of v1.0.5 shows the extractor given as an ordinary item, which is what we do.
/// </summary>
public static class StartingKit
{
    /// <summary>
    /// The endless items: the first drill listed, then the last crafting table and the last smelter listed (the
    /// original keeps the last of each kind). Ids the itemset does not define are skipped.
    /// </summary>
    public static IReadOnlyList<int> Endless(ItemsetDef set)
    {
        ArgumentNullException.ThrowIfNull(set);
        int? drill = null, table = null, smelter = null;
        foreach (var id in set.DefaultItems)
        {
            if (!set.ItemsById.TryGetValue(id, out var item)) continue;
            if (item.Get<DrillComponent>() is not null) drill ??= id;
            else if (item.Get<CraftingStationComponent>()?.Station == CraftingStation.CraftingTable) table = id;
            else if (item.Get<CraftingStationComponent>()?.Station == CraftingStation.Smelter) smelter = id;
        }
        return new[] { drill, table, smelter }.OfType<int>().ToList();
    }

    /// <summary>
    /// Adds one item per <c>defaultItems</c> entry that is not endless, in list order, into the slots from
    /// <paramref name="firstSlot"/> on (the main inventory, <see cref="Crafting.MainInventory"/>, in the game). Drills
    /// after the first are left out, as the original does; unknown ids are skipped; a repeated entry gives a second item.
    /// </summary>
    /// <returns>How many items did not fit.</returns>
    public static int Give(Inventory inventory, ItemsetDef set, int firstSlot = 0)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(set);
        var leftOver = 0;
        foreach (var id in set.DefaultItems)
        {
            if (!set.ItemsById.TryGetValue(id, out var item)) continue;
            if (item.Get<DrillComponent>() is not null) continue;   // the first is endless, the rest are ignored
            if (item.Get<CraftingStationComponent>()?.Station is CraftingStation.CraftingTable or CraftingStation.Smelter) continue;
            leftOver += inventory.AddFrom(id, 1, firstSlot);
        }
        return leftOver;
    }
}
