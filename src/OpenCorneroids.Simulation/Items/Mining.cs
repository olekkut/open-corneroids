using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

/// <summary>
/// Drilling rules. Only the power comparison comes from the itemset format (<c>power</c> on drills,
/// <c>powerToDrill</c> on blocks); the timing formula and the drop rule are ours, tunable, and the original
/// game's behaviour for both is unknown.
/// </summary>
public static class Mining
{
    /// <summary>Floor on mining time so even a strong drill on a weak block is a visible action, not a flicker. Tunable.</summary>
    public const double MinSecondsToMine = 0.15;

    /// <summary>Seconds per point of block health at drill power 1. Tunable; original behaviour unknown.</summary>
    public const double SecondsPerHealthPerPower = 0.1;

    /// <summary>
    /// The best drill the player carries anywhere in the inventory, not just the selected slot, so mining does not
    /// depend on juggling the hotbar. 0 means no drill.
    /// </summary>
    public static int DrillPower(Inventory inventory, ItemsetDef set)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(set);
        var best = 0;
        foreach (var stack in inventory.Slots)
            if (stack is not null && set.ItemsById.TryGetValue(stack.ItemId, out var item) && item.Get<DrillComponent>() is { } drill)
                best = Math.Max(best, drill.Power);
        return best;
    }

    /// <summary>A drill is needed even for blocks with <c>powerToDrill</c> 0, so bare hands never mine.</summary>
    public static bool CanMine(int drillPower, BlockDef block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return drillPower > 0 && drillPower >= block.PowerToDrill;
    }

    /// <summary>
    /// Time scales with block health and inversely with drill power. Our formula, tunable; the original's is unknown.
    /// Zero or negative health needs no special case: it lands on <see cref="MinSecondsToMine"/> like any other
    /// quick block, so the result is always positive. <see cref="double.PositiveInfinity"/> when the block cannot be
    /// mined.
    /// </summary>
    public static double SecondsToMine(int drillPower, BlockDef block) =>
        CanMine(drillPower, block)
            ? Math.Max(MinSecondsToMine, SecondsPerHealthPerPower * block.Health / drillPower)
            : double.PositiveInfinity;

    /// <summary>
    /// A mined block yields one of its own item. Our rule: the original may drop something else (ore from asteroids,
    /// for instance), which is unknown.
    /// </summary>
    public static ItemStack Drop(ItemDef minedBlockItem)
    {
        ArgumentNullException.ThrowIfNull(minedBlockItem);
        return new ItemStack(minedBlockItem.ItemId, 1);
    }
}
