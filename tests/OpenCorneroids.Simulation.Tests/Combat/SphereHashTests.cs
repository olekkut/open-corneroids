using System.Numerics;
using OpenCorneroids.Simulation.Combat;

namespace OpenCorneroids.Simulation.Tests.Combat;

public class SphereHashTests
{
    static List<(float At, int Index)> Hits(SphereHash hash, Vector3 from, Vector3 to)
    {
        var hits = new List<(float, int)>();
        hash.SegmentHits(from, to, hits);
        return hits;
    }

    static List<(float At, int Index)> Brute(SphereHash hash, Vector3 from, Vector3 to)
    {
        var hits = new List<(float, int)>();
        hash.SegmentHitsBruteForce(from, to, hits);
        return hits;
    }

    [Fact]
    public void A_segment_through_two_spheres_reports_both_nearest_first()
    {
        var hash = new SphereHash();
        int far = hash.Add(new Vector3(20, 0, 0), 1);
        int near = hash.Add(new Vector3(5, 0, 0), 1);
        hash.Add(new Vector3(10, 10, 0), 1);   // off the path

        var hits = Hits(hash, Vector3.Zero, new Vector3(30, 0, 0));

        Assert.Equal([near, far], hits.Select(h => h.Index));
        Assert.Equal(4f / 30, hits[0].At, 5);
        Assert.Equal(19f / 30, hits[1].At, 5);
    }

    [Fact]
    public void A_segment_starting_inside_a_sphere_hits_it_at_zero_even_when_it_has_no_length()
    {
        var hash = new SphereHash();
        hash.Add(new Vector3(1, 1, 1), 2);

        Assert.Equal([(0f, 0)], Hits(hash, new Vector3(1.5f, 1, 1), new Vector3(1.5f, 1, 1)));
        Assert.Empty(Hits(hash, new Vector3(9, 9, 9), new Vector3(9, 9, 9)));
    }

    [Fact]
    public void A_sphere_reaching_into_a_neighbouring_cell_is_found_from_there()
    {
        // Centre in one 8 m cell, the shot only in the next one: the sphere's edge reaches over.
        var hash = new SphereHash();
        hash.Add(new Vector3(7.5f, 4, 4), 1);

        var hits = Hits(hash, new Vector3(8.2f, 0, 4), new Vector3(8.2f, 8, 4));

        Assert.Single(hits);
    }

    [Fact]
    public void Clearing_forgets_every_sphere_and_the_hash_can_be_filled_again()
    {
        var hash = new SphereHash();
        hash.Add(new Vector3(5, 0, 0), 1);
        hash.Clear();
        Assert.Equal(0, hash.Count);
        Assert.Empty(Hits(hash, Vector3.Zero, new Vector3(10, 0, 0)));

        int again = hash.Add(new Vector3(0, 5, 0), 1);
        Assert.Equal(0, again);
        Assert.Empty(Hits(hash, Vector3.Zero, new Vector3(10, 0, 0)));
        Assert.Single(Hits(hash, Vector3.Zero, new Vector3(0, 10, 0)));
    }

    [Fact]
    public void Spheres_that_can_not_be_hit_keep_their_index_but_are_never_reported()
    {
        var hash = new SphereHash();
        hash.Add(new Vector3(5, 0, 0), 0);
        hash.Add(new Vector3(5, 0, 0), -1);
        hash.Add(new Vector3(5, 0, 0), float.NaN);
        hash.Add(new Vector3(float.NaN, 0, 0), 1);
        int real = hash.Add(new Vector3(5, 0, 0), 1);

        Assert.Equal(5, hash.Count);
        Assert.Equal([real], Hits(hash, Vector3.Zero, new Vector3(10, 0, 0)).Select(h => h.Index));
    }

    [Fact]
    public void Segments_that_are_not_finite_or_very_long_give_the_brute_force_answer()
    {
        var hash = new SphereHash();
        hash.Add(new Vector3(5, 0, 0), 1);
        hash.Add(new Vector3(50_000, 0, 0), 3);

        var nan = new Vector3(float.NaN, 0, 0);
        Assert.Equal(Brute(hash, nan, new Vector3(10, 0, 0)), Hits(hash, nan, new Vector3(10, 0, 0)));
        var to = new Vector3(100_000, 0, 0);
        Assert.Equal(Brute(hash, Vector3.Zero, to), Hits(hash, Vector3.Zero, to));
        Assert.Equal(2, Hits(hash, Vector3.Zero, to).Count);
    }

    [Fact]
    public void A_huge_sphere_is_still_found()
    {
        var hash = new SphereHash(cellSize: 2);
        int big = hash.Add(new Vector3(0, 0, 0), 40);

        Assert.Equal([big], Hits(hash, new Vector3(-100, 39, 0), new Vector3(100, 39, 0)).Select(h => h.Index));
    }

    [Fact]
    public void A_cell_size_must_be_positive_and_finite()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SphereHash(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SphereHash(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SphereHash(float.PositiveInfinity));
    }

    /// <summary>The hash gives exactly the brute-force answer: same spheres, same distances, same order, over random
    /// scenes and shots, including shots along cell edges and through cell corners.</summary>
    [Theory]
    [InlineData(8f, 1)]
    [InlineData(8f, 2)]
    [InlineData(1f, 3)]
    [InlineData(3.5f, 4)]
    public void Random_shots_match_the_brute_force_check(float cellSize, int seed)
    {
        var random = new Random(seed);
        var hash = new SphereHash(cellSize);
        var hits = new List<(float, int)>();
        var expected = new List<(float, int)>();
        int found = 0;
        for (int scene = 0; scene < 200; scene++)
        {
            hash.Clear();
            int spheres = random.Next(0, 60);
            for (int i = 0; i < spheres; i++)
            {
                float radius = random.Next(10) switch
                {
                    0 => (float)random.NextDouble() * 30,     // now and then a big one
                    1 => 0.735f,
                    _ => 0.05f + (float)random.NextDouble() * 2.5f,
                };
                hash.Add(Point(random, 40), radius);
            }
            for (int shot = 0; shot < 50; shot++)
            {
                var from = Point(random, 50);
                var to = random.Next(4) switch
                {
                    0 => from,                                                              // no travel
                    1 => from + Axis(random) * (float)(random.NextDouble() * 60),           // along an axis
                    2 => Snap(from, cellSize) + Vector3.Normalize(new Vector3(1, 1, 1)) *
                         (float)(random.NextDouble() * 60) * (random.Next(2) == 0 ? 1 : -1), // through cell corners
                    _ => from + Point(random, 30),
                };
                if (random.Next(5) == 0) from = Snap(from, cellSize);                       // starting on a cell edge
                hash.SegmentHits(from, to, hits);
                hash.SegmentHitsBruteForce(from, to, expected);
                Assert.Equal(expected, hits);
                found += hits.Count;
            }
        }
        Assert.True(found > 500, $"only {found} hits: the random scenes are too sparse to test anything");
    }

    static Vector3 Point(Random random, float extent) =>
        new(((float)random.NextDouble() * 2 - 1) * extent, ((float)random.NextDouble() * 2 - 1) * extent, ((float)random.NextDouble() * 2 - 1) * extent);

    static Vector3 Axis(Random random)
    {
        float sign = random.Next(2) == 0 ? 1 : -1;
        return random.Next(3) switch { 0 => Vector3.UnitX * sign, 1 => Vector3.UnitY * sign, _ => Vector3.UnitZ * sign };
    }

    static Vector3 Snap(Vector3 v, float cell) =>
        new(MathF.Round(v.X / cell) * cell, MathF.Round(v.Y / cell) * cell, MathF.Round(v.Z / cell) * cell);
}
