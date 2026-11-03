using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

/// <summary>What a new player spawns with, driven by the itemset's <c>defaultItems</c> list.</summary>
public static class StartingKit
{
    /// <summary>
    /// Adds one item per <c>defaultItems</c> entry, in list order so the first entries land in the hotbar. Entries
    /// naming ids the itemset does not define are skipped rather than failing, because mods often trim items and
    /// forget the list. A repeated entry gives a second item. Quantities are ours; the original's are unknown.
    /// </summary>
    /// <returns>How many items did not fit.</returns>
    public static int Give(Inventory inventory, ItemsetDef set)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(set);
        var leftOver = 0;
        foreach (var itemId in set.DefaultItems)
            if (set.ItemsById.ContainsKey(itemId))
                leftOver += inventory.Add(itemId, 1);
        return leftOver;
    }
}
