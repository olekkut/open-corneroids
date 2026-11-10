using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Items;

public class ContainersTests
{
    const int A = 7; // stack limit 5
    const int B = 8; // stack limit 64

    static int StackOf(int id) => id == A ? 5 : 64;

    static readonly Int3 Here = new(1, 2, 3);
    static readonly Int3 There = new(4, 0, 0);

    static ItemStack S(int id, int quantity) => new(id, quantity);

    static long Total(Inventory inventory) => inventory.Slots.Sum(s => (long)(s?.Quantity ?? 0));

    // --- size rules ---

    [Theory]
    [InlineData(5, 4, 5, 4, 20)]   // the default itemset's container block
    [InlineData(0, 0, 1, 1, 1)]    // missing values are 1
    [InlineData(9, 6, 7, 5, 35)]   // held within 7 by 5
    [InlineData(-3, 2, 1, 2, 2)]
    public void A_container_is_1_by_1_to_7_by_5_slots_as_in_the_original(int width, int height, int w, int h, int slots)
    {
        var container = new ContainerComponent(width, height);
        Assert.Equal((w, h), ContainerRules.Size(container));
        Assert.Equal(slots, ContainerRules.SlotCount(container));
    }

    [Fact]
    public void Slots_are_numbered_row_by_row()
    {
        Assert.Equal(0, ContainerRules.SlotIndex(0, 0, 5));
        Assert.Equal(4, ContainerRules.SlotIndex(4, 0, 5));
        Assert.Equal(5, ContainerRules.SlotIndex(0, 1, 5));
        Assert.Equal(19, ContainerRules.SlotIndex(4, 3, 5));
    }

    // --- chests on a grid ---

    [Fact]
    public void A_chest_is_made_once_per_cell_and_kept()
    {
        var chests = new GridContainers();
        var first = chests.GetOrCreate(Here, 20, StackOf);

        Assert.Same(first, chests.GetOrCreate(Here, 20, StackOf));
        Assert.Same(first, chests.TryGet(Here));
        Assert.Equal(20, first.Slots.Count);
        Assert.Null(chests.TryGet(There));
    }

    [Fact]
    public void Removing_a_chest_returns_its_stacks_in_slot_order_and_forgets_it()
    {
        var chests = new GridContainers();
        var chest = chests.GetOrCreate(Here, 6, StackOf);
        chest.PutIntoSlot(4, B, 30);
        chest.PutIntoSlot(1, A, 3);

        var spilled = chests.Remove(Here);

        Assert.Equal([S(A, 3), S(B, 30)], spilled);
        Assert.Null(chests.TryGet(Here));
        Assert.Empty(chests.Remove(Here));
    }

    [Fact]
    public void Forgetting_a_chest_drops_its_state_so_a_new_block_there_starts_empty()
    {
        var chests = new GridContainers();
        chests.GetOrCreate(Here, 4, StackOf).PutIntoSlot(0, B, 9);

        chests.Forget(Here);

        Assert.Null(chests.TryGet(Here));
        Assert.All(chests.GetOrCreate(Here, 4, StackOf).Slots, Assert.Null);
    }

    [Fact]
    public void A_launch_moves_only_the_chests_of_the_launched_cells_with_their_contents()
    {
        var chests = new GridContainers();
        var moving = chests.GetOrCreate(Here, 4, StackOf);
        moving.PutIntoSlot(2, B, 11);
        var staying = chests.GetOrCreate(There, 4, StackOf);
        var shift = new Int3(-10, 1, 0);

        var ship = chests.ExtractAndShift(new HashSet<Int3> { Here }, shift);

        Assert.Same(moving, ship.TryGet(Here - shift));
        Assert.Equal(1, ship.Count);
        Assert.Null(chests.TryGet(Here));
        Assert.Same(staying, chests.TryGet(There));
    }

    [Fact]
    public void Only_chests_holding_something_are_saved_and_they_load_back_slot_for_slot()
    {
        var chests = new GridContainers();
        chests.GetOrCreate(There, 4, StackOf);   // empty: not saved, as in the original
        var chest = chests.GetOrCreate(Here, 4, StackOf);
        chest.PutIntoSlot(3, A, 5);
        chest.PutIntoSlot(1, B, 64);

        var saved = chests.ToSnapshot();

        var only = Assert.Single(saved);
        Assert.Equal(Here, only.Cell);
        Assert.Equal([null, S(B, 64), null, S(A, 5)], only.Slots);

        var loaded = new GridContainers();
        var result = loaded.LoadSnapshot(saved, _ => 4, StackOf);
        Assert.Same(ContainerLoad.None, result);
        Assert.Equal(only.Slots, loaded.TryGet(Here)!.Slots);
    }

    [Fact]
    public void Every_chest_of_a_ship_comes_back_unlike_the_original()
    {
        var saved = new[]
        {
            new SavedContainer(Here, [S(B, 1)]),
            new SavedContainer(There, [S(B, 2)]),
            new SavedContainer(new Int3(9, 9, 9), [S(A, 3)]),
        };
        var chests = new GridContainers();

        chests.LoadSnapshot(saved, _ => 20, StackOf);

        Assert.Equal(3, chests.Count);
        Assert.Equal(S(A, 3), chests.TryGet(new Int3(9, 9, 9))!.Slots[0]);
    }

    [Fact]
    public void A_chest_that_became_smaller_keeps_what_fits_and_returns_the_rest()
    {
        // Saved with 6 slots, now 2: slots 0 and 1 come back in place, the others are added where they fit.
        var saved = new SavedContainer(Here, [S(B, 10), null, S(B, 60), S(A, 5), S(A, 5), S(B, 64)]);
        var chests = new GridContainers();

        var result = chests.LoadSnapshot([saved], _ => 2, StackOf);

        var chest = chests.TryGet(Here)!;
        Assert.Equal([S(B, 64), S(B, 64)], chest.Slots);
        Assert.Equal([S(A, 5), S(A, 5), S(B, 6)], result.Leftovers.Select(l => l.Stack));
        Assert.Equal(0, result.Dropped);
        Assert.All(result.Leftovers, l => Assert.Equal(Here, l.Cell));
        Assert.Equal(10 + 60 + 5 + 5 + 64, Total(chest) + result.Leftovers.Sum(l => (long)l.Stack.Quantity));
    }

    [Fact]
    public void A_stack_over_a_smaller_stack_limit_is_split_into_other_slots()
    {
        var saved = new SavedContainer(Here, [S(A, 12), null, null]);
        var chests = new GridContainers();

        var result = chests.LoadSnapshot([saved], _ => 3, StackOf);

        Assert.Equal([S(A, 5), S(A, 5), S(A, 2)], chests.TryGet(Here)!.Slots);
        Assert.Empty(result.Leftovers);
    }

    [Fact]
    public void A_saved_chest_whose_cell_is_no_longer_a_container_is_dropped_and_its_items_returned()
    {
        var saved = new[] { new SavedContainer(Here, [S(B, 3), null, S(A, 2)]), new SavedContainer(There, [S(B, 1)]) };
        var chests = new GridContainers();

        var result = chests.LoadSnapshot(saved, cell => cell == There ? 4 : null, StackOf);

        Assert.Null(chests.TryGet(Here));
        Assert.NotNull(chests.TryGet(There));
        Assert.Equal(1, result.Dropped);
        Assert.Equal([new CellStack(Here, S(B, 3)), new CellStack(Here, S(A, 2))], result.Leftovers);
    }

    [Fact]
    public void A_cell_saved_twice_keeps_the_first_chest_and_returns_the_second_one_s_items()
    {
        var saved = new[] { new SavedContainer(Here, [S(B, 3)]), new SavedContainer(Here, [S(B, 4)]) };
        var chests = new GridContainers();

        var result = chests.LoadSnapshot(saved, _ => 4, StackOf);

        Assert.Equal(S(B, 3), chests.TryGet(Here)!.Slots[0]);
        Assert.Equal(1, result.Dropped);
        Assert.Equal([new CellStack(Here, S(B, 4))], result.Leftovers);
    }

    [Fact]
    public void Loading_replaces_what_the_grid_had_and_nothing_loads_from_nothing()
    {
        var chests = new GridContainers();
        chests.GetOrCreate(Here, 4, StackOf).PutIntoSlot(0, B, 1);

        Assert.Same(ContainerLoad.None, chests.LoadSnapshot(null, _ => 4, StackOf));
        Assert.Equal(0, chests.Count);
    }

    [Fact]
    public void Moving_items_between_the_player_and_a_chest_through_the_hand_keeps_every_item()
    {
        var rng = new Random(1234);
        var player = new Inventory(38, StackOf);
        player.Add(A, 17);
        player.Add(B, 150);
        var chests = new GridContainers();
        var chest = chests.GetOrCreate(Here, 20, StackOf);
        chest.PutIntoSlot(7, A, 4);
        var hand = new ItemHand();
        long before = Total(player) + Total(chest);

        for (int i = 0; i < 5000; i++)
        {
            var inventory = rng.Next(2) == 0 ? player : chest;
            hand.Click(inventory, rng.Next(inventory.Slots.Count), rng.Next(2) == 0 ? HandButton.Left : HandButton.Right);
            Assert.Equal(before, Total(player) + Total(chest) + (hand.Held?.Quantity ?? 0));
        }
        hand.ReturnTo(player);
        Assert.Equal(before, Total(player) + Total(chest) + (hand.Held?.Quantity ?? 0));
        Assert.Equal(before, Total(player) + chests.Remove(Here).Sum(s => (long)s.Quantity) + (hand.Held?.Quantity ?? 0));
    }

    // --- spilling ---

    [Fact]
    public void A_spilled_stack_flies_out_in_a_random_direction_between_the_two_speeds()
    {
        var rng = new Random(42);
        for (int i = 0; i < 2000; i++)
        {
            var velocity = ContainerSpill.Velocity(rng, 1, 4);
            float speed = velocity.Length();
            Assert.InRange(speed, 1f - 1e-4f, 4f + 1e-4f);
        }
    }

    [Fact]
    public void Spilling_is_repeatable_with_the_same_seed_and_covers_every_direction()
    {
        var first = new Random(7);
        var second = new Random(7);
        var signs = new HashSet<(bool, bool, bool)>();
        for (int i = 0; i < 400; i++)
        {
            var v = ContainerSpill.Velocity(first, 1, 4);
            Assert.Equal(v, ContainerSpill.Velocity(second, 1, 4));
            signs.Add((v.X > 0, v.Y > 0, v.Z > 0));
        }
        Assert.Equal(8, signs.Count);
    }

    [Fact]
    public void Equal_speeds_give_exactly_that_speed_and_swapped_ones_are_read_the_other_way()
    {
        var rng = new Random(3);
        Assert.Equal(2f, ContainerSpill.Velocity(rng, 2, 2).Length(), 4);
        for (int i = 0; i < 100; i++)
            Assert.InRange(ContainerSpill.Velocity(rng, 4, 1).Length(), 1f - 1e-4f, 4f + 1e-4f);
        Assert.Equal(0f, ContainerSpill.Velocity(rng, 0, 0).Length());
    }
}
