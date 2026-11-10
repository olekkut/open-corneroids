using System.Numerics;
using OpenCorneroids.Simulation.Grids;

namespace OpenCorneroids.Simulation.Tests.Grids;

public class SurfaceSamplerTests
{
    static readonly (int X, int Y, int Z)[] Steps = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    /// <summary>The whole computation done again from scratch: every solid cell with an open face gives a point inside
    /// each open face and its centre.</summary>
    static List<Vector3> FullComputation(VoxelCells cells)
    {
        var points = new List<Vector3>();
        int size = cells.Size;
        for (int z = 0; z < size; z++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            if (!cells.IsSolid(x, y, z)) continue;
            var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
            bool open = false;
            foreach (var (sx, sy, sz) in Steps)
            {
                if (cells.IsSolid(x + sx, y + sy, z + sz)) continue;
                points.Add(center + new Vector3(sx, sy, sz) * 0.45f);
                open = true;
            }
            if (open) points.Add(center);
        }
        return points;
    }

    static void AssertSamePoints(List<Vector3> expected, IReadOnlyList<Vector3> actual)
    {
        static List<Vector3> Sorted(IEnumerable<Vector3> points) =>
            [.. points.OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z)];
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(Sorted(expected), Sorted(actual));
    }

    [Fact]
    public void A_lone_block_gives_six_face_points_and_its_centre()
    {
        var cells = new VoxelCells(16);
        cells.Set(3, 4, 5, 1);

        var points = new SurfaceSampler(cells).Points;

        Assert.Equal(7, points.Count);
        Assert.Contains(new Vector3(3.5f, 4.5f, 5.5f), points);
        Assert.Contains(new Vector3(3.95f, 4.5f, 5.5f), points);
        Assert.Contains(new Vector3(3.5f, 4.5f, 5.05f), points);
    }

    [Fact]
    public void A_buried_cell_adds_nothing()
    {
        var cells = new VoxelCells(16);
        for (int z = 0; z < 3; z++)
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
            cells.Set(x + 1, y + 1, z + 1, 1);

        var points = new SurfaceSampler(cells).Points;

        Assert.DoesNotContain(new Vector3(2.5f, 2.5f, 2.5f), points);
        AssertSamePoints(FullComputation(cells), points);
    }

    [Fact]
    public void Nothing_changed_gives_the_same_array_back()
    {
        var cells = new VoxelCells(32);
        cells.Set(1, 1, 1, 1);
        var sampler = new SurfaceSampler(cells);
        var first = sampler.Points;

        cells.Set(1, 1, 1, 2);   // still solid: no point moves

        Assert.Same(first, sampler.Points);
    }

    [Fact]
    public void An_edit_samples_only_its_chunk_and_the_neighbours_it_touches()
    {
        var cells = new VoxelCells(64);   // 64 chunks
        var sampler = new SurfaceSampler(cells);
        _ = sampler.Points;
        Assert.Equal(64, sampler.LastRebuilt);

        cells.Set(20, 20, 20, 1);   // inside chunk (1, 1, 1)
        _ = sampler.Points;

        Assert.Equal(7, sampler.LastRebuilt);
    }

    [Fact]
    public void A_block_on_a_chunk_border_reopens_the_face_of_its_neighbour()
    {
        var cells = new VoxelCells(32);
        cells.Set(15, 0, 0, 1);
        cells.Set(16, 0, 0, 1);
        var sampler = new SurfaceSampler(cells);
        Assert.DoesNotContain(new Vector3(15.95f, 0.5f, 0.5f), sampler.Points);

        cells.Set(16, 0, 0, 0);

        Assert.Contains(new Vector3(15.95f, 0.5f, 0.5f), sampler.Points);
        AssertSamePoints(FullComputation(cells), sampler.Points);
    }

    [Fact]
    public void Random_edits_match_the_full_computation()
    {
        var rng = new Random(271828);
        var cells = new VoxelCells(48);
        var sampler = new SurfaceSampler(cells);
        for (int round = 0; round < 30; round++)
        {
            for (int edit = 0; edit < 60; edit++)
            {
                int x = rng.Next(8, 40), y = rng.Next(8, 40), z = rng.Next(8, 40);
                if (rng.Next(5) == 0) cells.SetObjectCell(x, y, z, rng.Next(2) == 0);
                else cells.Set(x, y, z, rng.Next(3) == 0 ? (ushort)0 : (ushort)1);
            }
            // A solid slab across chunk borders, sometimes, so buried cells and border faces both occur.
            if (round % 7 == 3)
                for (int x = 12; x < 20; x++)
                for (int y = 12; y < 20; y++)
                    cells.Set(x, y, 16, 1);
            AssertSamePoints(FullComputation(cells), sampler.Points);
        }
    }
}
