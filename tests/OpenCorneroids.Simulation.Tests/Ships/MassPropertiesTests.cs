using System.Numerics;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class MassPropertiesTests
{
    static void AssertClose(float expected, float actual, float tolerance = 1e-4f) =>
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");

    static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");

    static void AssertTensor(float[,] expected, Matrix4x4 m, float tolerance = 1e-4f)
    {
        float[,] actual = { { m.M11, m.M12, m.M13 }, { m.M21, m.M22, m.M23 }, { m.M31, m.M32, m.M33 } };
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                AssertClose(expected[r, c], actual[r, c], tolerance);
    }

    static MassProperties Boxes(params (Vector3 center, Vector3 size, float mass)[] boxes) => MassProperties.From(boxes);

    [Fact]
    public void Single_cube_has_its_centre_and_the_textbook_tensor()
    {
        // Solid cube of side 1, mass 6: I = m/12 · (1 + 1) = 1 on every axis.
        var mp = Boxes((new(2, 3, 4), Vector3.One, 6));
        Assert.Equal(6, mp.Mass);
        AssertClose(1f / 6, mp.InverseMass);
        AssertClose(new Vector3(2, 3, 4), mp.CenterOfMass);
        AssertTensor(new float[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } }, mp.Inertia);
        AssertTensor(new float[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } }, mp.InverseInertia);
    }

    [Fact]
    public void Two_cubes_use_the_parallel_axis_theorem()
    {
        // Unit cubes of mass 1 at x 0 and 2: COM x 1. Ixx = 2·(1/6); Iyy = Izz = 2·(1/6) + 2·1·1².
        var mp = Boxes((Vector3.Zero, Vector3.One, 1), (new(2, 0, 0), Vector3.One, 1));
        Assert.Equal(2, mp.Mass);
        AssertClose(new Vector3(1, 0, 0), mp.CenterOfMass);
        AssertTensor(new float[,] { { 1f / 3, 0, 0 }, { 0, 7f / 3, 0 }, { 0, 0, 7f / 3 } }, mp.Inertia);
        AssertTensor(new float[,] { { 3, 0, 0 }, { 0, 3f / 7, 0 }, { 0, 0, 3f / 7 } }, mp.InverseInertia);
    }

    [Fact]
    public void Unequal_masses_pull_the_centre_toward_the_heavy_one()
    {
        var mp = Boxes((Vector3.Zero, Vector3.One, 1), (new(4, 0, 0), Vector3.One, 3));
        AssertClose(new Vector3(3, 0, 0), mp.CenterOfMass);
        // Iyy = 4·(1/6) + 1·3² + 3·1² = 2/3 + 12.
        AssertClose(2f / 3 + 12, mp.Inertia.M22);
    }

    [Fact]
    public void Rod_of_cubes_matches_the_solid_rod()
    {
        // Five unit cubes along X equal one 5×1×1 box: Ixx = m/12·(1+1), Iyy = Izz = m/12·(25+1).
        var cubes = MassProperties.From(Enumerable.Range(0, 5).Select(x => (new Vector3(x, 0, 0), Vector3.One, 1f)));
        var solid = Boxes((new(2, 0, 0), new(5, 1, 1), 5));
        AssertClose(new Vector3(2, 0, 0), cubes.CenterOfMass);
        AssertTensor(new float[,] { { 10f / 12, 0, 0 }, { 0, 130f / 12, 0 }, { 0, 0, 130f / 12 } }, cubes.Inertia);
        AssertTensor(new float[,] { { 10f / 12, 0, 0 }, { 0, 130f / 12, 0 }, { 0, 0, 130f / 12 } }, solid.Inertia);
    }

    [Fact]
    public void Ideal_thin_rod_gets_a_pseudo_inverse_instead_of_infinity()
    {
        // Zero thickness: Ixx = 0, Iyy = Izz = mL²/12 = 2·36/12 = 6.
        var mp = Boxes((Vector3.Zero, new(6, 0, 0), 2));
        AssertTensor(new float[,] { { 0, 0, 0 }, { 0, 6, 0 }, { 0, 0, 6 } }, mp.Inertia);
        AssertTensor(new float[,] { { 0, 0, 0 }, { 0, 1f / 6, 0 }, { 0, 0, 1f / 6 } }, mp.InverseInertia);
    }

    [Fact]
    public void Off_diagonal_products_and_their_inverse()
    {
        // Unit cubes of mass 1 at (1,1,0) and (−1,−1,0): Ixy = −Σ m·x·y = −2.
        var mp = Boxes((new(1, 1, 0), Vector3.One, 1), (new(-1, -1, 0), Vector3.One, 1));
        AssertClose(Vector3.Zero, mp.CenterOfMass);
        AssertTensor(new float[,] { { 2 + 1f / 3, -2, 0 }, { -2, 2 + 1f / 3, 0 }, { 0, 0, 4 + 1f / 3 } }, mp.Inertia);
        var product = mp.Inertia * mp.InverseInertia;
        AssertTensor(new float[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } }, product);
    }

    [Fact]
    public void Inverse_undoes_the_tensor_for_an_irregular_ship()
    {
        var rng = new Random(7);
        var boxes = Enumerable.Range(0, 30)
            .Select(_ => (new Vector3(rng.Next(-5, 6), rng.Next(-3, 4), rng.Next(-8, 9)), Vector3.One, (float)rng.Next(1, 6)))
            .ToList();
        var mp = MassProperties.From(boxes);
        var v = new Vector3(0.3f, -1.2f, 2.5f);
        AssertClose(v, MassProperties.Multiply(mp.InverseInertia, MassProperties.Multiply(mp.Inertia, v)), 1e-3f);
    }

    [Fact]
    public void World_inertia_follows_the_orientation()
    {
        // A 4×1×1 bar turned 90° about Z lies along world Y, so spinning about world Y is the cheap axis.
        var mp = Boxes((Vector3.Zero, new(4, 1, 1), 12));
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        AssertClose(new Vector3(0, 2, 0), mp.ApplyWorldInertia(q, Vector3.UnitY));      // m/12·(1+1)
        AssertClose(new Vector3(17, 0, 0), mp.ApplyWorldInertia(q, Vector3.UnitX));     // m/12·(16+1)
        AssertClose(new Vector3(0, 0.5f, 0), mp.ApplyWorldInverseInertia(q, Vector3.UnitY));
    }

    [Fact]
    public void Nothing_or_only_bad_boxes_is_massless_without_nan()
    {
        foreach (var mp in new[]
        {
            MassProperties.From([]),
            Boxes((Vector3.Zero, Vector3.One, 0), (Vector3.One, Vector3.One, -3), (Vector3.Zero, Vector3.One, float.NaN),
                (new(float.NaN, 0, 0), Vector3.One, 1)),
        })
        {
            Assert.True(mp.IsMassless);
            Assert.Equal(0, mp.Mass);
            Assert.Equal(0, mp.InverseMass);
            Assert.Equal(Vector3.Zero, mp.CenterOfMass);
            Assert.Equal(Vector3.Zero, MassProperties.Multiply(mp.InverseInertia, Vector3.One));
        }
        Assert.Throws<ArgumentNullException>(() => MassProperties.From(null!));
    }

    [Fact]
    public void Point_mass_has_no_rotational_inertia_and_a_zero_inverse()
    {
        var mp = Boxes((new(1, 2, 3), Vector3.Zero, 4));
        Assert.Equal(4, mp.Mass);
        Assert.Equal(Vector3.Zero, MassProperties.Multiply(mp.Inertia, Vector3.One));
        Assert.Equal(Vector3.Zero, MassProperties.Multiply(mp.InverseInertia, Vector3.One));
    }

    [Fact]
    public void Far_from_the_origin_keeps_precision()
    {
        var offset = new Vector3(100_000, -50_000, 30_000);
        var mp = Boxes((offset, Vector3.One, 1), (offset + new Vector3(2, 0, 0), Vector3.One, 1));
        AssertClose(7f / 3, mp.Inertia.M22, 1e-3f);
    }
}
