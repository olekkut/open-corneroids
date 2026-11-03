using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

/// <summary>
/// Blueprint rules. Crafting is instant and all-or-nothing; there is no queue or timer because the original's
/// crafting timing is unknown and an instant craft is the simplest rule that plays well.
/// </summary>
public static class Crafting
{
    /// <summary>Blueprints usable at a station, in file order so the menu matches the modder's layout.</summary>
    public static IReadOnlyList<BlueprintDef> For(ItemsetDef set, CraftingStation station)
    {
        ArgumentNullException.ThrowIfNull(set);
        return set.Blueprints.Where(b => b.Usage == station).ToList();
    }

    /// <summary>
    /// How many times the ingredients on hand allow this blueprint, with repeated ingredient ids counted together.
    /// Output space is ignored so the menu can show what the player could make after clearing room; a blueprint with
    /// no ingredients is unbounded and reports <see cref="int.MaxValue"/>.
    /// </summary>
    public static int MaxCraftable(Inventory inventory, BlueprintDef blueprint)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(blueprint);
        long max = int.MaxValue;
        foreach (var (itemId, needed) in Inventory.Totals(blueprint.Ingredients))
            max = Math.Min(max, inventory.CountExact(itemId) / needed);
        return (int)max;
    }

    /// <summary>
    /// Crafts <paramref name="times"/> runs at once. Fails without touching the inventory when an ingredient is
    /// short, an id (ingredient or result) is not in the itemset, the blueprint has no result, or the results do not
    /// fit; the fit check runs after the ingredients are gone, so consuming a full stack can make room for the output.
    /// </summary>
    public static bool TryCraft(Inventory inventory, ItemsetDef set, BlueprintDef blueprint, int times = 1)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(blueprint);
        if (times < 1 || blueprint.Result.Quantity < 1)
            return false;
        if (!set.ItemsById.ContainsKey(blueprint.Result.ItemId) || blueprint.Ingredients.Any(i => !set.ItemsById.ContainsKey(i.ItemId)))
            return false;

        var take = new List<ItemStack>();
        foreach (var (itemId, perRun) in Inventory.Totals(blueprint.Ingredients))
        {
            var total = perRun * times;
            // Without this a wrapped (negative) take would be skipped as "nothing needed" and the craft would be free.
            if (total > int.MaxValue)
                return false;
            take.Add(new ItemStack(itemId, (int)total));
        }
        var produced = (long)blueprint.Result.Quantity * times;
        if (produced > int.MaxValue)
            return false;

        return inventory.TryExchange(take, [blueprint.Result with { Quantity = (int)produced }]);
    }
}
