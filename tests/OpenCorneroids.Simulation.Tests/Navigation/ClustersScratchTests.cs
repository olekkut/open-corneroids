using OpenCorneroids.Simulation.Navigation;

namespace OpenCorneroids.Simulation.Tests.Navigation;

public class ClustersScratchTests
{
    static ushort[] RandomRock(Random rng, int size, double fill)
    {
        var cells = new ushort[size * size * size];
        for (int i = 0; i < cells.Length; i++)
            if (rng.NextDouble() < fill) cells[i] = (ushort)rng.Next(1, 4);
        return cells;
    }

    [Fact]
    public void Reused_buffers_find_the_same_clusters_as_fresh_ones()
    {
        var rng = new Random(31337);
        var scratch = new Clusters.Scratch();
        Func<ushort, float> mass = v => v * 1.5f;
        // Sizes change between searches, and the same size comes back with different cells, so stale "seen" marks
        // from an earlier search would show.
        (int Size, double Fill)[] searches = [(8, 0.3), (8, 0.5), (16, 0.2), (8, 0.25), (24, 0.35), (24, 0.1)];
        foreach (var (size, fill) in searches)
        {
            var cells = RandomRock(rng, size, fill);

            var fresh = Clusters.Find(cells, size, mass, 2);
            var reused = Clusters.Find(cells, size, mass, 2, scratch);

            Assert.Equal(fresh, reused);
        }
    }

    [Fact]
    public void The_scratch_overload_checks_its_arguments()
    {
        var cells = new ushort[8];
        Assert.Throws<ArgumentNullException>(() => Clusters.Find(cells, 2, _ => 1, 1, null!));
        Assert.Throws<ArgumentException>(() => Clusters.Find(cells, 3, _ => 1, 1, new Clusters.Scratch()));
    }
}
