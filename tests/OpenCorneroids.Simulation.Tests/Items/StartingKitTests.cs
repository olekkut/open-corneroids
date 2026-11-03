using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Tests.Items.Support;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;

namespace OpenCorneroids.Simulation.Tests.Items;

public class StartingKitTests
{
    [Fact]
    public void Give_adds_one_of_each_default_item_in_list_order()
    {
        var set = Set(defaultItems: [WeakDrill, Rock, Plate]);
        var inventory = Inventory.ForItemset(set, 4);
        Assert.Equal(0, StartingKit.Give(inventory, set));
        Assert.Equal(["20x1", "1x1", "10x1", "-"], inventory.Layout());
    }

    [Fact]
    public void Give_skips_ids_the_itemset_does_not_define()
    {
        var set = Set(defaultItems: [Unknown, Rock, -1]);
        var inventory = Inventory.ForItemset(set, 3);
        Assert.Equal(0, StartingKit.Give(inventory, set));
        Assert.Equal(["1x1", "-", "-"], inventory.Layout());
    }

    [Fact]
    public void Give_treats_repeated_entries_as_separate_items()
    {
        var set = Set(defaultItems: [Rock, WeakDrill, Rock, WeakDrill]);
        var inventory = Inventory.ForItemset(set, 4);
        StartingKit.Give(inventory, set);
        Assert.Equal(["1x2", "20x1", "20x1", "-"], inventory.Layout());
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
    public void Give_adds_to_what_the_inventory_already_holds()
    {
        var set = Set(defaultItems: [Rock]);
        var inventory = Inventory.ForItemset(set, 2);
        inventory.Add(Rock, 5);
        StartingKit.Give(inventory, set);
        Assert.Equal(["1x6", "-"], inventory.Layout());
    }

    [Fact]
    public void Give_reports_items_that_did_not_fit()
    {
        var set = Set(defaultItems: [WeakDrill, StrongDrill, Rock]);
        var inventory = Inventory.ForItemset(set, 1);
        Assert.Equal(2, StartingKit.Give(inventory, set));
        Assert.Equal(["20x1"], inventory.Layout());
    }
}
