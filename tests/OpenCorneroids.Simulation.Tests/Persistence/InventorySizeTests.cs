using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Settings;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>The inventory size rule (<c>sv_inventory_slots</c>), and worlds saved before it with 40 slots.</summary>
public class InventorySizeTests
{
    static WorldSnapshot Saved(IReadOnlyList<ItemStack?> inventory, IReadOnlyDictionary<string, string>? rules = null) =>
        AllAir(2) with
        {
            Itemset = new ItemsetBinding("TestItemset", "Test", "test-pack", "test"),
            Inventory = inventory,
            Rules = rules ?? new Dictionary<string, string>(),
        };

    static ItemStack?[] Slots(int count, params (int Slot, ItemStack Stack)[] filled)
    {
        var slots = new ItemStack?[count];
        foreach (var (slot, stack) in filled) slots[slot] = stack;
        return slots;
    }

    /// <summary>Loads a save the way the game does: its rules, then the size rule and the fill order, then the items.</summary>
    static (CvarStore Cvars, Inventory Inventory, InventoryRestoreReport Report) Load(WorldSnapshot saved)
    {
        var cvars = new CvarStore();
        Assert.Empty(cvars.Import(saved.Rules, CvarFlags.World));
        var inventory = Inventory.ForItemset(Set(), InventorySize.Apply(cvars, saved));
        inventory.Order = InventorySize.FillOrderFor(saved);
        return (cvars, inventory, InventoryRestore.Restore(inventory, saved.Inventory, Set()));
    }

    [Fact]
    public void The_rule_defaults_to_the_original_thirty_main_slots()
    {
        Assert.Equal(30, Cvars.InventorySlots.Default);
        Assert.Equal(Inventory.DefaultMainSlots, (int)Cvars.InventorySlots.Default);
        Assert.True(Cvars.InventorySlots.AppliesOnLoad);
        Assert.Contains(Cvars.InventorySlots, Cvars.All);
        Assert.Contains(Cvars.InventoryFillHotbarFirst, Cvars.All);
    }

    [Fact]
    public void A_new_world_has_thirty_eight_slots()
    {
        var cvars = new CvarStore();
        Assert.Equal(38, InventorySize.SlotCountFor(null));
        Assert.Equal(38, InventorySize.Apply(cvars, null));
        Assert.True(cvars.IsDefault(Cvars.InventorySlots));
    }

    [Theory]
    [InlineData(0)]    // a creative world that never had a survival player
    [InlineData(12)]
    [InlineData(38)]
    public void A_save_no_larger_than_the_default_gets_the_default(int savedSlots)
    {
        var (cvars, inventory, _) = Load(Saved(new ItemStack?[savedSlots]));
        Assert.Equal(38, inventory.Slots.Count);
        Assert.True(cvars.IsDefault(Cvars.InventorySlots));
    }

    [Fact]
    public void A_forty_slot_save_from_before_the_rule_keeps_its_slots_and_items()
    {
        var saved = FromBytes(ToBytes(Saved(Slots(40, (0, S(WeakDrill, 1)), (37, S(Rock, 64)), (38, S(Plate, 7)), (39, S(Ore, 3))))));

        var (cvars, inventory, report) = Load(saved);

        Assert.Equal(40, InventorySize.SlotCountFor(saved));
        Assert.Equal(40, inventory.Slots.Count);
        Assert.True(report.IsClean);
        Assert.Equal(saved.Inventory, inventory.Slots);
        Assert.Equal(S(Plate, 7), inventory.Slots[38]);
        Assert.Equal(S(Ore, 3), inventory.Slots[39]);
        // The size became this world's own rule, so it is saved with the world and stays on the next load.
        Assert.Equal(32, cvars.Int(Cvars.InventorySlots));
        Assert.Equal("32", cvars.Export(CvarFlags.World)[Cvars.InventorySlots.Name]);
    }

    [Fact]
    public void An_old_world_saved_again_keeps_its_size()
    {
        var (cvars, inventory, _) = Load(Saved(Slots(40, (39, S(Ore, 3)))));
        var resaved = FromBytes(ToBytes(Saved(InventoryRestore.SlotsToSave(inventory, new Suit(Set())),
            InventorySize.RulesToSave(cvars.Export(CvarFlags.World), cvars.Int(Cvars.InventorySlots), inventory.Slots.Count))));

        var (_, reloaded, report) = Load(resaved);

        Assert.Equal(40, reloaded.Slots.Count);
        Assert.True(report.IsClean);
        Assert.Equal(S(Ore, 3), reloaded.Slots[39]);
    }

    [Fact]
    public void Setting_an_old_world_back_to_the_default_shrinks_it_on_the_next_load()
    {
        var (cvars, inventory, _) = Load(Saved(Slots(40, (39, S(Ore, 3)))));
        cvars.Reset(Cvars.InventorySlots);   // back to the original 30; the open inventory keeps its 40 slots

        // Left out of the rules, the default would be read as a world from before the rule and keep 40 slots.
        var rules = InventorySize.RulesToSave(cvars.Export(CvarFlags.World), cvars.Int(Cvars.InventorySlots), inventory.Slots.Count);
        Assert.Equal("30", rules[Cvars.InventorySlots.Name]);

        var (_, reloaded, report) = Load(FromBytes(ToBytes(Saved(inventory.Slots.ToArray(), rules))));

        Assert.Equal(38, reloaded.Slots.Count);
        Assert.Equal(0, report.LostItems);
        Assert.Equal(3, reloaded.Count(Ore));   // moved from slot 39 to a free slot
    }

    [Fact]
    public void The_default_is_not_written_when_the_inventory_already_has_the_default_size()
    {
        var rules = new Dictionary<string, string> { ["sv_ricochet"] = "1" };
        Assert.Same(rules, InventorySize.RulesToSave(rules, 30, 38));
        Assert.Same(rules, InventorySize.RulesToSave(rules, 30, 0));
    }

    [Theory]
    [InlineData("48", 56)]
    [InlineData("6", 14)]
    [InlineData("30", 38)]
    public void A_saved_rule_wins_over_the_saved_slot_count(string rule, int expectedSlots)
    {
        var saved = Saved(new ItemStack?[40], new Dictionary<string, string> { ["sv_inventory_slots"] = rule });
        Assert.Equal(expectedSlots, InventorySize.SlotCountFor(saved));
        Assert.Equal(expectedSlots, Load(saved).Inventory.Slots.Count);
    }

    [Theory]
    [InlineData("1000")]
    [InlineData("lots")]
    [InlineData("2.5")]
    public void An_invalid_saved_rule_is_ignored_as_on_import(string rule)
    {
        var rules = new Dictionary<string, string> { ["sv_inventory_slots"] = rule };
        Assert.Equal(40, InventorySize.SlotCountFor(Saved(new ItemStack?[40], rules)));
        Assert.Equal(38, InventorySize.SlotCountFor(Saved(new ItemStack?[38], rules)));
    }

    [Fact]
    public void A_huge_saved_inventory_is_held_to_the_rules_maximum()
    {
        Assert.Equal(96, InventorySize.LegacyMainSlots(5000));
        Assert.Equal(Inventory.HotbarSize + 96, InventorySize.SlotCountFor(Saved(new ItemStack?[5000])));
        Assert.Null(InventorySize.LegacyMainSlots(38));
        Assert.Equal(31, InventorySize.LegacyMainSlots(39));
    }

    [Fact]
    public void The_item_check_before_loading_sees_the_same_size_as_the_load()
    {
        var saved = Saved(Slots(40, (38, S(Plate, 7)), (39, S(Ore, 3))));

        var problems = ItemCheck.Of(saved, Set(), InventorySize.SlotCountFor(saved));

        Assert.True(problems.IsClean);
        Assert.Equal(0, problems.StacksBeyondCapacity);
    }

    [Theory]
    [InlineData(null, FillOrder.SlotOrder)]
    [InlineData("1", FillOrder.SlotOrder)]
    [InlineData("0", FillOrder.TopUpFirst)]
    [InlineData("off", FillOrder.TopUpFirst)]
    [InlineData("maybe", FillOrder.SlotOrder)]   // invalid: ignored on import too, so the default
    public void The_fill_order_comes_from_the_saved_rule_as_the_load_imports_it(string? rule, FillOrder expected)
    {
        var rules = rule is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["SV_Inventory_Fill_Hotbar_First"] = rule };
        var cvars = new CvarStore();
        cvars.Import(rules, CvarFlags.World);

        Assert.Equal(expected, InventorySize.FillOrderFor(Saved([], rules)));
        Assert.Equal(expected, InventorySize.FillOrderOf(cvars.Bool(Cvars.InventoryFillHotbarFirst)));
        Assert.Equal(FillOrder.SlotOrder, InventorySize.FillOrderFor(null));
    }

    [Theory]
    [InlineData(null, 1)]   // slot order: Rock goes to the empty slot 0, so Ore finds no room
    [InlineData("0", 0)]    // ours: Rock tops up slot 13 and Ore takes slot 0
    public void The_item_check_before_loading_uses_the_same_fill_order_as_the_load(string? fillRule, long expectedLost)
    {
        // 14 slots (the rule says 6 main slots): slot 0 free, 1..12 full, 13 has room for 4 Rock. Two stacks past the end.
        var slots = Slots(16, (13, S(Rock, 60)), (14, S(Rock, 4)), (15, S(Ore, 1)));
        for (int i = 1; i <= 12; i++) slots[i] = S(WeakDrill, 1);
        var rules = new Dictionary<string, string> { ["sv_inventory_slots"] = "6" };
        if (fillRule is not null) rules["sv_inventory_fill_hotbar_first"] = fillRule;
        var saved = Saved(slots, rules);

        var problems = ItemCheck.Of(saved, Set(), InventorySize.SlotCountFor(saved), InventorySize.FillOrderFor(saved));
        var (_, _, report) = Load(saved);

        Assert.Equal(expectedLost, report.LostItems);
        Assert.Equal(report.LostItems, problems.LostItems);
    }
}
