using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;

namespace OpenCorneroids.Simulation.Tests.Items;

public class SuitTests
{
    static (Suit Suit, Inventory Inventory) New(params ItemStack?[] contents)
    {
        var set = Set();
        var inventory = Inventory.ForItemset(set, Math.Max(4, contents.Length));
        for (int i = 0; i < contents.Length; i++) inventory.SetSlot(i, contents[i]);
        return (new Suit(set), inventory);
    }

    [Fact]
    public void A_new_suit_matches_the_original_character_window()
    {
        var (suit, _) = New();
        Assert.Equal((100, 100, 0, 5), (suit.MaxHealth, suit.MaxPower, suit.Armor, suit.PowerRecharge));
        Assert.Equal((100.0, 100.0), (suit.Health, suit.Power));
        Assert.Equal(7, suit.Equipment.Count);
    }

    [Fact]
    public void Wearing_adds_armor_health_and_recharge_and_frees_the_inventory_slot()
    {
        var (suit, inventory) = New(S(Helmet, 1), S(Battery, 1));

        Assert.True(suit.TryEquip(inventory, 0));
        Assert.True(suit.TryEquip(inventory, 1));

        Assert.Equal((110, 2, 8), (suit.MaxHealth, suit.Armor, suit.PowerRecharge));
        Assert.Equal(S(Helmet, 1), suit.Equipment[(int)WearableSlot.Helmet]);
        Assert.Equal(S(Battery, 1), suit.Equipment[(int)WearableSlot.PowerSource]);
        Assert.All(inventory.Slots, Assert.Null);
    }

    [Fact]
    public void Wearing_over_a_worn_piece_swaps_it_into_the_inventory()
    {
        var (suit, inventory) = New(S(Helmet, 1), null);
        suit.TryEquip(inventory, 0);
        inventory.SetSlot(1, S(Helmet, 1));

        Assert.True(suit.TryEquip(inventory, 1));

        Assert.Equal(S(Helmet, 1), inventory.Slots[1]);
        Assert.Equal(S(Helmet, 1), suit.Equipment[(int)WearableSlot.Helmet]);
    }

    [Fact]
    public void What_cant_be_worn_is_refused_without_changes()
    {
        var (suit, inventory) = New(S(Rock, 3));
        Assert.False(suit.TryEquip(inventory, 0));
        Assert.Equal(S(Rock, 3), inventory.Slots[0]);
        Assert.All(suit.Equipment, Assert.Null);
    }

    [Fact]
    public void Taking_off_needs_room_and_lowers_health_to_the_new_maximum()
    {
        var (suit, inventory) = New(S(Helmet, 1), S(Rock, 1), S(Rock, 1), S(Rock, 1));
        suit.TryEquip(inventory, 0);
        suit.Restore(suit.Save() with { Health = 110 });
        inventory.SetSlot(0, S(Bar, 1));

        Assert.False(suit.TryUnequip(WearableSlot.Helmet, inventory));
        inventory.SetSlot(0, null);
        Assert.True(suit.TryUnequip(WearableSlot.Helmet, inventory));

        Assert.Equal(S(Helmet, 1), inventory.Slots[0]);
        Assert.Equal(100.0, suit.Health);
    }

    [Fact]
    public void Power_is_spent_whole_or_not_at_all_and_recharges_over_time()
    {
        var (suit, _) = New();
        Assert.True(suit.TrySpend(70));
        Assert.False(suit.TrySpend(31));
        Assert.Equal(30.0, suit.Power);

        suit.Tick(2);
        Assert.Equal(40.0, suit.Power, 6);
        suit.Tick(100);
        Assert.Equal(100.0, suit.Power);
    }

    [Fact]
    public void A_potion_heals_but_is_kept_at_full_health()
    {
        var (suit, inventory) = New(S(Potion, 2));
        Assert.Equal("Health is already full", suit.TryConsume(inventory, 0));
        Assert.Equal(S(Potion, 2), inventory.Slots[0]);

        suit.Restore(suit.Save() with { Health = 60 });
        Assert.Null(suit.TryConsume(inventory, 0));

        Assert.Equal(85.0, suit.Health);
        Assert.Equal(S(Potion, 1), inventory.Slots[0]);
    }

    [Fact]
    public void Timed_buffs_add_while_they_last_and_restart_rather_than_stack()
    {
        var (suit, inventory) = New(S(Overcharge, 2), S(ArmorBoost, 1));
        suit.TryConsume(inventory, 0);
        suit.TryConsume(inventory, 1);
        Assert.Equal((15, 10), (suit.PowerRecharge, suit.Armor));

        suit.Tick(1.5);
        Assert.Equal((15, 0), (suit.PowerRecharge, suit.Armor));

        suit.TryConsume(inventory, 0);
        Assert.Single(suit.Buffs);
        Assert.Equal(20.0, suit.Buffs[0].SecondsLeft);
    }

    [Fact]
    public void Restore_clamps_and_returns_what_does_not_fit_its_slot()
    {
        var (suit, _) = New();
        ItemStack?[] equipment = [S(Battery, 1), null, null, null, null, null, S(Battery, 1), S(Helmet, 1)];

        var unworn = suit.Restore(new SuitState(500, -3, equipment, [new SuitBuff(ArmorBoost, 0, 10, 0)]));

        // A battery in the helmet slot, and an eighth slot: handed back, not deleted.
        Assert.Equal([S(Battery, 1), S(Helmet, 1)], unworn);
        Assert.Equal(S(Battery, 1), suit.Equipment[(int)WearableSlot.PowerSource]);
        Assert.Equal((100.0, 0.0), (suit.Health, suit.Power));
        Assert.Empty(suit.Buffs);   // an expired buff is not restored
    }

    [Fact]
    public void Changed_fires_on_whole_points_not_every_tick()
    {
        var (suit, _) = New();
        suit.TrySpend(50);
        int changes = 0;
        suit.Changed += () => changes++;

        for (int i = 0; i < 10; i++) suit.Tick(0.01);   // 0.5 power in total
        Assert.Equal(0, changes);
        suit.Tick(0.2);
        Assert.Equal(1, changes);
    }
}
