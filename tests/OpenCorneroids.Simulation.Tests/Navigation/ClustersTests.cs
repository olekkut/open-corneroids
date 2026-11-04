using System.Numerics;
using OpenCorneroids.Simulation.Navigation;

namespace OpenCorneroids.Simulation.Tests.Navigation;

public class ClustersTests
{
    const int Size = 8;

    static ushort[] Grid(params (int X, int Y, int Z, ushort Value)[] cells)
    {
        var grid = new ushort[Size * Size * Size];
        foreach (var (x, y, z, v) in cells) grid[(z * Size + y) * Size + x] = v;
        return grid;
    }

    [Fact]
    public void Face_connected_cells_form_one_cluster_and_diagonals_do_not_join()
    {
        var grid = Grid((0, 0, 0, 1), (1, 0, 0, 1), (2, 1, 0, 2), (7, 7, 7, 1));

        var clusters = Clusters.Find(grid, Size, v => v == 2 ? 10 : 1);

        Assert.Equal(3, clusters.Count);
        Assert.Equal((2, 2.0), (clusters[0].Cells, clusters[0].Mass));
        Assert.Equal(new Vector3(1, 0.5f, 0.5f), clusters[0].Center);
        Assert.Equal(1f, clusters[0].Radius, 5);
        Assert.Contains(clusters, c => c.Mass == 10);
    }

    [Fact]
    public void Small_clusters_are_left_out_and_the_largest_comes_first()
    {
        var grid = Grid((0, 0, 0, 1), (5, 5, 5, 1), (5, 5, 6, 1), (5, 6, 6, 1));

        var clusters = Clusters.Find(grid, Size, _ => 1, minCells: 2);

        var only = Assert.Single(clusters);
        Assert.Equal(3, only.Cells);
    }

    [Fact]
    public void A_cluster_may_span_the_whole_grid()
    {
        var grid = new ushort[Size * Size * Size];
        Array.Fill(grid, (ushort)1);

        var all = Assert.Single(Clusters.Find(grid, Size, _ => 2));

        Assert.Equal((Size * Size * Size, 2.0 * Size * Size * Size), (all.Cells, all.Mass));
        Assert.Equal(new Vector3(Size / 2f), all.Center);
    }

    [Fact]
    public void A_wrong_cell_count_is_refused() =>
        Assert.Throws<ArgumentException>(() => Clusters.Find(new ushort[7], 2, _ => 1));
}
