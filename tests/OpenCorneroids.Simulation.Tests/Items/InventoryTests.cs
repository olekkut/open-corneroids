using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Tests.Items.Support;

namespace OpenCorneroids.Simulation.Tests.Items;

public class InventoryTests
{
    const int A = 7; // stack limit 5
    const int B = 8; // stack limit 10

    static Inventory Small(int slots = 4) => new(slots, id => id == A ? 5 : 10);

    static Inventory With(params ItemStack?[] contents)
    {
        var inventory = Small(contents.Length);
        for (var i = 0; i < contents.Length; i++)
            inventory.SetSlot(i, contents[i]);
        return inventory;
    }

    static ItemStack S(int id, int quantity) => new(id, quantity);

    // --- construction ---

    [Fact]
    public void New_inventory_has_the_requested_number_of_empty_slots()
    {
        var inventory = Small(5);
        Assert.Equal(5, inventory.Slots.Count);
        Assert.All(inventory.Slots, Assert.Null);
    }

    [Fact]
    public void Hotbar_is_eight_slots_like_the_original() => Assert.Equal(8, Inventory.HotbarSize);

    [Fact]
    public void Default_inventory_is_the_hotbar_and_six_by_five_main_slots_as_in_the_original()
    {
        Assert.Equal(30, Inventory.DefaultMainSlots);
        Assert.Equal(38, Inventory.DefaultSlotCount);
        Assert.Equal(38, Inventory.ForItemset(TestItems.Set()).Slots.Count);
    }

    // --- fill order ---

    static Inventory InSlotOrder(params ItemStack?[] contents)
    {
        var inventory = With(contents);
        inventory.Order = FillOrder.SlotOrder;
        return inventory;
    }

    [Fact]
    public void Slot_order_puts_an_item_in_the_first_slot_that_takes_it_as_in_the_original()
    {
        // The empty slot 0 comes before the matching stack in slot 1, so it is used first.
        var inventory = InSlotOrder(null, S(A, 3), S(B, 1), null);
        Assert.Equal(0, inventory.Add(A, 4));
        Assert.Equal(["7x4", "7x3", "8x1", "-"], inventory.Layout());
    }

    [Fact]
    public void Slot_order_tops_up_a_stack_that_comes_before_an_empty_slot()
    {
        var inventory = InSlotOrder(S(B, 1), S(A, 3), null, S(A, 1));
        Assert.Equal(0, inventory.Add(A, 6));
        Assert.Equal(["8x1", "7x5", "7x4", "7x1"], inventory.Layout());
    }

    [Fact]
    public void Slot_order_adds_only_what_is_left_to_each_slot()
    {
        // The original gives every slot it tries the whole count, which makes items; ours adds exactly what was asked.
        var inventory = InSlotOrder(S(A, 4), null, null, null);
        Assert.Equal(0, inventory.Add(A, 3));
        Assert.Equal(7, inventory.Count(A));
        Assert.Equal(["7x5", "7x2", "-", "-"], inventory.Layout());
    }

    [Fact]
    public void Slot_order_fills_the_hotbar_before_the_main_inventory()
    {
        var inventory = new Inventory(Inventory.HotbarSize + 2, _ => 1) { Order = FillOrder.SlotOrder };
        inventory.SetSlot(Inventory.HotbarSize, S(A, 1));
        Assert.Equal(0, inventory.Add(A, Inventory.HotbarSize));
        Assert.All(inventory.Slots.Take(Inventory.HotbarSize), s => Assert.Equal(S(A, 1), s));
        Assert.Null(inventory.Slots[Inventory.HotbarSize + 1]);

        Assert.Equal(1, inventory.AddFrom(B, 2, Inventory.HotbarSize));   // the main inventory only: one free slot
        Assert.Equal(S(B, 1), inventory.Slots[Inventory.HotbarSize + 1]);
    }

    [Fact]
    public void Both_fill_orders_fit_the_same_number_of_items_in_one_addition()
    {
        var random = new Random(38);
        for (int round = 0; round < 500; round++)
        {
            var contents = new ItemStack?[6];
            for (int i = 0; i < contents.Length; i++)
                contents[i] = random.Next(3) switch { 0 => null, 1 => S(A, random.Next(1, 6)), _ => S(B, random.Next(1, 11)) };
            int item = random.Next(2) == 0 ? A : B, count = random.Next(1, 60);
            var slotOrder = InSlotOrder(contents);
            var topUp = With(contents);

            int leftSlotOrder = slotOrder.Add(item, count), leftTopUp = topUp.Add(item, count);

            Assert.Equal(leftTopUp, leftSlotOrder);
            Assert.Equal(topUp.Count(item), slotOrder.Count(item));
            Assert.Equal(With(contents).Count(item) + count - leftSlotOrder, slotOrder.Count(item));
        }
    }

    [Fact]
    public void Over_several_additions_the_fill_orders_can_fit_different_amounts()
    {
        // Slot order spends the empty slot 0 on A, which topping up would have put onto slot 1, leaving no room for B.
        var slotOrder = InSlotOrder(null, S(A, 1));
        var topUp = With(null, S(A, 1));

        Assert.Equal(0, slotOrder.Add(A, 4));
        Assert.Equal(1, slotOrder.Add(B, 1));
        Assert.Equal(0, topUp.Add(A, 4));
        Assert.Equal(0, topUp.Add(B, 1));
    }

    // --- picking up from space ---

    /// <summary>A full hotbar except slot 0 (A×3) and a main inventory of two slots, slot 9 holding a full B.</summary>
    static Inventory HotbarNearlyFull()
    {
        var inventory = new Inventory(Inventory.HotbarSize + 2, id => id == A ? 5 : 10) { Order = FillOrder.SlotOrder };
        inventory.SetSlot(0, S(A, 3));
        for (int i = 1; i < Inventory.HotbarSize; i++) inventory.SetSlot(i, S(B, 10));
        inventory.SetSlot(Inventory.HotbarSize + 1, S(B, 10));
        return inventory;
    }

    [Fact]
    public void A_pickup_puts_the_rest_in_the_main_inventory_only_if_all_of_it_fits_as_in_the_original()
    {
        var inventory = HotbarNearlyFull();

        Assert.Equal(6, inventory.PickUp(A, 8, wholeRest: true));   // the hotbar takes 2; 6 do not fit one free slot of 5
        Assert.Equal(S(A, 5), inventory.Slots[0]);
        Assert.Null(inventory.Slots[Inventory.HotbarSize]);

        Assert.Equal(0, inventory.PickUp(A, 5, wholeRest: true));   // all of it fits the main inventory
        Assert.Equal(S(A, 5), inventory.Slots[Inventory.HotbarSize]);
    }

    [Fact]
    public void Without_the_whole_rest_rule_a_pickup_keeps_what_fits()
    {
        var inventory = HotbarNearlyFull();
        Assert.Equal(1, inventory.PickUp(A, 8, wholeRest: false));   // 2 in the hotbar, 5 in the free main slot
        Assert.Equal(10, inventory.Count(A));

        var topUp = HotbarNearlyFull();
        topUp.Order = FillOrder.TopUpFirst;   // the rule belongs to the original's slot order only
        Assert.Equal(1, topUp.PickUp(A, 8, wholeRest: true));
    }

    [Fact]
    public void Can_pick_up_says_whether_a_pickup_would_take_anything()
    {
        var inventory = HotbarNearlyFull();
        inventory.SetSlot(0, S(A, 5));   // the hotbar is full now

        Assert.False(inventory.CanPickUp(A, 6, wholeRest: true));
        Assert.True(inventory.CanPickUp(A, 6, wholeRest: false));
        Assert.True(inventory.CanPickUp(A, 5, wholeRest: true));
        Assert.False(inventory.CanPickUp(A, 0, wholeRest: true));
        Assert.Equal(S(A, 5), inventory.Slots[0]);
        Assert.Null(inventory.Slots[Inventory.HotbarSize]);   // nothing changed
    }

    [Fact]
    public void Zero_slots_is_allowed_and_holds_nothing()
    {
        var inventory = Small(0);
        Assert.Equal(3, inventory.Add(A, 3));
        Assert.False(inventory.CanAdd([S(A, 1)]));
    }

    [Fact]
    public void Negative_slot_count_and_null_stack_function_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory(-1, _ => 1));
        Assert.Throws<ArgumentNullException>(() => new Inventory(1, null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Max_stack_below_one_is_clamped_to_one(int limit)
    {
        var inventory = new Inventory(3, _ => limit);
        Assert.Equal(1, inventory.MaxStackOf(A));
        Assert.Equal(0, inventory.Add(A, 3));
        Assert.Equal(["7x1", "7x1", "7x1"], inventory.Layout());
    }

    [Fact]
    public void Changed_fires_after_the_slots_are_updated()
    {
        // Handlers redraw from Slots instead of diffing, so they must never see the old contents.
        var inventory = Small();
        var seen = new List<ItemStack?>();
        inventory.Changed += () => seen.Add(inventory.Slots[0]);
        inventory.Add(A, 3);
        inventory.SetSlot(0, S(B, 7));
        inventory.Clear();
        Assert.Equal([S(A, 3), S(B, 7), null], seen);
    }

    // --- huge stack limits (modded itemsets) ---

    [Fact]
    public void Count_saturates_instead_of_overflowing()
    {
        var inventory = new Inventory(3, _ => int.MaxValue);
        Assert.Equal(0, inventory.Add(A, int.MaxValue));
        Assert.Equal(0, inventory.Add(A, int.MaxValue));
        Assert.Equal(int.MaxValue, inventory.Count(A));
        Assert.Equal(0, inventory.Add(A, int.MaxValue));
        Assert.Equal(int.MaxValue, inventory.Count(A));
    }

    [Fact]
    public void TryRemove_works_when_the_total_exceeds_int_range()
    {
        var inventory = new Inventory(2, _ => int.MaxValue);
        inventory.SetSlot(0, S(A, int.MaxValue));
        inventory.SetSlot(1, S(A, int.MaxValue));
        Assert.True(inventory.TryRemove(A, int.MaxValue));
        Assert.Equal([S(A, int.MaxValue), null], inventory.Slots);
        Assert.True(inventory.TryRemove(A, 1));
        Assert.Equal(int.MaxValue - 1, inventory.Count(A));
    }

    [Fact]
    public void Slots_view_is_live_and_read_only()
    {
        var inventory = Small();
        var view = inventory.Slots;
        inventory.Add(A, 2);
        Assert.Equal(S(A, 2), view[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<ItemStack?>)view)[0] = null);
    }

    // --- Add ---

    [Fact]
    public void Add_fills_empty_slots_lowest_first_respecting_stack_limit()
    {
        var inventory = Small();
        Assert.Equal(0, inventory.Add(A, 12));
        Assert.Equal(["7x5", "7x5", "7x2", "-"], inventory.Layout());
    }

    [Fact]
    public void Add_tops_up_existing_stacks_before_using_empty_slots()
    {
        var inventory = With(null, S(A, 3), S(B, 1), S(A, 4));
        Assert.Equal(0, inventory.Add(A, 4));
        Assert.Equal(["7x1", "7x5", "8x1", "7x5"], inventory.Layout());
    }

    [Fact]
    public void Add_tops_up_lowest_existing_stack_first()
    {
        var inventory = With(S(A, 1), null, S(A, 1), null);
        inventory.Add(A, 4);
        Assert.Equal(["7x5", "-", "7x1", "-"], inventory.Layout());
    }

    [Fact]
    public void Add_returns_what_did_not_fit_and_keeps_what_did()
    {
        var inventory = With(S(B, 10), S(A, 4), null);
        Assert.Equal(4, inventory.Add(A, 10));
        Assert.Equal(["8x10", "7x5", "7x5"], inventory.Layout());
    }

    [Fact]
    public void Add_into_a_full_inventory_returns_everything_and_raises_nothing()
    {
        var inventory = With(S(B, 10), S(A, 5));
        var changes = new ChangeCounter(inventory);
        Assert.Equal(6, inventory.Add(A, 6));
        Assert.Equal(0, changes.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Add_of_non_positive_count_is_a_no_op(int count)
    {
        var inventory = Small();
        var changes = new ChangeCounter(inventory);
        Assert.Equal(0, inventory.Add(A, count));
        Assert.All(inventory.Slots, Assert.Null);
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void Add_raises_changed_exactly_once_even_across_many_slots()
    {
        var inventory = Small();
        var changes = new ChangeCounter(inventory);
        inventory.Add(A, 17);
        Assert.Equal(1, changes.Count);
    }

    [Fact]
    public void Add_uses_each_items_own_stack_limit()
    {
        var inventory = Small();
        inventory.Add(B, 15);
        inventory.Add(A, 6);
        Assert.Equal(["8x10", "8x5", "7x5", "7x1"], inventory.Layout());
    }

    // --- Count / CanAdd ---

    [Fact]
    public void Count_sums_all_stacks_of_an_item()
    {
        var inventory = With(S(A, 2), S(B, 9), S(A, 5));
        Assert.Equal(7, inventory.Count(A));
        Assert.Equal(9, inventory.Count(B));
        Assert.Equal(0, inventory.Count(99));
    }

    [Fact]
    public void CanAdd_checks_all_stacks_together_without_mutating()
    {
        var inventory = With(S(A, 3), null, null);
        var changes = new ChangeCounter(inventory);

        Assert.True(inventory.CanAdd([S(A, 2), S(B, 10), S(A, 5)]));  // 3+2 tops up, then one slot each
        Assert.False(inventory.CanAdd([S(A, 2), S(B, 10), S(A, 6)])); // second A needs two slots
        Assert.Equal(["7x3", "-", "-"], inventory.Layout());
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void CanAdd_counts_space_used_by_earlier_stacks_in_the_same_request()
    {
        var inventory = Small(1);
        Assert.True(inventory.CanAdd([S(A, 2), S(A, 3)]));
        Assert.False(inventory.CanAdd([S(A, 2), S(B, 1)]));
    }

    [Fact]
    public void CanAdd_ignores_non_positive_quantities_and_accepts_empty_requests()
    {
        var inventory = Small(0);
        Assert.True(inventory.CanAdd([]));
        Assert.True(inventory.CanAdd([S(A, 0), S(B, -2)]));
    }

    // --- TryRemove ---

    [Fact]
    public void TryRemove_takes_from_the_highest_slot_first()
    {
        var inventory = With(S(A, 5), S(A, 2), S(B, 1), S(A, 3));
        Assert.True(inventory.TryRemove(A, 4));
        Assert.Equal(["7x5", "7x1", "8x1", "-"], inventory.Layout());
    }

    [Fact]
    public void TryRemove_spends_hotbar_stacks_last()
    {
        var inventory = new Inventory(12, _ => 10);
        inventory.SetSlot(0, S(A, 10));  // hotbar
        inventory.SetSlot(11, S(A, 3));  // backpack
        Assert.True(inventory.TryRemove(A, 5));
        Assert.Equal(S(A, 8), inventory.Slots[0]);
        Assert.Null(inventory.Slots[11]);
    }

    [Fact]
    public void TryRemove_is_all_or_nothing()
    {
        var inventory = With(S(A, 2), S(A, 3));
        var changes = new ChangeCounter(inventory);
        Assert.False(inventory.TryRemove(A, 6));
        Assert.Equal(["7x2", "7x3"], inventory.Layout());
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void TryRemove_of_exactly_everything_empties_the_slots()
    {
        var inventory = With(S(A, 2), S(A, 3));
        var changes = new ChangeCounter(inventory);
        Assert.True(inventory.TryRemove(A, 5));
        Assert.All(inventory.Slots, Assert.Null);
        Assert.Equal(1, changes.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryRemove_of_non_positive_count_succeeds_without_change(int count)
    {
        var inventory = With(S(A, 2));
        var changes = new ChangeCounter(inventory);
        Assert.True(inventory.TryRemove(A, count));
        Assert.True(inventory.TryRemove(99, count));
        Assert.Equal(S(A, 2), inventory.Slots[0]);
        Assert.Equal(0, changes.Count);
    }

    // --- TryTakeFromSlot ---

    [Fact]
    public void TryTakeFromSlot_takes_only_from_that_slot()
    {
        var inventory = With(S(A, 3), S(A, 5));
        Assert.True(inventory.TryTakeFromSlot(0, 2));
        Assert.Equal(["7x1", "7x5"], inventory.Layout());
        Assert.True(inventory.TryTakeFromSlot(0, 1));
        Assert.Equal(["-", "7x5"], inventory.Layout());
    }

    [Fact]
    public void TryTakeFromSlot_fails_without_change_when_the_slot_is_short_or_empty()
    {
        var inventory = With(S(A, 3), S(A, 5), null);
        var changes = new ChangeCounter(inventory);
        Assert.False(inventory.TryTakeFromSlot(0, 4)); // other slots do not help
        Assert.False(inventory.TryTakeFromSlot(2, 1));
        Assert.Equal(["7x3", "7x5", "-"], inventory.Layout());
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void TryTakeFromSlot_raises_changed_once_on_success()
    {
        var inventory = With(S(A, 3));
        var changes = new ChangeCounter(inventory);
        inventory.TryTakeFromSlot(0, 1);
        Assert.Equal(1, changes.Count);
    }

    [Fact]
    public void TryTakeFromSlot_of_non_positive_count_succeeds_without_change()
    {
        var inventory = With(null, S(A, 1));
        var changes = new ChangeCounter(inventory);
        Assert.True(inventory.TryTakeFromSlot(0, 0));
        Assert.True(inventory.TryTakeFromSlot(1, -2));
        Assert.Equal(0, changes.Count);
    }

    // --- Swap / MoveOrMerge ---

    [Fact]
    public void Swap_exchanges_two_slots()
    {
        var inventory = With(S(A, 1), null, S(B, 2));
        var changes = new ChangeCounter(inventory);
        inventory.Swap(0, 2);
        Assert.Equal(["8x2", "-", "7x1"], inventory.Layout());
        inventory.Swap(0, 1);
        Assert.Equal(["-", "8x2", "7x1"], inventory.Layout());
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void Swap_that_changes_nothing_raises_nothing()
    {
        var inventory = With(S(A, 1), S(A, 1), null, null);
        var changes = new ChangeCounter(inventory);
        inventory.Swap(0, 0);
        inventory.Swap(0, 1); // identical stacks
        inventory.Swap(2, 3); // both empty
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void MoveOrMerge_into_an_empty_slot_moves()
    {
        var inventory = With(S(A, 3), null);
        inventory.MoveOrMerge(0, 1);
        Assert.Equal(["-", "7x3"], inventory.Layout());
    }

    [Fact]
    public void MoveOrMerge_onto_a_different_item_swaps()
    {
        var inventory = With(S(A, 3), S(B, 4));
        inventory.MoveOrMerge(0, 1);
        Assert.Equal(["8x4", "7x3"], inventory.Layout());
    }

    [Fact]
    public void MoveOrMerge_onto_the_same_item_merges_everything_that_fits()
    {
        var inventory = With(S(A, 2), S(A, 3));
        var changes = new ChangeCounter(inventory);
        inventory.MoveOrMerge(0, 1);
        Assert.Equal(["-", "7x5"], inventory.Layout());
        Assert.Equal(1, changes.Count);
    }

    [Fact]
    public void MoveOrMerge_leaves_the_remainder_in_the_source_slot()
    {
        var inventory = With(S(A, 4), S(A, 3));
        inventory.MoveOrMerge(0, 1);
        Assert.Equal(["7x2", "7x5"], inventory.Layout());
    }

    [Fact]
    public void MoveOrMerge_onto_a_full_stack_of_the_same_item_changes_nothing()
    {
        var inventory = With(S(A, 2), S(A, 5));
        var changes = new ChangeCounter(inventory);
        inventory.MoveOrMerge(0, 1);
        Assert.Equal(["7x2", "7x5"], inventory.Layout());
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void MoveOrMerge_from_an_empty_slot_onto_a_stack_swaps()
    {
        // An empty source is "not the same item", so the general swap rule applies and the stack moves back.
        var inventory = With(null, S(A, 2));
        var changes = new ChangeCounter(inventory);
        inventory.MoveOrMerge(0, 1);
        Assert.Equal(["7x2", "-"], inventory.Layout());
        Assert.Equal(1, changes.Count);
    }

    [Fact]
    public void MoveOrMerge_between_empty_slots_or_onto_itself_changes_nothing()
    {
        var inventory = With(null, S(A, 2), null);
        var changes = new ChangeCounter(inventory);
        inventory.MoveOrMerge(0, 2);
        inventory.MoveOrMerge(1, 1);
        Assert.Equal(["-", "7x2", "-"], inventory.Layout());
        Assert.Equal(0, changes.Count);
    }

    // --- SetSlot / Clear ---

    [Fact]
    public void SetSlot_clamps_to_the_stack_limit()
    {
        var inventory = Small();
        inventory.SetSlot(2, S(A, 50));
        Assert.Equal(S(A, 5), inventory.Slots[2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void SetSlot_clamps_to_at_least_one_when_the_stack_function_is_below_one(int limit)
    {
        var inventory = new Inventory(1, _ => limit);
        inventory.SetSlot(0, S(A, 5));
        Assert.Equal(S(A, 1), inventory.Slots[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void SetSlot_with_non_positive_quantity_clears(int quantity)
    {
        var inventory = With(S(A, 2));
        inventory.SetSlot(0, S(B, quantity));
        Assert.Null(inventory.Slots[0]);
    }

    [Fact]
    public void SetSlot_with_null_clears()
    {
        var inventory = With(S(A, 2));
        inventory.SetSlot(0, null);
        Assert.Null(inventory.Slots[0]);
    }

    [Fact]
    public void SetSlot_raises_changed_only_when_the_slot_differs()
    {
        var inventory = Small();
        var changes = new ChangeCounter(inventory);
        inventory.SetSlot(0, S(A, 2));
        inventory.SetSlot(0, S(A, 2));   // same value
        inventory.SetSlot(1, null);      // already empty
        inventory.SetSlot(1, S(A, 0));   // clears an empty slot
        inventory.SetSlot(0, S(A, 9));   // clamped to 5: a change
        inventory.SetSlot(0, S(A, 7));   // clamped to 5 again: no change
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void Clear_empties_everything_and_raises_once()
    {
        var inventory = With(S(A, 1), S(B, 2), null);
        var changes = new ChangeCounter(inventory);
        inventory.Clear();
        Assert.All(inventory.Slots, Assert.Null);
        Assert.Equal(1, changes.Count);
        inventory.Clear();
        Assert.Equal(1, changes.Count);
    }

    // --- slot index validation ---

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Out_of_range_slots_throw(int slot)
    {
        var inventory = Small();
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.TryTakeFromSlot(slot, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Swap(0, slot));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Swap(slot, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.MoveOrMerge(slot, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.MoveOrMerge(0, slot));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.SetSlot(slot, S(A, 1)));
    }

    // --- ForItemset ---

    [Fact]
    public void ForItemset_takes_stack_limits_from_the_itemset()
    {
        var inventory = TestItems.NewInventory();
        Assert.Equal(36, inventory.Slots.Count);
        Assert.Equal(10, inventory.MaxStackOf(TestItems.Plate));
        Assert.Equal(64, inventory.MaxStackOf(TestItems.Rock));
        Assert.Equal(1, inventory.MaxStackOf(TestItems.WeakDrill));
    }

    [Fact]
    public void ForItemset_uses_64_for_unknown_ids_and_clamps_zero_limits()
    {
        var inventory = TestItems.NewInventory(4);
        Assert.Equal(4, inventory.Slots.Count);
        Assert.Equal(Inventory.UnknownItemMaxStack, inventory.MaxStackOf(TestItems.Unknown));
        Assert.Equal(64, Inventory.UnknownItemMaxStack);
        Assert.Equal(1, inventory.MaxStackOf(TestItems.Single));
    }

    [Fact]
    public void ForItemset_stacks_tools_one_per_slot()
    {
        var inventory = TestItems.NewInventory(3);
        Assert.Equal(1, inventory.Add(TestItems.WeakDrill, 4));
        Assert.Equal(["20x1", "20x1", "20x1"], inventory.Layout());
    }

    // --- trash ---

    [Fact]
    public void Discarding_moves_the_whole_slot_into_the_trash()
    {
        var inventory = With(S(A, 5), S(B, 3));
        var changes = 0;
        inventory.Changed += () => changes++;

        inventory.Discard(0);

        Assert.Equal([null, S(B, 3)], inventory.Slots);
        Assert.Equal(S(A, 5), inventory.Trash);
        Assert.Equal(0, inventory.Count(A));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Discarding_an_empty_slot_does_nothing()
    {
        var inventory = With(S(A, 5), null);
        inventory.Discard(0);
        var changes = 0;
        inventory.Changed += () => changes++;

        inventory.Discard(1);

        Assert.Equal(S(A, 5), inventory.Trash);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void The_next_discard_destroys_what_the_trash_held()
    {
        var inventory = With(S(A, 5), S(B, 3));
        inventory.Discard(0);
        inventory.Discard(1);

        Assert.Equal(S(B, 3), inventory.Trash);
        Assert.Equal(3, inventory.RestoreTrash());
        Assert.Equal(0, inventory.Count(A));
        Assert.Equal(3, inventory.Count(B));
    }

    [Fact]
    public void Restoring_merges_first_and_leaves_what_does_not_fit_in_the_trash()
    {
        var inventory = With(S(A, 4), S(A, 5), S(B, 10));
        inventory.Discard(1);                  // trash: 5 A
        inventory.SetSlot(1, S(B, 10));        // the freed slot fills up again

        Assert.Equal(1, inventory.RestoreTrash());
        Assert.Equal([S(A, 5), S(B, 10), S(B, 10)], inventory.Slots);
        Assert.Equal(S(A, 4), inventory.Trash);
    }

    [Fact]
    public void Restoring_with_no_room_or_an_empty_trash_changes_nothing()
    {
        var inventory = With(S(B, 10));
        Assert.Equal(0, inventory.RestoreTrash());
        inventory.Discard(0);
        inventory.SetSlot(0, S(A, 5));
        var changes = 0;
        inventory.Changed += () => changes++;

        Assert.Equal(0, inventory.RestoreTrash());
        Assert.Equal(S(B, 10), inventory.Trash);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Clear_empties_the_trash_too_and_reports_it()
    {
        var inventory = With(S(A, 5), null);
        inventory.Discard(0);
        var changes = 0;
        inventory.Changed += () => changes++;

        inventory.Clear();

        Assert.Null(inventory.Trash);
        Assert.Equal(1, changes);
    }

    // --- single-slot moves for the cursor stack ---

    [Fact]
    public void TakeFromSlot_returns_what_it_took_and_leaves_the_rest()
    {
        var inventory = With(S(B, 7));
        var changes = new ChangeCounter(inventory);

        Assert.Equal(S(B, 3), inventory.TakeFromSlot(0, 3));
        Assert.Equal(S(B, 4), inventory.Slots[0]);
        Assert.Equal(S(B, 4), inventory.TakeFromSlot(0, 99));
        Assert.Null(inventory.Slots[0]);
        Assert.Null(inventory.TakeFromSlot(0, 1));
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void PutIntoSlot_fills_an_empty_or_matching_slot_up_to_the_limit_only()
    {
        var inventory = With(null, S(A, 3), S(B, 1));
        var changes = new ChangeCounter(inventory);

        Assert.Equal(5, inventory.PutIntoSlot(0, A, 9));
        Assert.Equal(2, inventory.PutIntoSlot(1, A, 9));
        Assert.Equal(0, inventory.PutIntoSlot(2, A, 1));
        Assert.Equal(0, inventory.PutIntoSlot(1, A, 1));
        Assert.Equal([S(A, 5), S(A, 5), S(B, 1)], inventory.Slots);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void ExchangeSlot_returns_the_old_stack_and_refuses_an_oversized_one()
    {
        var inventory = With(S(B, 2));
        var changes = new ChangeCounter(inventory);

        Assert.Equal(S(B, 2), inventory.ExchangeSlot(0, S(A, 4)));
        Assert.Equal(S(A, 4), inventory.Slots[0]);
        Assert.Throws<ArgumentException>(() => inventory.ExchangeSlot(0, S(A, 6)));
        Assert.Equal(S(A, 4), inventory.Slots[0]);
        Assert.Equal(S(A, 4), inventory.ExchangeSlot(0, null));
        Assert.Null(inventory.Slots[0]);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void A_stack_off_the_slots_can_be_trashed_and_taken_back_whole()
    {
        var inventory = With((ItemStack?)null);
        var changes = new ChangeCounter(inventory);

        inventory.Discard(S(B, 30));
        Assert.Equal(S(B, 30), inventory.Trash);
        Assert.Equal(S(B, 30), inventory.TakeTrash());
        Assert.Null(inventory.Trash);
        Assert.Null(inventory.TakeTrash());
        Assert.Equal(2, changes.Count);
    }
}
