using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

/// <summary>
/// Blueprint rules, as the original has them: crafting is instant and all-or-nothing, and it uses
/// only the main inventory: ingredients are counted and taken from the slots after the hotbar, and the results must
/// fit there. Pass <c>firstSlot</c> 0 to use the whole inventory.
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
    public static int MaxCraftable(Inventory inventory, BlueprintDef blueprint, int firstSlot = 0)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(blueprint);
        long max = int.MaxValue;
        foreach (var (itemId, needed) in Inventory.Totals(blueprint.Ingredients))
            max = Math.Min(max, inventory.CountFrom(itemId, firstSlot) / needed);
        return (int)max;
    }

    /// <summary>The first slot of the main inventory, after the hotbar: where crafting looks.</summary>
    public const int MainInventory = Inventory.HotbarSize;

    /// <summary>Right click in the original: half of what can be afforded, rounded down, at least one.</summary>
    public static int HalfBatch(int affordable) => Math.Max(1, affordable / 2);

    /// <summary>
    /// Crafts <paramref name="times"/> runs at once. Fails without touching the inventory when an ingredient is
    /// short, an id (ingredient or result) is not in the itemset, the blueprint has no result, or the results do not
    /// fit; the fit check runs after the ingredients are gone, so consuming a full stack can make room for the output.
    /// </summary>
    public static bool TryCraft(Inventory inventory, ItemsetDef set, BlueprintDef blueprint, int times = 1, int firstSlot = 0)
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

        return inventory.TryExchange(take, [blueprint.Result with { Quantity = (int)produced }], firstSlot);
    }
}
