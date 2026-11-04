using System.Numerics;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Shapes;

public class OrientationTests
{
    static int Det(Orientation o) =>
        o[0, 0] * (o[1, 1] * o[2, 2] - o[1, 2] * o[2, 1])
        - o[0, 1] * (o[1, 0] * o[2, 2] - o[1, 2] * o[2, 0])
        + o[0, 2] * (o[1, 0] * o[2, 1] - o[1, 1] * o[2, 0]);

    static string Key(Orientation o) => string.Join(",", o.ToMatrix().Cast<int>());

    [Fact]
    public void There_are_24_distinct_proper_rotations()
    {
        Assert.Equal(24, Orientation.All.Count);
        Assert.Equal(24, Orientation.All.Select(Key).Distinct().Count());
        foreach (var o in Orientation.All)
        {
            Assert.Equal(1, Det(o));
            // Every column is a unit axis vector: a signed permutation matrix.
            for (int c = 0; c < 3; c++) Assert.NotNull(AxisDirections.FromInt3(o.Column(c)));
        }
    }

    [Fact]
    public void Default_is_the_identity_and_index_zero()
    {
        Assert.Equal(0, Orientation.Default.Index);
        Assert.Equal(AxisDirection.PosY, Orientation.Default.Up);
        Assert.Equal(0, Orientation.Default.Spin);
        Assert.Equal(Orientation.Default, default);
        Assert.Equal(new Int3(3, -4, 5), Orientation.Default.Apply(new Int3(3, -4, 5)));
    }

    [Fact]
    public void Index_round_trips()
    {
        for (int i = 0; i < Orientation.Count; i++)
        {
            var o = Orientation.FromIndex(i);
            Assert.Equal(i, o.Index);
            Assert.Equal(o, Orientation.From(o.Up, o.Spin));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => Orientation.FromIndex(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => Orientation.FromIndex(-1));
    }

    [Fact]
    public void Up_is_where_local_y_points()
    {
        foreach (var o in Orientation.All)
            Assert.Equal(o.Up.ToInt3(), o.Apply(Int3.UnitY));
    }

    [Fact]
    public void Spin_turns_about_up_and_keeps_it()
    {
        foreach (var o in Orientation.All)
        {
            var next = o.NextSpin();
            Assert.Equal(o.Up, next.Up);
            Assert.Equal((o.Spin + 1) % 4, next.Spin);
            Assert.Equal(o, o.NextSpin().NextSpin().NextSpin().NextSpin());
        }
        // Counter-clockwise seen from above: +X turns to −Z for up +Y (right-hand rule).
        Assert.Equal(-Int3.UnitZ, Orientation.From(AxisDirection.PosY, 1).Apply(Int3.UnitX));
    }

    [Fact]
    public void Next_up_cycles_all_six_and_keeps_spin()
    {
        var o = Orientation.From(AxisDirection.PosY, 2);
        var seen = new HashSet<AxisDirection>();
        for (int i = 0; i < 6; i++, o = o.NextUp())
        {
            seen.Add(o.Up);
            Assert.Equal(2, o.Spin);
        }
        Assert.Equal(6, seen.Count);
        Assert.Equal(Orientation.From(AxisDirection.PosY, 2), o);
    }

    [Fact]
    public void Inverse_undoes_and_composes_to_identity()
    {
        var probe = new Int3(1, 2, 3);
        foreach (var o in Orientation.All)
        {
            Assert.Equal(probe, o.Inverse.Apply(o.Apply(probe)));
            Assert.Equal(Orientation.Default, o.Then(o.Inverse));
            Assert.Equal(Orientation.Default, o.Inverse.Then(o));
        }
    }

    [Fact]
    public void Then_applies_first_then_second()
    {
        var probe = new Int3(1, 2, 3);
        foreach (var a in Orientation.All)
            foreach (var b in Orientation.All)
                Assert.Equal(b.Apply(a.Apply(probe)), a.Then(b).Apply(probe));
    }

    [Fact]
    public void Apply_preserves_lengths_and_matches_for_ints_and_floats()
    {
        var v = new Vector3(0.5f, -2.25f, 7f);
        var i = new Int3(4, -1, 9);
        foreach (var o in Orientation.All)
        {
            Assert.Equal(v.Length(), o.Apply(v).Length(), 5);
            Assert.Equal(Int3.Dot(i, i), Int3.Dot(o.Apply(i), o.Apply(i)));
            Assert.Equal(o.Apply(i).ToVector3(), o.Apply(i.ToVector3()));
        }
    }
}
