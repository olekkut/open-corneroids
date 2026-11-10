using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Simulation.Persistence;

/// <summary>
/// How a saved world's player items (inventory, suit and running buffs) fit the itemset that is active now. Items are
/// saved by item id, so an edited or different itemset can leave ids it no longer defines, equipment it no longer
/// wears in that slot, or a smaller inventory. The counts are what a load would actually do (see
/// <see cref="ItemCheck.Of"/>).
/// </summary>
/// <param name="UnknownInventoryStacks">Inventory stacks of items the itemset does not define (kept, without an icon).</param>
/// <param name="OversizedStacks">Inventory stacks above the itemset's stack size, split on load.</param>
/// <param name="StacksBeyondCapacity">Inventory stacks saved in slots past the inventory's size, moved to free slots.</param>
/// <param name="UnknownEquipment">Worn items the itemset does not define; moved to the inventory.</param>
/// <param name="UnwearableEquipment">Worn items the itemset defines but no longer wears in that slot; moved to the inventory.</param>
/// <param name="ExtraWornItems">Items above one in a worn stack; moved to the inventory.</param>
/// <param name="UnknownBuffs">Running buffs whose item is not a timed consumable of the itemset (they keep their saved effect).</param>
/// <param name="LostItems">Items that would find no room in the inventory and be gone after the load.</param>
public sealed record ItemProblems(
    int UnknownInventoryStacks,
    int OversizedStacks,
    int StacksBeyondCapacity,
    int UnknownEquipment,
    int UnwearableEquipment,
    long ExtraWornItems,
    int UnknownBuffs,
    long LostItems)
{
    /// <summary>Every item fits the itemset as it was saved.</summary>
    public static readonly ItemProblems None = new(0, 0, 0, 0, 0, 0, 0, 0);

    public bool IsClean => this == None;

    /// <summary>The player's items as one list of sentences, or null when <see cref="IsClean"/>.</summary>
    public string? Describe(string itemsetName)
    {
        var notes = new List<string>();
        if (UnknownInventoryStacks > 0) notes.Add($"{UnknownInventoryStacks} inventory stack(s) are items {itemsetName} does not define; they are kept, without an icon.");
        if (OversizedStacks > 0) notes.Add($"{OversizedStacks} inventory stack(s) are above {itemsetName}'s stack size and will be split.");
        if (StacksBeyondCapacity > 0) notes.Add($"{StacksBeyondCapacity} inventory stack(s) were saved past the end of the inventory and will move to free slots.");
        int unworn = UnknownEquipment + UnwearableEquipment;
        if (unworn > 0) notes.Add($"{unworn} worn item(s) can't be worn with {itemsetName} ({UnknownEquipment} unknown, {UnwearableEquipment} not for that slot) and will move to the inventory.");
        if (ExtraWornItems > 0) notes.Add($"{ExtraWornItems} extra item(s) of worn stacks will move to the inventory.");
        if (UnknownBuffs > 0) notes.Add($"{UnknownBuffs} running effect(s) come from items {itemsetName} does not have as timed consumables; they keep their saved effect.");
        if (LostItems > 0) notes.Add($"{LostItems} item(s) will not fit the inventory and will be lost.");
        return notes.Count == 0 ? null : string.Join(" ", notes);
    }
}

/// <summary>
/// Checks a save's player items against an itemset before loading. The inventory and suit are restored into
/// throwaway copies with the same rules a load uses (<see cref="InventoryRestore"/>), so the counts, lost items
/// included, are the ones the player will actually see.
/// </summary>
public static class ItemCheck
{
    /// <param name="inventorySlots">The size of the inventory the load will create.</param>
    /// <param name="fillOrder">Where the load's inventory puts added items (<see cref="InventorySize.FillOrderFor"/>);
    /// over several additions the orders can fit different amounts, so the check must use the load's.</param>
    public static ItemProblems Of(WorldSnapshot snapshot, ItemsetDef itemset, int inventorySlots = Inventory.DefaultSlotCount,
        FillOrder fillOrder = FillOrder.SlotOrder)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(itemset);

        var inventory = Inventory.ForItemset(itemset, inventorySlots);
        inventory.Order = fillOrder;
        var restored = InventoryRestore.Restore(inventory, snapshot.Inventory, itemset);
        int beyond = 0;
        for (int i = inventorySlots; i < snapshot.Inventory.Count; i++)
            if (snapshot.Inventory[i] is { Quantity: > 0 }) beyond++;
        long lost = restored.LostItems;

        int unknownEquipment = 0, unwearable = 0, unknownBuffs = 0;
        long extraWorn = 0;
        if (snapshot.Suit is { } suitState)
        {
            var suit = new Suit(itemset);
            for (int i = 0; i < suitState.Equipment.Count; i++)
            {
                if (suitState.Equipment[i] is not { Quantity: > 0 } stack) continue;
                if (!itemset.ItemsById.ContainsKey(stack.ItemId)) unknownEquipment++;
                else if (i >= Suit.SlotCount || suit.WearableOf(stack.ItemId) is not { } wearable || (int)wearable.Slot != i) unwearable++;
            }
            var suitReport = InventoryRestore.RestoreSuit(suit, suitState, inventory);
            extraWorn = suitReport.ExtraWornItems;
            lost += suitReport.LostItems;
            unknownBuffs = suitState.Buffs.Count(b => b.SecondsLeft > 0 && double.IsFinite(b.SecondsLeft) && !IsTimedConsumable(itemset, b.ItemId));
        }

        return new ItemProblems(restored.UnknownStacks, restored.SplitStacks, beyond, unknownEquipment, unwearable, extraWorn,
            unknownBuffs, lost);
    }

    static bool IsTimedConsumable(ItemsetDef itemset, int itemId) =>
        itemset.ItemsById.TryGetValue(itemId, out var item) &&
        item.Get<ConsumableComponent>() is { Kind: ConsumableKind.TimedBuff };
}
