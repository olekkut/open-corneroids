using System.Numerics;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class MassAccumulatorTests
{
    const float Tolerance = 1e-4f;

    static void AssertClose(float expected, float actual, string what)
    {
        float scale = Math.Max(1, Math.Abs(expected));
        Assert.True(Math.Abs(expected - actual) <= Tolerance * scale, $"{what}: expected {expected}, got {actual}");
    }

    static void AssertSameBody(MassProperties expected, MassProperties actual)
    {
        AssertClose(expected.Mass, actual.Mass, "mass");
        AssertClose(expected.CenterOfMass.X, actual.CenterOfMass.X, "centre x");
        AssertClose(expected.CenterOfMass.Y, actual.CenterOfMass.Y, "centre y");
        AssertClose(expected.CenterOfMass.Z, actual.CenterOfMass.Z, "centre z");
        var a = expected.Inertia;
        var b = actual.Inertia;
        float[] ea = [a.M11, a.M12, a.M13, a.M22, a.M23, a.M33];
        float[] eb = [b.M11, b.M12, b.M13, b.M22, b.M23, b.M33];
        for (int i = 0; i < ea.Length; i++) AssertClose(ea[i], eb[i], $"inertia {i}");
    }

    [Fact]
    public void Nothing_counted_is_massless()
    {
        var sums = new MassAccumulator();

        Assert.Same(MassProperties.Empty, sums.ToProperties());
        Assert.Equal(0, sums.Boxes);
    }

    [Fact]
    public void One_unit_cube_matches_the_full_sum()
    {
        var sums = new MassAccumulator();
        sums.Add(new Vector3(10.5f, 3.5f, 7.5f), Vector3.One, 6);

        AssertSameBody(MassProperties.From([(new Vector3(10.5f, 3.5f, 7.5f), Vector3.One, 6f)]), sums.ToProperties());
        Assert.Equal(1f, sums.ToProperties().Inertia.M11, 4);   // m/12·(1 + 1)
    }

    [Fact]
    public void Boxes_the_full_sum_skips_are_skipped_both_ways()
    {
        var sums = new MassAccumulator();
        sums.Add(Vector3.One, Vector3.One, 0);
        sums.Add(Vector3.One, Vector3.One, -3);
        sums.Add(new Vector3(float.NaN, 0, 0), Vector3.One, 2);
        sums.Add(Vector3.One, Vector3.One, float.PositiveInfinity);
        Assert.Equal(0, sums.Boxes);

        sums.Add(Vector3.One, new Vector3(-1, 2, float.NaN), 2);
        AssertSameBody(MassProperties.From([(Vector3.One, new Vector3(-1, 2, float.NaN), 2f)]), sums.ToProperties());
    }

    [Fact]
    public void Removing_every_box_leaves_exactly_nothing()
    {
        var sums = new MassAccumulator();
        sums.Add(new Vector3(1.5f, 2.5f, 3.5f), Vector3.One, 0.3f);
        sums.Add(new Vector3(40.5f, 2.5f, 3.5f), new Vector3(2, 3, 1), 1.7f);
        sums.Remove(new Vector3(1.5f, 2.5f, 3.5f), Vector3.One, 0.3f);
        sums.Remove(new Vector3(40.5f, 2.5f, 3.5f), new Vector3(2, 3, 1), 1.7f);

        Assert.Equal(0, sums.Boxes);
        Assert.Equal(0, sums.TotalMass);
        Assert.Same(MassProperties.Empty, sums.ToProperties());
    }

    [Fact]
    public void Random_edits_match_the_full_sum()
    {
        var rng = new Random(1618);
        var sums = new MassAccumulator();
        var placed = new Dictionary<(int, int, int), (Vector3 Center, Vector3 Size, float Mass)>();
        float[] masses = [0.5f, 1, 1.3f, 2.25f, 7, 0.1f];
        for (int round = 0; round < 40; round++)
        {
            for (int edit = 0; edit < 150; edit++)
            {
                var key = (rng.Next(64), rng.Next(64), rng.Next(64));
                if (placed.Remove(key, out var old))
                {
                    sums.Remove(old.Center, old.Size, old.Mass);
                    if (rng.Next(2) == 0) continue;
                }
                // Mostly unit cubes, sometimes a bigger object box.
                var size = rng.Next(6) == 0 ? new Vector3(rng.Next(1, 4), rng.Next(1, 12), rng.Next(1, 3)) : Vector3.One;
                var center = new Vector3(key.Item1, key.Item2, key.Item3) + size * 0.5f;
                var box = (center, size, masses[rng.Next(masses.Length)]);
                placed[key] = box;
                sums.Add(box.center, box.size, box.Item3);
            }
            Assert.Equal(placed.Count, sums.Boxes);
            AssertSameBody(MassProperties.From(placed.Values.Select(b => (b.Center, b.Size, b.Mass))), sums.ToProperties());
        }
    }
}
