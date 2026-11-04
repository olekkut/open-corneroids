using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class StructureTests
{
    const int Hull = 1, Console = 2, Rock = 3, Mineral = 4, Door = 5;

    static bool ShipBlock(int id) => id is not (Rock or Mineral);

    sealed class Grid
    {
        public readonly Dictionary<Int3, int> Blocks = new();
        public Grid Put(int id, params Int3[] cells)
        {
            foreach (var c in cells) Blocks[c] = id;
            return this;
        }
        public int? At(Int3 c) => Blocks.TryGetValue(c, out var id) ? id : null;
    }

    static Int3 C(int x, int y = 0, int z = 0) => new(x, y, z);

    [Fact]
    public void Collects_the_ship_and_stops_at_rock()
    {
        var grid = new Grid()
            .Put(Console, C(0))
            .Put(Hull, C(1), C(2), C(0, 1), C(0, 0, -1))
            .Put(Rock, C(3), C(-1))
            .Put(Hull, C(4), C(-2));   // beyond the rock on both sides
        var scan = Structure.Collect(C(0), grid.At, ShipBlock);
        Assert.False(scan.Truncated);
        Assert.Equal(C(0), scan.Cells[0]);
        Assert.Equal(new HashSet<Int3> { C(0), C(1), C(2), C(0, 1), C(0, 0, -1) }, scan.Cells.ToHashSet());
        Assert.True(scan.Contains(C(2)));
        Assert.False(scan.Contains(C(3)));
        Assert.False(scan.Contains(C(4)));
    }

    [Fact]
    public void Minerals_also_stop_it_and_diagonals_do_not_connect()
    {
        var grid = new Grid().Put(Console, C(0)).Put(Mineral, C(1)).Put(Hull, C(2)).Put(Hull, C(1, 1)).Put(Hull, C(1, 1, 1));
        var scan = Structure.Collect(C(0), grid.At, ShipBlock);
        Assert.Equal([C(0)], scan.Cells);
    }

    [Fact]
    public void Multi_cell_objects_join_through_any_cell()
    {
        // A 2×4×1 door touching the hull only at its top-right cell; everything past it comes along.
        var door = (from x in new[] { 5, 6 } from y in Enumerable.Range(0, 4) select C(x, y)).ToArray();
        var grid = new Grid().Put(Console, C(0, 3)).Put(Hull, C(1, 3), C(2, 3), C(3, 3), C(4, 3)).Put(Door, door).Put(Hull, C(7, 0));
        var scan = Structure.Collect(C(0, 3), grid.At, ShipBlock);
        Assert.All(door, c => Assert.True(scan.Contains(c)));
        Assert.True(scan.Contains(C(7, 0)));
        Assert.Equal(5 + 8 + 1, scan.Cells.Count);
    }

    [Fact]
    public void Rejected_or_empty_start_collects_nothing()
    {
        var grid = new Grid().Put(Rock, C(0)).Put(Hull, C(1));
        Assert.Empty(Structure.Collect(C(0), grid.At, ShipBlock).Cells);
        Assert.Empty(Structure.Collect(C(5), grid.At, ShipBlock).Cells);
    }

    [Fact]
    public void Limit_truncates_and_says_so()
    {
        var grid = new Grid().Put(Hull, Enumerable.Range(0, 10).Select(x => C(x)).ToArray());
        var cut = Structure.Collect(C(0), grid.At, ShipBlock, maxCells: 4);
        Assert.True(cut.Truncated);
        Assert.Equal([C(0), C(1), C(2), C(3)], cut.Cells);

        var exact = Structure.Collect(C(0), grid.At, ShipBlock, maxCells: 10);
        Assert.False(exact.Truncated);
        Assert.Equal(10, exact.Cells.Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => Structure.Collect(C(0), grid.At, ShipBlock, maxCells: 0));
    }

    [Fact]
    public void Solid_cube_is_collected_once_per_cell_and_in_a_stable_order()
    {
        var cells = (from x in Enumerable.Range(0, 5) from y in Enumerable.Range(0, 5) from z in Enumerable.Range(0, 5) select C(x, y, z)).ToArray();
        var grid = new Grid().Put(Hull, cells);
        var a = Structure.Collect(C(2, 2, 2), grid.At, ShipBlock);
        var b = Structure.Collect(C(2, 2, 2), grid.At, ShipBlock);
        Assert.Equal(125, a.Cells.Count);
        Assert.Equal(125, a.Cells.Distinct().Count());
        Assert.Equal(a.Cells, b.Cells);
    }
}
