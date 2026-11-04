using System.Numerics;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class ShipContactTests
{
    /// <summary>A floor: every cell with y &lt; 0 is solid.</summary>
    static bool Floor(int x, int y, int z) => y < 0;

    static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void Points_inside_a_floor_say_up()
    {
        var normal = ShipContact.Normal([new Vector3(0.2f, -0.1f, 0.7f), new Vector3(3.9f, -0.05f, 0.1f)], Floor);
        Assert.NotNull(normal);
        Near(Vector3.UnitY, normal.Value);
    }

    [Fact]
    public void A_face_against_more_rock_is_never_the_way_out()
    {
        // Deep in the floor, nearer the cell's bottom (-Y) and its +X side: below is rock, so +X wins.
        var normal = ShipContact.Normal([new Vector3(0.95f, -1.9f, 0.5f)], (x, y, z) => y < 0 && x < 1);
        Assert.NotNull(normal);
        Near(Vector3.UnitX, normal.Value);
    }

    [Fact]
    public void A_corner_gives_the_diagonal()
    {
        static bool Corner(int x, int y, int z) => y < 0 || x < 0;
        var normal = ShipContact.Normal([new Vector3(0.5f, -0.05f, 0.5f), new Vector3(-0.05f, 0.5f, 0.5f)], Corner);
        Assert.NotNull(normal);
        Near(Vector3.Normalize(new Vector3(1, 1, 0)), normal.Value);
    }

    [Fact]
    public void No_point_inside_or_no_way_out_gives_no_normal()
    {
        Assert.Null(ShipContact.Normal([new Vector3(0.5f, 0.5f, 0.5f)], Floor));
        Assert.Null(ShipContact.Normal([new Vector3(0.5f, 0.5f, 0.5f)], (_, _, _) => true));
        Assert.Null(ShipContact.Normal([], Floor));
    }

    [Fact]
    public void In_solid_ignores_non_finite_points()
    {
        Assert.True(ShipContact.InSolid(new Vector3(0, -0.5f, 0), Floor));
        Assert.False(ShipContact.InSolid(new Vector3(0, 0, 0), Floor));
        Assert.False(ShipContact.InSolid(new Vector3(float.NaN, -1, 0), Floor));
    }

    [Fact]
    public void Last_free_finds_the_contact_from_below()
    {
        // A body moving down from y = 1 to y = -1 overlaps once it is below 0.25, i.e. after 37.5% of the step.
        float t = ShipContact.LastFree(f => 1 - 2 * f < 0.25f, steps: 16);
        Assert.InRange(t, 0.375f - 1e-3f, 0.375f);
        Assert.False(1 - 2 * t < 0.25f);
    }

    [Fact]
    public void Last_free_handles_no_contact_and_a_body_already_inside()
    {
        Assert.Equal(1, ShipContact.LastFree(_ => false));
        Assert.Equal(0, ShipContact.LastFree(_ => true));
    }

    [Fact]
    public void A_bounce_against_rock_keeps_the_slide_and_reverses_some_of_the_approach()
    {
        var v = ShipContact.Bounce(new Vector3(3, -10, 0), Vector3.UnitY, restitution: 0.2f);
        Near(new Vector3(3, 2, 0), v);
        // Moving away already: untouched.
        Near(new Vector3(3, 1, 0), ShipContact.Bounce(new Vector3(3, 1, 0), Vector3.UnitY));
        // No restitution: stops dead along the normal.
        Near(new Vector3(0, 0, 4), ShipContact.Bounce(new Vector3(0, -5, 4), Vector3.UnitY, restitution: 0));
    }

    [Fact]
    public void Two_bodies_keep_their_momentum()
    {
        var (a, b) = ShipContact.Bounce(new Vector3(-4, 0, 0), 1f / 100, new Vector3(1, 0, 0), 1f / 300, Vector3.UnitX, restitution: 0.5f);
        var before = 100 * -4f + 300 * 1f;
        var after = 100 * a.X + 300 * b.X;
        Assert.Equal(before, after, 3);
        // Closing speed 5 becomes 2.5 apart.
        Assert.Equal(2.5f, a.X - b.X, 4);
    }

    [Fact]
    public void A_massless_partner_counts_as_immovable()
    {
        var (a, b) = ShipContact.Bounce(new Vector3(-2, 0, 0), 0.5f, Vector3.Zero, 0, Vector3.UnitX, restitution: 0);
        Near(Vector3.Zero, a);
        Near(Vector3.Zero, b);
        var (c, d) = ShipContact.Bounce(new Vector3(-2, 0, 0), 0, Vector3.Zero, 0, Vector3.UnitX);
        Near(new Vector3(-2, 0, 0), c);
        Near(Vector3.Zero, d);
    }
}
