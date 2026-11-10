using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Persistence;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>Saving and restoring the survival inventory and suit, whatever the mode the world is saved in.</summary>
public class InventoryRestoreTests
{
    /// <summary>Saves the player's items the way the game does, through the real file format.</summary>
    static WorldSnapshot SaveAndLoad(Inventory inventory, Suit? suit, bool creative)
        => SaveAndLoad(inventory, suit, held: null, creative);

    static WorldSnapshot SaveAndLoad(Inventory inventory, Suit? suit, ItemStack? held, bool creative = false)
    {
        var snapshot = SmallWorld() with
        {
            Inventory = InventoryRestore.SlotsToSave(inventory, suit, held),
            Suit = suit?.Save(),
            Creative = creative,
        };
        return FromBytes(ToBytes(snapshot));
    }

    [Fact]
    public void Survival_items_saved_in_creative_come_back_when_switching_to_survival()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 12);
        inventory.SetSlot(0, S(WeakDrill, 1));
        inventory.SetSlot(3, S(Rock, 30));
        inventory.SetSlot(11, S(Plate, 7));
        inventory.SetSlot(5, S(Helmet, 1));
        var suit = new Suit(set);
        Assert.True(suit.TryEquip(inventory, 5));
        suit.SetHealth(42);
        var before = inventory.Slots.ToArray();

        // The player switches to creative (mode creative) and saves: the world is now a creative one.
        var loaded = SaveAndLoad(inventory, suit, creative: true);
        Assert.True(loaded.Creative);

        // A creative load restores both, so switching back to survival finds them.
        var restoredInventory = Inventory.ForItemset(set, 12);
        var report = InventoryRestore.Restore(restoredInventory, loaded.Inventory, set);
        var restoredSuit = new Suit(set);
        var suitReport = InventoryRestore.RestoreSuit(restoredSuit, loaded.Suit!, restoredInventory);

        Assert.True(report.IsClean);
        Assert.True(suitReport.IsClean);
        Assert.Equal(before, restoredInventory.Slots);
        Assert.Equal(S(Helmet, 1), restoredSuit.Equipment[(int)WearableSlot.Helmet]);
        Assert.Equal(42, restoredSuit.Health);
    }

    [Fact]
    public void A_creative_world_that_never_had_a_survival_player_saves_no_inventory()
    {
        var inventory = NewInventory(12);

        Assert.Empty(InventoryRestore.SlotsToSave(inventory, suit: null));
        Assert.Null(SaveAndLoad(inventory, suit: null, creative: true).Suit);
    }

    [Fact]
    public void An_empty_survival_inventory_still_saves_every_slot()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 12);

        var slots = InventoryRestore.SlotsToSave(inventory, new Suit(set));

        Assert.Equal(12, slots.Count);
        Assert.All(slots, Assert.Null);
    }

    [Fact]
    public void Saved_slots_are_a_copy_that_later_changes_do_not_touch()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 4);
        inventory.SetSlot(1, S(Rock, 3));

        var slots = InventoryRestore.SlotsToSave(inventory, new Suit(set));
        inventory.Clear();

        Assert.Equal(S(Rock, 3), slots[1]);
    }

    [Fact]
    public void A_stack_held_on_the_cursor_is_saved_with_the_inventory_without_changing_it()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 4);
        inventory.SetSlot(0, S(Rock, 60));
        var hand = new ItemHand();
        var scratch = Inventory.ForItemset(set, 1);
        scratch.SetSlot(0, S(Rock, 10));
        hand.Click(scratch, 0, HandButton.Left);

        var loaded = SaveAndLoad(inventory, new Suit(set), hand.Held);

        // Topped up the way a pickup would be, and the live inventory and hand are as they were.
        Assert.Equal([S(Rock, 64), S(Rock, 6), null, null], loaded.Inventory);
        Assert.Equal([S(Rock, 60), null, null, null], inventory.Slots);
        Assert.Equal(S(Rock, 10), hand.Held);
    }

    [Fact]
    public void A_held_stack_that_does_not_fit_is_saved_past_the_end_and_restored_like_a_pickup()
    {
        var set = Set();
        var full = Inventory.ForItemset(set, 2);
        full.SetSlot(0, S(Rock, 64));
        full.SetSlot(1, S(Plate, 10));

        var slots = InventoryRestore.SlotsToSave(full, new Suit(set), S(Rock, 5));
        Assert.Equal([S(Rock, 64), S(Plate, 10), S(Rock, 5)], slots);

        // A larger inventory on the next load has room for it; the same one loses it and says so.
        var larger = Inventory.ForItemset(set, 3);
        Assert.True(InventoryRestore.Restore(larger, slots, set).IsClean);
        Assert.Equal(69, larger.CountFrom(Rock, 0));
        Assert.Equal(5, InventoryRestore.Restore(Inventory.ForItemset(set, 2), slots, set).LostItems);
    }

    [Fact]
    public void A_creative_world_holding_a_stack_saves_it()
    {
        var slots = InventoryRestore.SlotsToSave(NewInventory(4), suit: null, S(Rock, 3));

        Assert.Equal([S(Rock, 3), null, null, null], slots);
    }

    [Fact]
    public void Unknown_items_are_kept_in_their_slot_and_counted()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 4);

        var report = InventoryRestore.Restore(inventory, [null, S(Unknown, 5)], set);

        Assert.Equal(new InventoryRestoreReport(1, 0, 0), report);
        Assert.Equal(S(Unknown, 5), inventory.Slots[1]);
    }

    [Fact]
    public void An_oversized_stack_keeps_the_limit_in_its_slot_and_the_rest_moves_on()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 4);

        var report = InventoryRestore.Restore(inventory, [S(Plate, 25)], set);

        Assert.Equal(new InventoryRestoreReport(0, 1, 0), report);
        Assert.Equal([S(Plate, 10), S(Plate, 10), S(Plate, 5), null], inventory.Slots);
    }

    [Fact]
    public void Slots_beyond_the_inventory_are_added_and_what_finds_no_room_is_counted_as_lost()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 2);

        var report = InventoryRestore.Restore(inventory, [S(Rock, 60), null, S(Rock, 10), S(Helmet, 1)], set);

        // Slot 2's rock tops up slot 0 and fills slot 1; slot 3's item has nowhere to go.
        Assert.Equal(new InventoryRestoreReport(0, 0, 1), report);
        Assert.Equal([S(Rock, 64), S(Rock, 6)], inventory.Slots);
    }

    [Fact]
    public void Empty_and_non_positive_saved_slots_are_skipped()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 3);

        var report = InventoryRestore.Restore(inventory, [S(Rock, 0), null, S(Ore, -4)], set);

        Assert.True(report.IsClean);
        Assert.All(inventory.Slots, Assert.Null);
    }

    [Fact]
    public void Equipment_that_cannot_be_worn_moves_to_the_inventory_and_overflow_is_lost()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 1);
        inventory.SetSlot(0, S(Helmet, 1));
        // A battery saved in the helmet slot and a rock in the power slot: neither fits there with this itemset.
        var state = new SuitState(80, 50, [S(Battery, 1), null, null, null, null, S(Rock, 1), null], []);
        var suit = new Suit(set);

        var report = InventoryRestore.RestoreSuit(suit, state, inventory);

        Assert.Equal(new SuitRestoreReport(2, 2), report);
        Assert.All(suit.Equipment, Assert.Null);
        Assert.Equal(80, suit.Health);
    }

    [Fact]
    public void The_extra_items_of_a_worn_stack_move_to_the_inventory()
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, 2);
        var state = new SuitState(80, 50, [S(Helmet, 3), null, null, null, null, null, null], []);
        var suit = new Suit(set);

        var report = InventoryRestore.RestoreSuit(suit, state, inventory);

        // A helmet stacks to one: two slots take the two extras.
        Assert.Equal(new SuitRestoreReport(0, 0) { ExtraWornItems = 2 }, report);
        Assert.Equal(S(Helmet, 1), suit.Equipment[(int)WearableSlot.Helmet]);
        Assert.Equal([S(Helmet, 1), S(Helmet, 1)], inventory.Slots);
        Assert.Equal("2 extra item(s) of worn stacks were moved to the inventory", report.Describe("Test"));
    }

    [Fact]
    public void Reports_describe_only_what_went_wrong()
    {
        Assert.Null(new InventoryRestoreReport(0, 0, 0).Describe("Test"));
        Assert.Null(new SuitRestoreReport(0, 0).Describe("Test"));

        var inventoryText = new InventoryRestoreReport(2, 0, 7).Describe("Test")!;
        Assert.Contains("2 inventory stack(s) are items Test does not define", inventoryText);
        Assert.Contains("7 item(s) did not fit", inventoryText);
        Assert.DoesNotContain("split", inventoryText);

        Assert.Equal("1 worn items can't be worn with Test and were moved to the inventory",
            new SuitRestoreReport(1, 0).Describe("Test"));
        Assert.EndsWith("; 3 did not fit and were lost", new SuitRestoreReport(1, 3).Describe("Test"));
    }
}
