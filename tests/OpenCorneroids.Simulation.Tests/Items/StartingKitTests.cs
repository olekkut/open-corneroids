using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Tests.Items.Support;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;

namespace OpenCorneroids.Simulation.Tests.Items;

/// <summary>The original's rules, with the extractor given as an item (see <see cref="StartingKit"/>).</summary>
public class StartingKitTests
{
    [Fact]
    public void The_first_drill_the_table_and_the_furnace_are_endless_and_not_given()
    {
        var set = Set(defaultItems: [Table, WeakDrill, Furnace, Rock, Extractor, Plate]);
        var inventory = Inventory.ForItemset(set, 4);

        Assert.Equal([WeakDrill, Table, Furnace], StartingKit.Endless(set));
        Assert.Equal(0, StartingKit.Give(inventory, set));
        Assert.Equal(["1x1", "52x1", "10x1", "-"], inventory.Layout());
    }

    [Fact]
    public void Later_drills_are_ignored_and_the_last_station_of_a_kind_wins()
    {
        var set = Set(defaultItems: [StrongDrill, WeakDrill, Table, Table, Rock]);
        var inventory = Inventory.ForItemset(set, 3);

        Assert.Equal([StrongDrill, Table], StartingKit.Endless(set));
        StartingKit.Give(inventory, set);
        Assert.Equal(["1x1", "-", "-"], inventory.Layout());
    }

    [Fact]
    public void Give_goes_into_the_slots_from_the_first_one_asked_for()
    {
        var set = Set(defaultItems: [Rock, Plate]);
        var inventory = Inventory.ForItemset(set, 4);
        StartingKit.Give(inventory, set, firstSlot: 2);
        Assert.Equal(["-", "-", "1x1", "10x1"], inventory.Layout());
    }

    [Fact]
    public void Give_skips_ids_the_itemset_does_not_define()
    {
        var set = Set(defaultItems: [Unknown, Rock, -1]);
        var inventory = Inventory.ForItemset(set, 3);
        Assert.Equal(0, StartingKit.Give(inventory, set));
        Assert.Equal(["1x1", "-", "-"], inventory.Layout());
        Assert.Empty(StartingKit.Endless(set));
    }

    [Fact]
    public void Give_treats_repeated_entries_as_separate_items()
    {
        var set = Set(defaultItems: [Rock, Plate, Rock]);
        var inventory = Inventory.ForItemset(set, 4);
        StartingKit.Give(inventory, set);
        Assert.Equal(["1x2", "10x1", "-", "-"], inventory.Layout());
    }

    [Fact]
    public void Give_with_an_empty_list_changes_nothing()
    {
        var set = Set(defaultItems: []);
        var inventory = Inventory.ForItemset(set, 2);
        var changes = new ChangeCounter(inventory);
        Assert.Equal(0, StartingKit.Give(inventory, set));
        Assert.Equal(0, changes.Count);
    }

    [Fact]
    public void Give_reports_items_that_did_not_fit()
    {
        var set = Set(defaultItems: [Plate, Bar, Rock]);
        var inventory = Inventory.ForItemset(set, 1);
        Assert.Equal(2, StartingKit.Give(inventory, set));
    }
}
