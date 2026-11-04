using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class LaunchTests
{
    static ItemDef Item(params IItemComponent[] components) => new(
        1, "block", "blocks", "Block", "", new Rgb8(1, 2, 3), 64, 1, 0, new CellRect(0, 0, 1, 1), null, components,
        new Dictionary<string, string>(), null);

    [Fact]
    public void Rock_and_minerals_never_belong_to_a_ship()
    {
        Assert.True(Launch.BelongsToShip(Item()));
        Assert.True(Launch.BelongsToShip(Item(new ConsoleComponent(8))));
        Assert.True(Launch.BelongsToShip(Item(new ThrusterComponent(20, 0, default))));
        Assert.False(Launch.BelongsToShip(Item(new AsteroidComponent(AsteroidType.Light))));
        Assert.False(Launch.BelongsToShip(Item(new MineralComponent(AsteroidType.Heavy, 3))));
    }

    [Fact]
    public void A_lone_console_or_nothing_is_refused()
    {
        Assert.Contains("not attached", Launch.Problem(1, Int3.Zero, Int3.Zero, truncated: false));
        Assert.Contains("no console", Launch.Problem(0, Int3.Zero, Int3.Zero, truncated: false));
        Assert.Null(Launch.Problem(2, Int3.Zero, Int3.UnitX, truncated: false));
    }

    [Fact]
    public void Too_big_structures_are_refused_with_their_size()
    {
        Assert.Contains("too big", Launch.Problem(5, Int3.Zero, Int3.One, truncated: true));
        var edge = Launch.MaxExtent - 1;
        Assert.Null(Launch.Problem(5, new Int3(-3, 0, 0), new Int3(edge - 3, edge, edge), truncated: false));
        var problem = Launch.Problem(5, Int3.Zero, new Int3(0, Launch.MaxExtent, 2), truncated: false);
        Assert.NotNull(problem);
        Assert.Contains($"1×{Launch.MaxExtent + 1}×3", problem);
    }

    [Fact]
    public void Launching_stops_where_a_save_could_no_longer_hold_the_ships()
    {
        long grid = (long)Launch.ShipGridSize * Launch.ShipGridSize * Launch.ShipGridSize;
        Assert.Null(Launch.FleetProblem(0, 0));
        Assert.Null(Launch.FleetProblem(WorldSaveFormat.MaxShips - 1, (WorldSaveFormat.MaxShips - 1) * grid));
        Assert.Contains($"{WorldSaveFormat.MaxShips} ships", Launch.FleetProblem(WorldSaveFormat.MaxShips, WorldSaveFormat.MaxShips * grid));
        // Fewer ships, but big loaded ones already use the cell budget.
        Assert.Contains("fill the space", Launch.FleetProblem(8, WorldSaveFormat.MaxTotalShipCells));
        // Every launch the count cap allows also fits the cell cap.
        Assert.True(WorldSaveFormat.MaxShips * grid <= WorldSaveFormat.MaxTotalShipCells);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(4, 1, 6)]
    [InlineData(47, 47, 47)]
    public void Bounds_are_centred_in_the_ship_grid(int ex, int ey, int ez)
    {
        var min = new Int3(100, -7, 3);
        var max = min + new Int3(ex, ey, ez);
        var local = Launch.LocalMin(min, max);
        var localMax = local + (max - min);
        foreach (int axis in new[] { 0, 1, 2 })
        {
            int before = local[axis], after = Launch.ShipGridSize - 1 - localMax[axis];
            Assert.InRange(before, 0, Launch.ShipGridSize);
            // Centred: the free space on both sides differs by at most the one leftover cell, which goes to the + side.
            Assert.InRange(after - before, 0, 1);
        }
    }

    [Fact]
    public void Bounds_that_do_not_fit_the_grid_throw()
    {
        Assert.Throws<ArgumentException>(() => Launch.LocalMin(Int3.Zero, new Int3(Launch.ShipGridSize, 0, 0)));
        Assert.Throws<ArgumentException>(() => Launch.LocalMin(Int3.One, Int3.Zero));
        Assert.Equal(Int3.Zero, Launch.LocalMin(new Int3(5, 5, 5), new Int3(8, 8, 8), gridSize: 4));
    }

    [Fact]
    public void Ship_grid_leaves_room_to_build_and_matches_the_chunk_size()
    {
        Assert.True(Launch.MaxExtent < Launch.ShipGridSize);
        Assert.Equal(0, Launch.ShipGridSize % 16);
    }
}
