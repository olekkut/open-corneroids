using System.Collections.ObjectModel;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Items;

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
    /// <summary>Slots 0..8 are the hotbar; nine matches the number keys 1-9.</summary>
    public const int HotbarSize = 9;

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

    /// <summary>Builds an inventory whose stack limits come from the itemset.</summary>
    public static Inventory ForItemset(ItemsetDef set, int slotCount = 36)
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
    /// Adds as much as fits: existing stacks are topped up before empty slots are used, so pickups merge into what
    /// the player already has instead of scattering.
    /// </summary>
    /// <returns>How many did not fit (0 when everything was added).</returns>
    public int Add(int itemId, int count)
    {
        if (count <= 0)
            return 0;
        var next = Snapshot();
        var left = Fill(next, itemId, count);
        Commit(next);
        return left;
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
    /// Puts the trashed stack back the way a pickup arrives (<see cref="Add"/>): onto matching stacks first, then
    /// into empty slots. Whatever does not fit stays in the trash.
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
    internal bool TryExchange(IEnumerable<ItemStack> take, IEnumerable<ItemStack> give)
    {
        var next = Snapshot();
        foreach (var (itemId, quantity) in Totals(take))
        {
            if (CountIn(next, itemId) < quantity)
                return false;
            Drain(next, itemId, quantity);
        }
        foreach (var stack in give)
            if (stack.Quantity > 0 && Fill(next, stack.ItemId, stack.Quantity) > 0)
                return false;
        Commit(next);
        return true;
    }

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

    int Fill(ItemStack?[] target, int itemId, int count)
    {
        var max = MaxStackOf(itemId);
        for (var i = 0; i < target.Length && count > 0; i++)
        {
            if (target[i] is { } stack && stack.ItemId == itemId && stack.Quantity < max)
            {
                var added = Math.Min(count, max - stack.Quantity);
                target[i] = stack with { Quantity = stack.Quantity + added };
                count -= added;
            }
        }
        for (var i = 0; i < target.Length && count > 0; i++)
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

    static long CountIn(ItemStack?[] source, int itemId)
    {
        long total = 0;
        foreach (var stack in source)
            if (stack?.ItemId == itemId)
                total += stack.Quantity;
        return total;
    }

    /// <summary>Caller has checked there is enough.</summary>
    static void Drain(ItemStack?[] target, int itemId, long count)
    {
        for (var i = target.Length - 1; i >= 0 && count > 0; i--)
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
