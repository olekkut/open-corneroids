using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;

namespace OpenCorneroids.Simulation.Tests.Items;

public class MiningTests
{
    static readonly ItemsetDef TestSet = Set();
    static BlockDef BlockOf(int itemId) => TestSet.ItemsById[itemId].Block!;

    // --- DrillPower ---

    [Fact]
    public void DrillPower_is_zero_without_a_drill()
    {
        var inventory = NewInventory();
        Assert.Equal(0, Mining.DrillPower(inventory, TestSet));
        inventory.Add(Rock, 10);
        inventory.Add(Plate, 3);
        Assert.Equal(0, Mining.DrillPower(inventory, TestSet));
    }

    [Fact]
    public void DrillPower_is_the_strongest_drill_anywhere_in_the_inventory()
    {
        var inventory = NewInventory();
        inventory.SetSlot(0, S(WeakDrill, 1));
        Assert.Equal(2, Mining.DrillPower(inventory, TestSet));
        inventory.SetSlot(35, S(StrongDrill, 1)); // outside the hotbar still counts
        Assert.Equal(5, Mining.DrillPower(inventory, TestSet));
        inventory.SetSlot(35, null);
        Assert.Equal(2, Mining.DrillPower(inventory, TestSet));
    }

    [Fact]
    public void DrillPower_ignores_ids_the_itemset_does_not_know()
    {
        var inventory = NewInventory();
        inventory.SetSlot(0, S(Unknown, 1));
        Assert.Equal(0, Mining.DrillPower(inventory, TestSet));
    }

    [Fact]
    public void DrillPower_never_goes_below_zero()
    {
        ItemDef[] items = [Item(30, "drill", 1, components: [new DrillComponent(-4)])];
        var set = Set();
        var odd = new ItemsetDef
        {
            Path = set.Path, Header = set.Header, Items = items, ItemsById = items.ToDictionary(i => i.ItemId),
            ItemsByBlockId = set.ItemsByBlockId, Projectiles = set.Projectiles, Blueprints = [], DefaultItems = [],
            Fingerprint = "odd",
        };
        var inventory = Inventory.ForItemset(odd, 2);
        inventory.Add(30, 1);
        Assert.Equal(0, Mining.DrillPower(inventory, odd));
    }

    // --- CanMine ---

    [Theory]
    [InlineData(0, Rock, false)]  // powerToDrill 1
    [InlineData(1, Rock, true)]
    [InlineData(2, Ore, false)]   // powerToDrill 3
    [InlineData(3, Ore, true)]
    [InlineData(5, Ore, true)]
    [InlineData(0, Glass, false)] // powerToDrill 0 still needs a drill
    [InlineData(-1, Glass, false)]
    [InlineData(1, Glass, true)]
    public void CanMine_needs_a_drill_at_least_as_strong_as_the_block(int power, int itemId, bool expected) =>
        Assert.Equal(expected, Mining.CanMine(power, BlockOf(itemId)));

    // --- SecondsToMine ---

    // The original's rules: 0.8 s, less 0.1 s per point of power over powerToDrill, never under 0.3 s.
    [Theory]
    [InlineData(1, Rock, 0.8)]   // powerToDrill 1: nothing to spare
    [InlineData(2, Rock, 0.7)]
    [InlineData(5, Rock, 0.4)]
    [InlineData(3, Ore, 0.8)]    // powerToDrill 3
    [InlineData(5, Ore, 0.6)]
    [InlineData(1, Glass, 0.7)]  // powerToDrill 0
    [InlineData(9, Rock, 0.3)]   // 8 to spare: floored
    public void SecondsToMine_falls_with_power_to_spare(int power, int itemId, double expected) =>
        Assert.Equal(expected, Mining.SecondsToMine(power, BlockOf(itemId)), precision: 10);

    [Theory]
    [InlineData(0, Rock)]
    [InlineData(2, Ore)]
    [InlineData(0, Glass)]
    public void SecondsToMine_is_infinite_when_the_block_cannot_be_mined(int power, int itemId) =>
        Assert.Equal(double.PositiveInfinity, Mining.SecondsToMine(power, BlockOf(itemId)));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(255)]
    public void A_blocks_health_plays_no_part(int health) =>
        Assert.Equal(Mining.MaxSecondsToMine - 0.1, Mining.SecondsToMine(1, Block(99, health, powerToDrill: 0)), precision: 10);

    [Fact]
    public void SecondsToMine_stays_between_the_limits()
    {
        Assert.Equal((0.3, 0.8), (Mining.MinSecondsToMine, Mining.MaxSecondsToMine));
        Assert.Equal(Mining.MinSecondsToMine, Mining.SecondsToMine(int.MaxValue, BlockOf(Ore)));
    }

    [Fact]
    public void Stronger_drill_is_never_slower()
    {
        var ore = BlockOf(Ore);
        for (var power = 3; power < 50; power++)
            Assert.True(Mining.SecondsToMine(power + 1, ore) <= Mining.SecondsToMine(power, ore));
    }

    // --- Drop ---

    [Fact]
    public void Drop_is_one_of_the_mined_blocks_own_item()
    {
        Assert.Equal(S(Rock, 1), Mining.Drop(TestSet.ItemsById[Rock]));
        Assert.Equal(S(Ore, 1), Mining.Drop(TestSet.ItemsByBlockId[Ore]));
    }

    [Fact]
    public void Mining_a_block_and_picking_up_the_drop_adds_it_to_the_inventory()
    {
        var inventory = NewInventory();
        inventory.Add(StrongDrill, 1);
        var ore = TestSet.ItemsById[Ore];
        Assert.True(Mining.CanMine(Mining.DrillPower(inventory, TestSet), ore.Block!));
        var drop = Mining.Drop(ore);
        Assert.Equal(0, inventory.Add(drop.ItemId, drop.Quantity));
        Assert.Equal(1, inventory.Count(Ore));
    }
}
