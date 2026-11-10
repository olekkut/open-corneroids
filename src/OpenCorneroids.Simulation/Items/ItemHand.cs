using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

/// <summary>The mouse button of a click in an item window.</summary>
public enum HandButton
{
    Left,
    Right,
}

/// <summary>What a click on an equipment slot did, so the window can say why nothing happened.</summary>
public enum WornClick
{
    /// <summary>Something moved.</summary>
    Changed,
    /// <summary>Nothing to do: an empty hand on an empty slot, or the same piece already worn.</summary>
    Nothing,
    /// <summary>The held item can't be worn at all.</summary>
    NotWearable,
    /// <summary>The held item is worn in another slot.</summary>
    WrongSlot,
    /// <summary>More than one is held and the piece worn now has no room in the inventory.</summary>
    NoRoom,
}

/// <summary>
/// The stack held on the mouse cursor while an item window is open, shared by every window so stacks move between
/// the hotbar, the inventory, the suit and (later) containers under one set of rules: the original's. A click acts on
/// the press, once:
/// <list type="bullet">
/// <item>empty hand, left click: picks up the whole stack;</item>
/// <item>empty hand, right click: picks up half, rounded down (5 gives 2), and a single item whole;</item>
/// <item>holding, left click: an empty slot takes the stack; the same item with room is topped up to its stack limit and
/// the rest stays in the hand; anything else (another item, or the same item at a full stack) swaps hand and slot,
/// unless the slot could not hold the whole hand, when nothing happens;</item>
/// <item>holding, right click: an empty slot, or the same item with room, takes one.</item>
/// </list>
/// In the original a right click on a different item, while holding a stack, destroys one held item; that is not
/// reproduced, it does nothing. Items are never made or lost here: what leaves a slot is in the hand, and the other
/// way round.
/// </summary>
public sealed class ItemHand
{
    /// <summary>The stack on the cursor, or null when the hand is empty.</summary>
    public ItemStack? Held { get; private set; }

    public bool IsEmpty => Held is null;

    /// <summary>Raised whenever <see cref="Held"/> changes, so the cursor can redraw.</summary>
    public event Action? Changed;

    /// <summary>Applies one click on <paramref name="slot"/> of <paramref name="inventory"/>; returns whether anything moved.</summary>
    public bool Click(Inventory inventory, int slot, HandButton button)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var inSlot = inventory.Slots[slot];
        if (Held is not { } held)
        {
            if (inSlot is null) return false;
            int take = button == HandButton.Left || inSlot.Quantity == 1 ? inSlot.Quantity : inSlot.Quantity / 2;
            return Set(inventory.TakeFromSlot(slot, take));
        }
        if (button == HandButton.Right)
            return inventory.PutIntoSlot(slot, held.ItemId, 1) == 1 && Set(Shrink(held, 1));
        if (inSlot is null || (inSlot.ItemId == held.ItemId && inSlot.Quantity < inventory.MaxStackOf(held.ItemId)))
        {
            int put = inventory.PutIntoSlot(slot, held.ItemId, held.Quantity);
            return put > 0 && Set(Shrink(held, put));
        }
        if (!inventory.CanHoldInSlot(held)) return false;
        return Set(inventory.ExchangeSlot(slot, held));
    }

    /// <summary>
    /// A click on an endless slot (the starting drill and stations) gives one more each time, as in the original: an
    /// empty hand takes one, and a hand holding the same item takes another, up to the item's stack limit. A hand holding
    /// a different item takes nothing.
    /// </summary>
    public bool TakeEndless(int itemId, int maxStack)
    {
        if (Held is null) return Set(new ItemStack(itemId, 1));
        if (Held.ItemId != itemId || Held.Quantity >= Math.Max(1, maxStack)) return false;
        return Set(Held with { Quantity = Held.Quantity + 1 });
    }

    /// <summary>
    /// A click on the trash slot (ours): a held stack is thrown away into <see cref="Inventory.Trash"/>; with an empty
    /// hand the trashed stack comes back onto the cursor.
    /// </summary>
    public bool ClickTrash(Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (Held is { } held)
        {
            inventory.Discard(held);
            return Set(null);
        }
        return Set(inventory.TakeTrash());
    }

    /// <summary>
    /// A click on an equipment slot of <paramref name="suit"/>. An empty hand takes off what is worn there. A held piece
    /// that is worn there goes on; the old piece comes into the hand when the hand held just that one item, and into
    /// <paramref name="inventory"/> otherwise (refused when it has no room). Either button does the same.
    /// </summary>
    public WornClick ClickWorn(Suit suit, WearableSlot slot, Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(suit);
        ArgumentNullException.ThrowIfNull(inventory);
        var worn = suit.Equipment[(int)slot];
        if (Held is not { } held)
        {
            if (worn is null || !suit.TryExchange(slot, null, out var old)) return WornClick.Nothing;
            Set(old);
            return WornClick.Changed;
        }
        if (suit.WearableOf(held.ItemId) is not { } wearable) return WornClick.NotWearable;
        if (wearable.Slot != slot) return WornClick.WrongSlot;
        if (worn is not null && worn.ItemId == held.ItemId) return WornClick.Nothing;
        var one = held with { Quantity = 1 };
        if (held.Quantity == 1)
        {
            if (!suit.TryExchange(slot, one, out var old)) return WornClick.Nothing;
            Set(old);
            return WornClick.Changed;
        }
        if (worn is not null && !inventory.CanAdd([worn])) return WornClick.NoRoom;
        if (!suit.TryExchange(slot, one, out var previous)) return WornClick.Nothing;
        if (previous is not null) inventory.Add(previous.ItemId, previous.Quantity);
        Set(Shrink(held, 1));
        return WornClick.Changed;
    }

    /// <summary>Empties the hand and returns what it held, for tossing it into space.</summary>
    public ItemStack? Release()
    {
        var held = Held;
        Set(null);
        return held;
    }

    /// <summary>
    /// Puts the held stack back into <paramref name="inventory"/> as a pickup would (<see cref="Inventory.Add"/>);
    /// what does not fit stays in the hand. Returns whether the hand is now empty.
    /// </summary>
    public bool ReturnTo(Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (Held is not { } held) return true;
        int left = inventory.Add(held.ItemId, held.Quantity);
        Set(left > 0 ? held with { Quantity = left } : null);
        return Held is null;
    }

    bool Set(ItemStack? stack)
    {
        if (stack is { Quantity: <= 0 }) stack = null;
        if (Held == stack) return false;
        Held = stack;
        Changed?.Invoke();
        return true;
    }

    static ItemStack? Shrink(ItemStack stack, int by) =>
        stack.Quantity > by ? stack with { Quantity = stack.Quantity - by } : null;
}
