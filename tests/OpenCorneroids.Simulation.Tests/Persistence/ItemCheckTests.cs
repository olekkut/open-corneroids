using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Persistence;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>Checking a save's inventory, suit and buffs against the itemset before loading it.</summary>
public class ItemCheckTests
{
    /// <summary>An empty 2³ world bound to the test itemset, with these player items.</summary>
    static WorldSnapshot Saved(IReadOnlyList<ItemStack?> inventory, SuitState? suit = null) =>
        AllAir(2) with
        {
            Itemset = new ItemsetBinding("TestItemset", "Test", "test-pack", "test"),
            Inventory = inventory,
            Suit = suit,
        };

    static SuitState Suit(IReadOnlyList<ItemStack?> equipment, params SuitBuff[] buffs) => new(80, 50, equipment, buffs);

    /// <summary>The test itemset with edited content: same key and items, another fingerprint.</summary>
    static ItemsetDef Edited()
    {
        var set = Set();
        return new ItemsetDef
        {
            Path = set.Path, Header = set.Header, Items = set.Items, ItemsById = set.ItemsById,
            ItemsByBlockId = set.ItemsByBlockId, Projectiles = set.Projectiles, Blueprints = set.Blueprints,
            DefaultItems = set.DefaultItems, Fingerprint = "edited",
        };
    }

    [Fact]
    public void Items_the_itemset_still_defines_are_clean()
    {
        var saved = Saved([S(Rock, 64), null, S(Helmet, 1)],
            Suit([null, null, null, null, null, null, S(Battery, 1)], new SuitBuff(Overcharge, 5, 0, 10)));

        var problems = ItemCheck.Of(saved, Set(), inventorySlots: 4);

        Assert.True(problems.IsClean);
        Assert.Equal(ItemProblems.None, problems);
        Assert.Null(problems.Describe("Test"));
    }

    [Fact]
    public void Unknown_and_oversized_inventory_stacks_are_counted()
    {
        var saved = Saved([S(Unknown, 5), S(Plate, 25), null, S(Unknown, 1), null, null]);

        var problems = ItemCheck.Of(saved, Set(), inventorySlots: 6);

        Assert.Equal(ItemProblems.None with { UnknownInventoryStacks = 2, OversizedStacks = 1 }, problems);
    }

    [Fact]
    public void Stacks_past_the_end_of_the_inventory_are_counted_and_what_finds_no_room_is_lost()
    {
        // Two slots now; slots 2 and 3 move into slot 1, and slot 4's items have nowhere to go.
        var saved = Saved([S(Rock, 60), null, S(Rock, 10), S(Helmet, 1), S(Plate, 7)]);

        var problems = ItemCheck.Of(saved, Set(), inventorySlots: 2);

        Assert.Equal(3, problems.StacksBeyondCapacity);
        Assert.Equal(8, problems.LostItems);
        Assert.Contains("8 item(s) will not fit the inventory and will be lost", problems.Describe("Test"));
    }

    [Fact]
    public void The_counts_are_the_ones_a_load_then_reports()
    {
        var set = Set();
        var saved = Saved([S(Rock, 64), S(Plate, 30), S(Unknown, 3), S(Ore, 64)],
            Suit([S(Battery, 1), S(Unknown, 1), null, null, null, null, null, S(Helmet, 1)]));

        var problems = ItemCheck.Of(saved, set, inventorySlots: 4);

        var inventory = Inventory.ForItemset(set, 4);
        var restored = InventoryRestore.Restore(inventory, saved.Inventory, set);
        var suit = InventoryRestore.RestoreSuit(new Suit(set), saved.Suit!, inventory);
        Assert.Equal(restored.UnknownStacks, problems.UnknownInventoryStacks);
        Assert.Equal(restored.SplitStacks, problems.OversizedStacks);
        Assert.Equal(restored.LostItems + suit.LostItems, problems.LostItems);
        Assert.True(problems.LostItems > 0);
    }

    [Fact]
    public void Equipment_is_sorted_into_unknown_unwearable_and_extra_items_of_worn_stacks()
    {
        // Slot 0 (helmet): an unknown item. Slot 5 (feet): a rock, which isn't wearable. Slot 1: a battery,
        // wearable but not there. An eighth slot: a helmet. The helmet in its own slot is a stack of 3.
        var equipment = new ItemStack?[] { S(Unknown, 1), S(Battery, 1), null, null, null, S(Rock, 1), null, S(Helmet, 1) };
        var saved = Saved(new ItemStack?[10], Suit(equipment));
        var problems = ItemCheck.Of(saved, Set(), inventorySlots: 10);
        Assert.Equal(ItemProblems.None with { UnknownEquipment = 1, UnwearableEquipment = 3 }, problems);

        var stacked = Saved(new ItemStack?[10], Suit([S(Helmet, 3), null, null, null, null, null, null]));
        Assert.Equal(ItemProblems.None with { ExtraWornItems = 2 }, ItemCheck.Of(stacked, Set(), inventorySlots: 10));
    }

    [Fact]
    public void Running_buffs_from_items_that_are_not_timed_consumables_are_counted()
    {
        var suit = Suit(new ItemStack?[7],
            new SuitBuff(Overcharge, 5, 0, 10),   // still a timed consumable
            new SuitBuff(Potion, 5, 0, 10),       // an instant one now
            new SuitBuff(Unknown, 5, 3, 0),       // gone
            new SuitBuff(Unknown, 0, 3, 0));      // expired: not restored, so not counted

        var problems = ItemCheck.Of(Saved([], suit), Set());

        Assert.Equal(ItemProblems.None with { UnknownBuffs = 2 }, problems);
    }

    [Fact]
    public void A_world_without_a_suit_checks_only_the_inventory()
    {
        Assert.True(ItemCheck.Of(Saved([S(Rock, 3)]), Set()).IsClean);
    }

    [Fact]
    public void The_binding_check_carries_the_item_problems_without_changing_the_status()
    {
        var saved = Saved([S(Unknown, 5)]);

        var result = SaveBinding.Check(saved, Edited(), "test-pack");

        Assert.Equal(BindingStatus.ContentChanged, result.Status);
        Assert.Equal(1, result.ItemProblems.UnknownInventoryStacks);
        Assert.True(result.AsksAboutItems);
    }

    [Fact]
    public void Changed_content_with_matching_items_does_not_ask()
    {
        var result = SaveBinding.Check(Saved([S(Rock, 5), S(Helmet, 1)]), Edited(), "test-pack");

        Assert.Equal(BindingStatus.ContentChanged, result.Status);
        Assert.True(result.ItemProblems.IsClean);
        Assert.False(result.AsksAboutItems);
    }

    [Fact]
    public void The_exact_itemset_asks_only_when_items_would_be_lost()
    {
        // Unknown items kept from an earlier load were already unknown when this world was saved: no question again.
        var kept = SaveBinding.Check(Saved([S(Unknown, 5)]), Set(), "test-pack");
        Assert.Equal(BindingStatus.Exact, kept.Status);
        Assert.Equal(1, kept.ItemProblems.UnknownInventoryStacks);
        Assert.False(kept.AsksAboutItems);

        // A smaller inventory than the save's loses items even with the same content.
        var full = Enumerable.Repeat<ItemStack?>(S(Rock, 64), 3).ToArray();
        var losing = SaveBinding.Check(Saved(full), Set(), "test-pack", inventorySlots: 2);
        Assert.Equal(BindingStatus.Exact, losing.Status);
        Assert.Equal(64, losing.ItemProblems.LostItems);
        Assert.True(losing.AsksAboutItems);
    }

    [Fact]
    public void A_different_itemset_is_refused_before_items_are_looked_at()
    {
        var saved = Saved([S(Unknown, 5)]) with { Itemset = new ItemsetBinding("OtherItemset", "Other", "test-pack", "x") };

        var result = SaveBinding.Check(saved, Set(), "test-pack");

        Assert.Equal(BindingStatus.DifferentItemset, result.Status);
        Assert.Equal(ItemProblems.None, result.ItemProblems);
        Assert.False(result.AsksAboutItems);
    }

    [Fact]
    public void The_description_gives_the_real_counts()
    {
        var text = new ItemProblems(2, 1, 0, 1, 2, 4, 1, 7).Describe("Test")!;

        Assert.Contains("2 inventory stack(s) are items Test does not define", text);
        Assert.Contains("1 inventory stack(s) are above Test's stack size", text);
        Assert.Contains("3 worn item(s) can't be worn with Test (1 unknown, 2 not for that slot)", text);
        Assert.Contains("4 extra item(s) of worn stacks", text);
        Assert.Contains("1 running effect(s)", text);
        Assert.Contains("7 item(s) will not fit", text);
        Assert.DoesNotContain("past the end", text);
    }
}
