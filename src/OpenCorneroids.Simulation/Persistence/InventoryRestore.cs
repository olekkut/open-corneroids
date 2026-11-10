using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Simulation.Persistence;

/// <summary>What restoring a saved inventory could not keep exactly as it was.</summary>
/// <param name="UnknownStacks">Stacks of items the itemset does not define. They stay in their slots (without an icon).</param>
/// <param name="SplitStacks">Stacks above the itemset's stack size. The excess went to other slots, or was lost.</param>
/// <param name="LostItems">Items that found no room in the inventory and are gone.</param>
public sealed record InventoryRestoreReport(int UnknownStacks, int SplitStacks, long LostItems)
{
    /// <summary>Everything came back as it was saved.</summary>
    public bool IsClean => UnknownStacks == 0 && SplitStacks == 0 && LostItems == 0;

    /// <summary>One sentence for the player, or null when <see cref="IsClean"/>.</summary>
    public string? Describe(string itemsetName)
    {
        var notes = new List<string>();
        if (UnknownStacks > 0) notes.Add($"{UnknownStacks} inventory stack(s) are items {itemsetName} does not define (kept, without an icon)");
        if (SplitStacks > 0) notes.Add($"{SplitStacks} inventory stack(s) were above this itemset's stack size and were split");
        if (LostItems > 0) notes.Add($"{LostItems} item(s) did not fit the inventory and were lost");
        return notes.Count == 0 ? null : string.Join("; ", notes) + ".";
    }
}

/// <summary>What restoring a saved suit could not keep on the suit.</summary>
/// <param name="UnwornStacks">Saved equipment this itemset can't wear in that slot; it was moved to the inventory.</param>
/// <param name="LostItems">Of what was moved to the inventory, the items that did not fit and are gone.</param>
public sealed record SuitRestoreReport(int UnwornStacks, long LostItems)
{
    /// <summary>Items above one in worn stacks (a suit slot holds one); moved to the inventory too.</summary>
    public long ExtraWornItems { get; init; }

    /// <summary>Everything worn came back worn.</summary>
    public bool IsClean => UnwornStacks == 0 && ExtraWornItems == 0;

    /// <summary>One sentence for the player, or null when <see cref="IsClean"/>.</summary>
    public string? Describe(string itemsetName)
    {
        var notes = new List<string>();
        if (UnwornStacks > 0) notes.Add($"{UnwornStacks} worn items can't be worn with {itemsetName} and were moved to the inventory");
        if (ExtraWornItems > 0) notes.Add($"{ExtraWornItems} extra item(s) of worn stacks were moved to the inventory");
        if (notes.Count == 0) return null;
        if (LostItems > 0) notes.Add($"{LostItems} did not fit and were lost");
        return string.Join("; ", notes);
    }
}

/// <summary>
/// Writing the player's survival inventory and suit to a save and reading them back. Both are kept whatever mode the
/// world is in when it is saved: a survival world switched to creative (the <c>mode</c> command) and saved still holds
/// the survival items, and switching back to survival after a reload finds them again.
/// </summary>
public static class InventoryRestore
{
    /// <summary>
    /// The slots to save. Every slot, empty ones included, so positions survive a round trip. Nothing only when the
    /// world has never had a survival player (no suit) and the inventory is empty: a creative world that was never
    /// switched to survival saves no inventory, as before. A stack <paramref name="held"/> on the cursor of an open item
    /// window is in no slot, so it is saved as if put back into the inventory (the live inventory is not changed); what
    /// does not fit is saved as one more slot past the end, which a load adds like a pickup.
    /// </summary>
    public static IReadOnlyList<ItemStack?> SlotsToSave(Inventory inventory, Suit? suit, ItemStack? held = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var slots = inventory.SlotsWith(held, out int left);
        if (left > 0) slots = [.. slots, held! with { Quantity = left }];
        return suit is null && slots.All(s => s is null) ? [] : slots;
    }

    /// <summary>
    /// Puts saved slots back into a fresh inventory, slot for slot. Items the itemset does not define are kept (with the
    /// unknown-item stack limit); a stack above the stack size keeps the limit in its slot and the rest is added like a
    /// pickup, as are stacks from slots beyond this inventory's size. Whatever still finds no room is lost and counted.
    /// </summary>
    public static InventoryRestoreReport Restore(Inventory inventory, IReadOnlyList<ItemStack?> saved, ItemsetDef itemset)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(itemset);
        int unknown = 0, split = 0;
        var overflow = new List<ItemStack>();
        for (int i = 0; i < saved.Count; i++)
        {
            if (saved[i] is not { Quantity: > 0 } stack) continue;
            if (i >= inventory.Slots.Count) { overflow.Add(stack); continue; }
            if (!itemset.ItemsById.ContainsKey(stack.ItemId)) unknown++;
            int max = inventory.MaxStackOf(stack.ItemId);
            if (stack.Quantity > max)
            {
                split++;
                overflow.Add(stack with { Quantity = stack.Quantity - max });
            }
            inventory.SetSlot(i, stack);
        }
        long lost = 0;
        foreach (var stack in overflow) lost += inventory.Add(stack.ItemId, stack.Quantity);
        return new InventoryRestoreReport(unknown, split, lost);
    }

    /// <summary>
    /// Restores the suit from a save. Equipment this itemset can't wear, and the extra items of a worn stack, go back
    /// to the inventory rather than vanishing; only what then finds no room is lost.
    /// </summary>
    public static SuitRestoreReport RestoreSuit(Suit suit, SuitState state, Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(suit);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inventory);
        var unworn = suit.Restore(state, out var extras);
        long lost = unworn.Concat(extras).Sum(stack => (long)inventory.Add(stack.ItemId, stack.Quantity));
        return new SuitRestoreReport(unworn.Count, lost) { ExtraWornItems = extras.Sum(stack => (long)stack.Quantity) };
    }
}
