using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Tests.Items.Support;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;

namespace OpenCorneroids.Simulation.Tests.Items;

public class CraftingTests
{
    static readonly ItemsetDef TestSet = Set();

    static Inventory Holding(int slotCount, params ItemStack[] stacks)
    {
        var inventory = Inventory.ForItemset(TestSet, slotCount);
        foreach (var stack in stacks)
            Assert.Equal(0, inventory.Add(stack.ItemId, stack.Quantity));
        return inventory;
    }

    // --- For ---

    [Fact]
    public void For_lists_blueprints_of_one_station_in_file_order()
    {
        Assert.Equal([PlateFromRock, DrillFromPlates], Crafting.For(TestSet, CraftingStation.CraftingTable));
        Assert.Equal([BarFromRockAndOre, GlassFromRock], Crafting.For(TestSet, CraftingStation.Smelter));
        Assert.Equal([PlatesFromBar], Crafting.For(TestSet, CraftingStation.Extractor));
    }

    [Fact]
    public void For_is_empty_for_an_itemset_without_blueprints() =>
        Assert.Empty(Crafting.For(Set(blueprints: []), CraftingStation.CraftingTable));

    // --- MaxCraftable ---

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(7, 3)]
    [InlineData(130, 65)]
    public void MaxCraftable_divides_what_is_on_hand_by_what_is_needed(int rocks, int expected)
    {
        var inventory = Holding(36, S(Rock, rocks));
        Assert.Equal(expected, Crafting.MaxCraftable(inventory, PlateFromRock));
    }

    [Fact]
    public void MaxCraftable_adds_up_duplicate_ingredient_ids()
    {
        // Bar needs Rock 1 + Rock 2 (= 3 rock) and Ore 1 per run.
        Assert.Equal(1, Crafting.MaxCraftable(Holding(10, S(Rock, 5), S(Ore, 9)), BarFromRockAndOre));
        Assert.Equal(3, Crafting.MaxCraftable(Holding(10, S(Rock, 9), S(Ore, 9)), BarFromRockAndOre));
    }

    [Fact]
    public void MaxCraftable_is_limited_by_the_scarcest_ingredient()
    {
        Assert.Equal(2, Crafting.MaxCraftable(Holding(10, S(Rock, 60), S(Ore, 2)), BarFromRockAndOre));
        Assert.Equal(0, Crafting.MaxCraftable(Holding(10, S(Rock, 60)), BarFromRockAndOre));
    }

    [Fact]
    public void MaxCraftable_ignores_output_space()
    {
        var inventory = Holding(1, S(Rock, 4)); // the only slot is taken by rock
        Assert.Equal(2, Crafting.MaxCraftable(inventory, PlateFromRock));
    }

    [Fact]
    public void MaxCraftable_of_a_blueprint_without_ingredients_is_unbounded()
    {
        var free = Bp("Free", CraftingStation.Extractor, [], S(Bar, 1));
        Assert.Equal(int.MaxValue, Crafting.MaxCraftable(Holding(1), free));
    }

    [Fact]
    public void MaxCraftable_skips_non_positive_ingredient_quantities()
    {
        var odd = Bp("Odd", CraftingStation.CraftingTable, [S(Rock, 2), S(Ore, 0)], S(Plate, 1));
        Assert.Equal(2, Crafting.MaxCraftable(Holding(2, S(Rock, 4)), odd));
    }

    // --- TryCraft: success ---

    [Fact]
    public void TryCraft_consumes_ingredients_and_adds_the_result()
    {
        var inventory = Holding(36, S(Rock, 5));
        Assert.True(Crafting.TryCraft(inventory, TestSet, PlateFromRock));
        Assert.Equal(3, inventory.Count(Rock));
        Assert.Equal(1, inventory.Count(Plate));
    }

    [Fact]
    public void TryCraft_multiplies_ingredients_and_results_by_times()
    {
        var inventory = Holding(36, S(Rock, 10), S(Ore, 4));
        Assert.True(Crafting.TryCraft(inventory, TestSet, BarFromRockAndOre, times: 3));
        Assert.Equal(1, inventory.Count(Rock));
        Assert.Equal(1, inventory.Count(Ore));
        Assert.Equal(6, inventory.Count(Bar));
    }

    [Fact]
    public void TryCraft_can_use_up_every_ingredient()
    {
        var inventory = Holding(36, S(Rock, 6));
        Assert.True(Crafting.TryCraft(inventory, TestSet, PlateFromRock, times: Crafting.MaxCraftable(inventory, PlateFromRock)));
        Assert.Equal(0, inventory.Count(Rock));
        Assert.Equal(3, inventory.Count(Plate));
    }

    [Fact]
    public void TryCraft_raises_changed_exactly_once()
    {
        var inventory = Holding(36, S(Rock, 10), S(Ore, 4));
        var changes = new ChangeCounter(inventory);
        Crafting.TryCraft(inventory, TestSet, BarFromRockAndOre, times: 2);
        Assert.Equal(1, changes.Count);
    }

    [Fact]
    public void TryCraft_results_may_go_into_the_slot_the_ingredients_freed()
    {
        // Two slots: the rock stack is used up entirely, which frees the slot the plates need.
        var inventory = Holding(2, S(Rock, 2), S(Ore, 1));
        Assert.True(Crafting.TryCraft(inventory, TestSet, PlateFromRock));
        Assert.Equal(["10x1", "2x1"], inventory.Layout());
    }

    [Fact]
    public void TryCraft_results_top_up_existing_stacks()
    {
        var inventory = Holding(2, S(Plate, 9), S(Rock, 2));
        Assert.True(Crafting.TryCraft(inventory, TestSet, PlateFromRock));
        Assert.Equal(["10x10", "-"], inventory.Layout());
    }

    [Fact]
    public void TryCraft_results_can_spill_over_several_slots()
    {
        var inventory = Holding(3, S(Bar, 3));
        Assert.True(Crafting.TryCraft(inventory, TestSet, PlatesFromBar, times: 3)); // 15 plates, stack 10
        Assert.Equal(["10x10", "10x5", "-"], inventory.Layout());
    }

    [Fact]
    public void TryCraft_takes_ingredients_from_the_back_so_the_hotbar_is_spent_last()
    {
        var inventory = Inventory.ForItemset(TestSet, 12);
        inventory.SetSlot(0, S(Rock, 4));
        inventory.SetSlot(11, S(Rock, 1));
        Assert.True(Crafting.TryCraft(inventory, TestSet, PlateFromRock));
        Assert.Equal(S(Rock, 3), inventory.Slots[0]);
        Assert.Equal(S(Plate, 1), inventory.Slots[1]);
        Assert.Null(inventory.Slots[11]);
    }

    [Fact]
    public void TryCraft_skips_non_positive_ingredient_quantities()
    {
        var odd = Bp("Odd", CraftingStation.CraftingTable, [S(Rock, 2), S(Ore, 0), S(Ore, -1)], S(Plate, 1));
        var inventory = Holding(3, S(Rock, 2));
        Assert.True(Crafting.TryCraft(inventory, TestSet, odd));
        Assert.Equal(["10x1", "-", "-"], inventory.Layout());
    }

    [Fact]
    public void TryCraft_of_a_blueprint_without_ingredients_only_adds()
    {
        var free = Bp("Free", CraftingStation.Extractor, [], S(Bar, 2));
        var inventory = Holding(2);
        Assert.True(Crafting.TryCraft(inventory, TestSet, free, times: 2));
        Assert.Equal(4, inventory.Count(Bar));
    }

    // --- TryCraft: failure leaves the inventory untouched ---

    static void AssertFailsUntouched(Inventory inventory, BlueprintDef blueprint, int times = 1, ItemsetDef? set = null)
    {
        var before = inventory.Layout();
        var changes = new ChangeCounter(inventory);
        Assert.False(Crafting.TryCraft(inventory, set ?? TestSet, blueprint, times));
        Assert.Equal(before, inventory.Layout());
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void TryCraft_fails_when_an_ingredient_is_short() =>
        AssertFailsUntouched(Holding(10, S(Rock, 1)), PlateFromRock);

    [Fact]
    public void TryCraft_fails_when_one_of_several_ingredients_is_missing() =>
        AssertFailsUntouched(Holding(10, S(Rock, 30)), BarFromRockAndOre);

    [Fact]
    public void TryCraft_checks_duplicate_ingredient_ids_together()
    {
        // 2 rock covers each Rock entry on its own (1 and 2) but not their sum (3).
        AssertFailsUntouched(Holding(10, S(Rock, 2), S(Ore, 1)), BarFromRockAndOre);
    }

    [Fact]
    public void TryCraft_fails_when_times_needs_more_than_is_on_hand() =>
        AssertFailsUntouched(Holding(10, S(Rock, 5)), PlateFromRock, times: 3);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryCraft_fails_for_times_below_one(int times) =>
        AssertFailsUntouched(Holding(10, S(Rock, 10)), PlateFromRock, times);

    [Fact]
    public void TryCraft_fails_and_rolls_back_when_results_do_not_fit()
    {
        // Ingredients would be consumed (rock 6 -> 4) but the slot stays occupied and the other is full of ore.
        AssertFailsUntouched(Holding(2, S(Rock, 6), S(Ore, 64)), PlateFromRock);
    }

    [Fact]
    public void TryCraft_fails_when_part_of_a_multi_slot_result_does_not_fit() =>
        AssertFailsUntouched(Holding(2, S(Bar, 3), S(Ore, 1)), PlatesFromBar, times: 3); // 15 plates need two slots

    [Fact]
    public void TryCraft_fails_when_the_result_id_is_unknown()
    {
        var broken = Bp("Broken", CraftingStation.CraftingTable, [S(Rock, 1)], S(Unknown, 1));
        AssertFailsUntouched(Holding(10, S(Rock, 5)), broken);
    }

    [Fact]
    public void TryCraft_fails_when_an_ingredient_id_is_unknown()
    {
        var broken = Bp("Broken", CraftingStation.CraftingTable, [S(Rock, 1), S(Unknown, 1)], S(Plate, 1));
        var inventory = Holding(10, S(Rock, 5));
        inventory.SetSlot(5, S(Unknown, 3)); // even when held, e.g. left over from a save
        AssertFailsUntouched(inventory, broken);
    }

    [Fact]
    public void TryCraft_fails_when_an_ingredient_is_unknown_to_the_given_itemset()
    {
        var withoutOre = Set(blueprints: []);
        var trimmed = new ItemsetDef
        {
            Path = withoutOre.Path, Header = withoutOre.Header, Items = withoutOre.Items.Where(i => i.ItemId != Ore).ToList(),
            ItemsById = withoutOre.ItemsById.Where(p => p.Key != Ore).ToDictionary(),
            ItemsByBlockId = withoutOre.ItemsByBlockId, Projectiles = withoutOre.Projectiles, Blueprints = [BarFromRockAndOre],
            DefaultItems = [], Fingerprint = "trimmed",
        };
        AssertFailsUntouched(Holding(10, S(Rock, 9), S(Ore, 9)), BarFromRockAndOre, set: trimmed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void TryCraft_fails_when_the_blueprint_has_no_result(int quantity)
    {
        var empty = Bp("Empty", CraftingStation.CraftingTable, [S(Rock, 1)], S(Plate, quantity));
        AssertFailsUntouched(Holding(10, S(Rock, 5)), empty);
    }

    [Fact]
    public void TryCraft_fails_for_a_result_missing_from_the_xml()
    {
        // The parser stores a missing <result> as (-1, 0).
        var missing = Bp("Missing", CraftingStation.CraftingTable, [S(Rock, 1)], S(-1, 0));
        AssertFailsUntouched(Holding(10, S(Rock, 5)), missing);
    }

    [Fact]
    public void TryCraft_fails_instead_of_overflowing_on_huge_times()
    {
        AssertFailsUntouched(Holding(10, S(Rock, 5)), PlateFromRock, times: int.MaxValue);
        var free = Bp("Free", CraftingStation.Extractor, [], S(Bar, 2));
        AssertFailsUntouched(Holding(10), free, times: int.MaxValue);
    }

    [Fact]
    public void TryCraft_fails_when_only_the_ingredient_total_overflows()
    {
        // 1,000,000 x 3000 = 3e9 ingredients: beyond int even though the 3000 results fit easily. Must not be
        // mistaken for "needs nothing" and hand out free bars.
        var costly = Bp("Costly", CraftingStation.Smelter, [S(Rock, 1_000_000)], S(Bar, 1));
        AssertFailsUntouched(Holding(50), costly, times: 3000);
        AssertFailsUntouched(Holding(50, S(Rock, 64)), costly, times: 3000);
    }

    [Fact]
    public void MaxCraftable_counts_beyond_int_range_without_going_negative()
    {
        var inventory = new Inventory(3, _ => int.MaxValue);
        inventory.SetSlot(0, S(Rock, int.MaxValue));
        inventory.SetSlot(1, S(Rock, int.MaxValue));
        var one = Bp("One", CraftingStation.Smelter, [S(Rock, 1)], S(Bar, 1));
        var two = Bp("Two", CraftingStation.Smelter, [S(Rock, 2)], S(Bar, 1));
        Assert.Equal(int.MaxValue, Crafting.MaxCraftable(inventory, one));
        Assert.Equal(int.MaxValue, Crafting.MaxCraftable(inventory, two)); // 2 * int.MaxValue / 2, not clamped first
        var three = Bp("Three", CraftingStation.Smelter, [S(Rock, 3)], S(Bar, 1));
        Assert.Equal((int)(2L * int.MaxValue / 3), Crafting.MaxCraftable(inventory, three));
    }

    [Fact]
    public void Failed_craft_followed_by_a_successful_one_raises_once()
    {
        var inventory = Holding(10, S(Rock, 2));
        var changes = new ChangeCounter(inventory);
        Assert.False(Crafting.TryCraft(inventory, TestSet, PlateFromRock, times: 2));
        Assert.True(Crafting.TryCraft(inventory, TestSet, PlateFromRock));
        Assert.Equal(1, changes.Count);
    }

    [Fact]
    public void Craft_that_turns_items_into_themselves_raises_nothing()
    {
        var identity = Bp("Identity", CraftingStation.CraftingTable, [S(Rock, 1)], S(Rock, 1));
        var inventory = Holding(2, S(Rock, 3));
        var changes = new ChangeCounter(inventory);
        Assert.True(Crafting.TryCraft(inventory, TestSet, identity));
        Assert.Equal(["1x3", "-"], inventory.Layout());
        Assert.Equal(0, changes.Count);
    }

    // --- the main inventory only ---

    [Fact]
    public void Crafting_from_the_main_inventory_ignores_the_hotbar()
    {
        var inventory = NewInventory(12);
        inventory.SetSlot(0, S(Rock, 10));          // hotbar
        inventory.SetSlot(Crafting.MainInventory, S(Rock, 3));
        var set = Set();

        Assert.Equal(1, Crafting.MaxCraftable(inventory, PlateFromRock, Crafting.MainInventory));
        Assert.True(Crafting.TryCraft(inventory, set, PlateFromRock, 1, Crafting.MainInventory));

        Assert.Equal(S(Rock, 10), inventory.Slots[0]);
        Assert.Equal(1, inventory.Count(Rock) - 10);
        Assert.DoesNotContain(inventory.Slots.Take(Crafting.MainInventory), s => s?.ItemId == Plate);
        Assert.Equal(1, inventory.Count(Plate));
        Assert.False(Crafting.TryCraft(inventory, set, PlateFromRock, 1, Crafting.MainInventory));
    }

    [Fact]
    public void The_result_must_fit_in_the_main_inventory()
    {
        var inventory = NewInventory(Crafting.MainInventory + 1);
        inventory.SetSlot(Crafting.MainInventory, S(Rock, 2));
        inventory.SetSlot(1, null);                 // room in the hotbar does not count

        Assert.True(Crafting.TryCraft(inventory, Set(), PlateFromRock, 1, Crafting.MainInventory));   // the rock's slot frees up
        Assert.Equal(S(Plate, 1), inventory.Slots[Crafting.MainInventory]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(7, 3)]
    [InlineData(10, 5)]
    public void Half_a_batch_is_half_of_what_can_be_afforded_and_at_least_one(int affordable, int expected) =>
        Assert.Equal(expected, Crafting.HalfBatch(affordable));
}
