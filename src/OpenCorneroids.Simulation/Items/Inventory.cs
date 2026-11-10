using System.Collections.ObjectModel;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

/// <summary>Where <see cref="Inventory.Add"/> and every other pickup-style addition put new items.</summary>
public enum FillOrder
{
    /// <summary>
    /// As in the original: slot by slot from the first, so the hotbar before the main inventory. Each slot takes what
    /// it can, into an empty slot or onto a matching stack, whichever comes first. (The original also hands a slot the
    /// whole requested count instead of what is left, which makes items; that is not reproduced.)
    /// </summary>
    SlotOrder,

    /// <summary>Ours: matching stacks anywhere are topped up first, lowest slot first, then empty slots are used.</summary>
    TopUpFirst,
}

/// <summary>
/// A fixed number of slots, each empty or holding one stack. The first <see cref="HotbarSize"/> slots double as the
/// hotbar, which is why removals drain from the back: the player's hand is the last thing to run dry.
/// </summary>
/// <remarks>
/// Every mutation is computed on a scratch copy and committed in one step. That keeps the all-or-nothing methods
/// honest and lets <see cref="Changed"/> fire exactly once per call, and only when the slots really differ.
/// Slot indices outside the inventory are programming errors and throw; running out of items or space is a
/// normal outcome and is reported through return values.
/// </remarks>
public sealed class Inventory
{
    /// <summary>Slots 0..7 are the hotbar: eight, like the original's, on the number keys 1-8.</summary>
    public const int HotbarSize = 8;

    /// <summary>The main inventory beside the hotbar: 6 × 5 slots, as in the original (<c>sv_inventory_slots</c>).</summary>
    public const int DefaultMainSlots = 30;

    /// <summary>The survival inventory: the hotbar and the 6 × 5 main inventory, 38 slots as in the original.</summary>
    public const int DefaultSlotCount = HotbarSize + DefaultMainSlots;

    /// <summary>Stack limit for item ids the itemset does not define, so stray save data still loads somewhere.</summary>
    public const int UnknownItemMaxStack = 64;

    readonly ItemStack?[] slots;
    readonly Func<int, int> maxStackOf;

    /// <param name="maxStackOf">Stack limit per item id. Values below 1 are treated as 1 so every item can be held.</param>
    public Inventory(int slotCount, Func<int, int> maxStackOf)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotCount);
        ArgumentNullException.ThrowIfNull(maxStackOf);
        slots = new ItemStack?[slotCount];
        this.maxStackOf = maxStackOf;
        Slots = Array.AsReadOnly(slots);
    }

    /// <summary>A live read-only view; it reflects later changes without being re-fetched.</summary>
    public IReadOnlyList<ItemStack?> Slots { get; }

    /// <summary>Raised once per public call that changed the slots or the trash, so UI can redraw without diffing.</summary>
    public event Action? Changed;

    /// <summary>
    /// Where added items go. <see cref="FillOrder.TopUpFirst"/> unless set; the game sets it from
    /// <c>sv_inventory_fill_hotbar_first</c>, whose default is the original's <see cref="FillOrder.SlotOrder"/>. A single
    /// addition fits the same number in either order; over several additions of different items they can differ (slot
    /// order may fill an empty slot that topping up would have left free for the next item).
    /// </summary>
    public FillOrder Order { get; set; } = FillOrder.TopUpFirst;

    /// <summary>Builds an inventory whose stack limits come from the itemset.</summary>
    public static Inventory ForItemset(ItemsetDef set, int slotCount = DefaultSlotCount)
    {
        ArgumentNullException.ThrowIfNull(set);
        return new Inventory(slotCount,
            id => set.ItemsById.TryGetValue(id, out var item) ? item.MaxStackSize : UnknownItemMaxStack);
    }

    /// <summary>The effective stack limit, after clamping the supplied function to at least 1.</summary>
    public int MaxStackOf(int itemId) => Math.Max(1, maxStackOf(itemId));

    /// <summary>
    /// Total quantity of an item across all slots, saturating at <see cref="int.MaxValue"/>: a modded itemset may
    /// allow stacks so large that several of them no longer add up to an int.
    /// </summary>
    public int Count(int itemId) => (int)Math.Min(int.MaxValue, CountIn(slots, itemId));

    /// <summary>The exact total, for rules that must not be fooled by <see cref="Count"/> saturating.</summary>
    internal long CountExact(int itemId) => CountIn(slots, itemId);

    /// <summary>
    /// Adds as much as fits, in the inventory's <see cref="Order"/>: slot by slot from the hotbar as in the original, or
    /// topping up existing stacks before using empty slots (ours), so pickups merge into what the player already has.
    /// </summary>
    /// <returns>How many did not fit (0 when everything was added).</returns>
    public int Add(int itemId, int count) => AddFrom(itemId, count, 0);

    /// <summary>As <see cref="Add"/>, using only the slots from <paramref name="firstSlot"/> on (the main inventory).</summary>
    public int AddFrom(int itemId, int count, int firstSlot)
    {
        if (count <= 0)
            return 0;
        var next = Snapshot();
        var left = Fill(next, itemId, count, firstSlot);
        Commit(next);
        return left;
    }

    /// <summary>
    /// Adds an item picked up from space. With <paramref name="wholeRest"/> in <see cref="FillOrder.SlotOrder"/>, as in
    /// the original: the hotbar takes what it can, slot by slot, and what is left goes into the main inventory only if
    /// all of it fits there; otherwise that part is refused (it stays floating) and the hotbar part stays added.
    /// Without it, or in <see cref="FillOrder.TopUpFirst"/>, it is <see cref="Add"/> (ours: the main inventory takes
    /// what fits).
    /// </summary>
    /// <returns>How many were not taken.</returns>
    public int PickUp(int itemId, int count, bool wholeRest)
    {
        if (count <= 0)
            return 0;
        var next = Snapshot();
        var left = PickUpInto(next, itemId, count, wholeRest);
        Commit(next);
        return left;
    }

    /// <summary>Whether <see cref="PickUp"/> would take any of them; nothing is changed.</summary>
    public bool CanPickUp(int itemId, int count, bool wholeRest) =>
        count > 0 && PickUpInto(Snapshot(), itemId, count, wholeRest) < count;

    /// <summary>
    /// The slots as they would be after <see cref="Add"/> of <paramref name="extra"/>; nothing is changed. Used to save
    /// a stack held on the cursor with the inventory. <paramref name="left"/> is how many would not fit.
    /// </summary>
    public ItemStack?[] SlotsWith(ItemStack? extra, out int left)
    {
        var scratch = Snapshot();
        left = extra is { Quantity: > 0 } ? Fill(scratch, extra.ItemId, extra.Quantity) : 0;
        return scratch;
    }

    /// <summary>Whether all the stacks would fit at once; nothing is changed. Non-positive quantities are ignored.</summary>
    public bool CanAdd(IEnumerable<ItemStack> stacks)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        var scratch = Snapshot();
        foreach (var stack in stacks)
            if (stack.Quantity > 0 && Fill(scratch, stack.ItemId, stack.Quantity) > 0)
                return false;
        return true;
    }

    /// <summary>
    /// Removes exactly <paramref name="count"/> or nothing. Takes from the highest slot first so the hotbar is
    /// spent last. A non-positive count trivially succeeds.
    /// </summary>
    public bool TryRemove(int itemId, int count)
    {
        if (count <= 0)
            return true;
        if (CountIn(slots, itemId) < count)
            return false;
        var next = Snapshot();
        Drain(next, itemId, count);
        Commit(next);
        return true;
    }

    /// <summary>
    /// Removes exactly <paramref name="count"/> from one slot or nothing; used when placing the selected hotbar
    /// block, where taking from another slot would surprise the player. A non-positive count trivially succeeds.
    /// </summary>
    public bool TryTakeFromSlot(int slot, int count)
    {
        CheckSlot(slot);
        if (count <= 0)
            return true;
        if (slots[slot] is not { } stack || stack.Quantity < count)
            return false;
        var next = Snapshot();
        next[slot] = Shrink(stack, count);
        Commit(next);
        return true;
    }

    /// <summary>
    /// Takes up to <paramref name="count"/> out of one slot and returns them (the cursor picking up a stack, as in the
    /// original's item windows). Null when the slot is empty or <paramref name="count"/> is not positive.
    /// </summary>
    public ItemStack? TakeFromSlot(int slot, int count)
    {
        CheckSlot(slot);
        if (count <= 0 || slots[slot] is not { } stack)
            return null;
        var taken = Math.Min(count, stack.Quantity);
        var next = Snapshot();
        next[slot] = Shrink(stack, taken);
        Commit(next);
        return stack with { Quantity = taken };
    }

    /// <summary>
    /// Puts up to <paramref name="count"/> of an item into one slot: an empty slot, or one holding the same item, filled
    /// up to the stack limit. Any other item in the slot takes none.
    /// </summary>
    /// <returns>How many were put in.</returns>
    public int PutIntoSlot(int slot, int itemId, int count)
    {
        CheckSlot(slot);
        if (count <= 0)
            return 0;
        var held = slots[slot];
        if (held is not null && held.ItemId != itemId)
            return 0;
        var added = Math.Min(count, MaxStackOf(itemId) - (held?.Quantity ?? 0));
        if (added <= 0)
            return 0;
        var next = Snapshot();
        next[slot] = new ItemStack(itemId, (held?.Quantity ?? 0) + added);
        Commit(next);
        return added;
    }

    /// <summary>
    /// Replaces one slot's stack with <paramref name="stack"/> (null or a non-positive quantity empties it) and returns
    /// what the slot held. The new stack must fit the slot (<see cref="CanHoldInSlot"/>); a caller swapping the cursor's
    /// stack checks that first, so nothing is ever cut down.
    /// </summary>
    /// <exception cref="ArgumentException">The stack is larger than the item's stack limit.</exception>
    public ItemStack? ExchangeSlot(int slot, ItemStack? stack)
    {
        CheckSlot(slot);
        if (!CanHoldInSlot(stack))
            throw new ArgumentException($"A stack of {stack!.Quantity} is over the limit of {MaxStackOf(stack.ItemId)}.", nameof(stack));
        var old = slots[slot];
        var next = Snapshot();
        next[slot] = stack is null || stack.Quantity <= 0 ? null : stack;
        Commit(next);
        return old;
    }

    /// <summary>Whether one slot can hold all of <paramref name="stack"/> (null and empty stacks always fit).</summary>
    public bool CanHoldInSlot(ItemStack? stack) => stack is null || stack.Quantity <= MaxStackOf(stack.ItemId);

    public void Swap(int a, int b)
    {
        CheckSlot(a);
        CheckSlot(b);
        var next = Snapshot();
        (next[a], next[b]) = (next[b], next[a]);
        Commit(next);
    }

    /// <summary>
    /// The drag-and-drop rule: onto the same item it merges as much as the stack limit allows and leaves the rest
    /// behind; otherwise it swaps. That includes an empty source, which pulls the target's stack back into
    /// <paramref name="from"/>, so the call behaves like <see cref="Swap"/> whenever there is nothing to merge.
    /// </summary>
    public void MoveOrMerge(int from, int to)
    {
        CheckSlot(from);
        CheckSlot(to);
        if (from == to)
            return;
        var next = Snapshot();
        if (next[from] is { } source && next[to] is { } target && target.ItemId == source.ItemId)
        {
            var moved = Math.Min(source.Quantity, MaxStackOf(target.ItemId) - target.Quantity);
            if (moved <= 0)
                return;
            next[to] = target with { Quantity = target.Quantity + moved };
            next[from] = Shrink(source, moved);
        }
        else
        {
            (next[from], next[to]) = (next[to], next[from]);
        }
        Commit(next);
    }

    /// <summary>
    /// Writes a slot directly, for loading saves. Null or a non-positive quantity clears it; an oversized stack is
    /// clamped to the limit because the save may come from an itemset with larger stacks.
    /// </summary>
    public void SetSlot(int slot, ItemStack? stack)
    {
        CheckSlot(slot);
        var next = Snapshot();
        next[slot] = stack is null || stack.Quantity <= 0
            ? null
            : stack with { Quantity = Math.Min(stack.Quantity, MaxStackOf(stack.ItemId)) };
        Commit(next);
    }

    /// <summary>Empties every slot and the trash.</summary>
    public void Clear()
    {
        var hadTrash = Trash is not null;
        Trash = null;
        if (!Commit(new ItemStack?[slots.Length]) && hadTrash)
            Changed?.Invoke();
    }

    /// <summary>
    /// The last stack thrown away, kept so a misclick can be undone. It sits outside <see cref="Slots"/>: it never
    /// counts as owned (crafting, drill power and placing ignore it), and the next discard destroys it for good. Saves
    /// do not keep it. Our rule; the original game's way of getting rid of items is unknown.
    /// </summary>
    public ItemStack? Trash { get; private set; }

    /// <summary>
    /// Moves a whole slot into <see cref="Trash"/>, destroying whatever the trash held. This is the way out of a full
    /// inventory: digging fills it with rock that no recipe uses. An empty slot does nothing.
    /// </summary>
    public void Discard(int slot)
    {
        CheckSlot(slot);
        if (slots[slot] is not { } stack)
            return;
        var next = Snapshot();
        next[slot] = null;
        Trash = stack;
        Commit(next);
    }

    /// <summary>
    /// Throws away a stack that is not in a slot (the one held on the cursor), as <see cref="Discard(int)"/> does: it
    /// becomes <see cref="Trash"/> and whatever the trash held is destroyed. Ours, like the trash itself.
    /// </summary>
    public void Discard(ItemStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (stack.Quantity <= 0)
            return;
        Trash = stack;
        Changed?.Invoke();
    }

    /// <summary>Takes the trashed stack out whole (onto the cursor), leaving the trash empty; null when it was empty.</summary>
    public ItemStack? TakeTrash()
    {
        if (Trash is not { } trash)
            return null;
        Trash = null;
        Changed?.Invoke();
        return trash;
    }

    /// <summary>
    /// Puts the trashed stack back as <see cref="Add"/> does, in the inventory's <see cref="Order"/>. Whatever does not
    /// fit stays in the trash.
    /// </summary>
    /// <returns>How many came back.</returns>
    public int RestoreTrash()
    {
        if (Trash is not { } trash)
            return 0;
        var next = Snapshot();
        var left = Fill(next, trash.ItemId, trash.Quantity);
        if (left == trash.Quantity)
            return 0;
        Trash = left > 0 ? trash with { Quantity = left } : null;
        Commit(next);
        return trash.Quantity - left;
    }

    /// <summary>
    /// Removes <paramref name="take"/> and then adds <paramref name="give"/> as one atomic step: if anything is
    /// missing or the additions do not fit into the space the removals free up, nothing changes.
    /// </summary>
    /// <param name="firstSlot">Only slots from this one on are taken from and filled (crafting leaves the hotbar alone).</param>
    internal bool TryExchange(IEnumerable<ItemStack> take, IEnumerable<ItemStack> give, int firstSlot = 0)
    {
        var next = Snapshot();
        foreach (var (itemId, quantity) in Totals(take))
        {
            if (CountIn(next, itemId, firstSlot) < quantity)
                return false;
            Drain(next, itemId, quantity, firstSlot);
        }
        foreach (var stack in give)
            if (stack.Quantity > 0 && Fill(next, stack.ItemId, stack.Quantity, firstSlot) > 0)
                return false;
        Commit(next);
        return true;
    }

    /// <summary>The exact total of an item in the slots from <paramref name="firstSlot"/> on.</summary>
    public long CountFrom(int itemId, int firstSlot) => CountIn(slots, itemId, firstSlot);

    /// <summary>Sums quantities per item id, skipping non-positive ones, so duplicate entries are checked together.</summary>
    internal static Dictionary<int, long> Totals(IEnumerable<ItemStack> stacks)
    {
        var totals = new Dictionary<int, long>();
        foreach (var stack in stacks)
            if (stack.Quantity > 0)
                totals[stack.ItemId] = totals.GetValueOrDefault(stack.ItemId) + stack.Quantity;
        return totals;
    }

    ItemStack?[] Snapshot() => (ItemStack?[])slots.Clone();

    /// <returns>Whether the slots changed (and <see cref="Changed"/> fired).</returns>
    bool Commit(ItemStack?[] next)
    {
        if (next.AsSpan().SequenceEqual(slots))
            return false;
        next.CopyTo(slots, 0);
        Changed?.Invoke();
        return true;
    }

    void CheckSlot(int slot, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(slot))] string? name = null)
    {
        if ((uint)slot >= (uint)slots.Length)
            throw new ArgumentOutOfRangeException(name, slot, $"slot must be in 0..{slots.Length - 1}");
    }

    /// <summary>The rule of <see cref="PickUp"/> on <paramref name="target"/>; returns how many were not taken.</summary>
    int PickUpInto(ItemStack?[] target, int itemId, int count, bool wholeRest)
    {
        if (!wholeRest || Order != FillOrder.SlotOrder)
            return Fill(target, itemId, count);
        var left = Fill(target, itemId, count, 0, HotbarSize);
        if (left == 0)
            return 0;
        var withMain = (ItemStack?[])target.Clone();
        if (Fill(withMain, itemId, left, HotbarSize) > 0)
            return left;
        withMain.CopyTo(target, 0);
        return 0;
    }

    /// <summary>Adds into the slots from <paramref name="firstSlot"/> up to (not including) <paramref name="endSlot"/>.</summary>
    int Fill(ItemStack?[] target, int itemId, int count, int firstSlot = 0, int endSlot = int.MaxValue)
    {
        var max = MaxStackOf(itemId);
        var end = Math.Min(target.Length, endSlot);
        if (Order == FillOrder.SlotOrder)
        {
            for (var i = Math.Max(0, firstSlot); i < end && count > 0; i++)
            {
                var room = target[i] is not { } held ? max : held.ItemId == itemId ? max - held.Quantity : 0;
                if (room <= 0) continue;
                var added = Math.Min(count, room);
                target[i] = new ItemStack(itemId, max - room + added);
                count -= added;
            }
            return count;
        }
        for (var i = Math.Max(0, firstSlot); i < end && count > 0; i++)
        {
            if (target[i] is { } stack && stack.ItemId == itemId && stack.Quantity < max)
            {
                var added = Math.Min(count, max - stack.Quantity);
                target[i] = stack with { Quantity = stack.Quantity + added };
                count -= added;
            }
        }
        for (var i = Math.Max(0, firstSlot); i < end && count > 0; i++)
        {
            if (target[i] is null)
            {
                var added = Math.Min(count, max);
                target[i] = new ItemStack(itemId, added);
                count -= added;
            }
        }
        return count;
    }

    static long CountIn(ItemStack?[] source, int itemId, int firstSlot = 0)
    {
        long total = 0;
        for (int i = Math.Max(0, firstSlot); i < source.Length; i++)
            if (source[i]?.ItemId == itemId)
                total += source[i]!.Quantity;
        return total;
    }

    /// <summary>Caller has checked there is enough.</summary>
    static void Drain(ItemStack?[] target, int itemId, long count, int firstSlot = 0)
    {
        for (var i = target.Length - 1; i >= Math.Max(0, firstSlot) && count > 0; i--)
        {
            if (target[i] is { } stack && stack.ItemId == itemId)
            {
                var taken = (int)Math.Min(count, stack.Quantity);
                target[i] = Shrink(stack, taken);
                count -= taken;
            }
        }
    }

    static ItemStack? Shrink(ItemStack stack, int by) =>
        stack.Quantity > by ? stack with { Quantity = stack.Quantity - by } : null;
}
