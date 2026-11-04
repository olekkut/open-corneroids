using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

/// <summary>
/// Drilling rules, as the original has them: a drill mines a block when its <c>power</c> is at
/// least the block's <c>powerToDrill</c>; it takes 0.8 s less 0.1 s for each point of power to spare, never under
/// 0.3 s; the block's <c>health</c> plays no part; and a mined block drops one of itself.
/// </summary>
public static class Mining
{
    public const double MaxSecondsToMine = 0.8;
    public const double MinSecondsToMine = 0.3;
    public const double SecondsPerSparePower = 0.1;

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

    /// <summary>Seconds to break <paramref name="block"/> with a drill of <paramref name="drillPower"/>;
    /// <see cref="double.PositiveInfinity"/> when it can't.</summary>
    public static double SecondsToMine(int drillPower, BlockDef block) =>
        CanMine(drillPower, block)
            ? Math.Clamp(MaxSecondsToMine - SecondsPerSparePower * (drillPower - block.PowerToDrill), MinSecondsToMine, MaxSecondsToMine)
            : double.PositiveInfinity;

    /// <summary>A mined block yields one of its own item, ores included (spec 1.5).</summary>
    public static ItemStack Drop(ItemDef minedBlockItem)
    {
        ArgumentNullException.ThrowIfNull(minedBlockItem);
        return new ItemStack(minedBlockItem.ItemId, 1);
    }
}
