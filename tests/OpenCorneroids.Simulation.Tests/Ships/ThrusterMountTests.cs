using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;
using static OpenCorneroids.Simulation.Tests.Shapes.Support.ShapeData;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class ThrusterMountTests
{
    /// <summary>Vanilla 1.2.0 "Weak hydrogen thruster": bounds 1×3×1, model 1×1×1 at placement y −1, force 20.</summary>
    static readonly BlockShape Weak = Shape(new(1, 3, 1), new(1, 1, 1), new(0, -1, 0));
    static readonly ThrusterComponent WeakThrust = new(20, 0, Vector3.Zero);

    static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Fact]
    public void Upright_weak_thruster_flames_up_and_pushes_down_from_its_model()
    {
        var mount = ThrusterMount.FromShape(Weak, new Int3(4, 5, 6), Orientation.Default, WeakThrust, 10);
        AssertClose(new Vector3(4.5f, 5.5f, 6.5f), mount.LocalPosition);   // bottom cell of the footprint
        AssertClose(-Vector3.UnitY, mount.LocalForceDirection);
        AssertClose(Vector3.UnitY, mount.LocalFlameDirection);
        Assert.Equal(200, mount.MaxForce);
    }

    [Fact]
    public void Upside_down_thruster_pushes_up_from_the_top_cell()
    {
        var mount = ThrusterMount.FromShape(Weak, Int3.Zero, Orientation.From(AxisDirection.NegY, 0), WeakThrust);
        AssertClose(new Vector3(0.5f, 2.5f, 0.5f), mount.LocalPosition);
        AssertClose(Vector3.UnitY, mount.LocalForceDirection);
        Assert.Equal(20 * ThrusterMount.DefaultThrustScale, mount.MaxForce);
    }

    [Theory]
    [InlineData(AxisDirection.PosX, -1, 0, 0, 0.5f, 0.5f, 0.5f)]
    [InlineData(AxisDirection.NegX, 1, 0, 0, 2.5f, 0.5f, 0.5f)]
    [InlineData(AxisDirection.PosZ, 0, 0, -1, 0.5f, 0.5f, 0.5f)]
    [InlineData(AxisDirection.NegZ, 0, 0, 1, 0.5f, 0.5f, 2.5f)]
    public void Tipped_thrusters_push_against_their_wash(AxisDirection up, float fx, float fy, float fz, float px, float py, float pz)
    {
        foreach (int spin in new[] { 0, 1, 2, 3 })
        {
            var mount = ThrusterMount.FromShape(Weak, Int3.Zero, Orientation.From(up, spin), WeakThrust);
            AssertClose(new Vector3(fx, fy, fz), mount.LocalForceDirection);
            AssertClose(new Vector3(px, py, pz), mount.LocalPosition);
        }
    }

    [Fact]
    public void Every_orientation_points_the_flame_from_the_model_into_the_footprint()
    {
        var origin = new Int3(-3, 7, 2);
        foreach (var o in Orientation.All)
        {
            var mount = ThrusterMount.FromShape(Weak, origin, o, WeakThrust);
            AssertClose(-o.Apply(Vector3.UnitY), mount.LocalForceDirection);
            var footprintCenter = origin.ToVector3() + Weak.RotatedSize(o).ToVector3() * 0.5f;
            AssertClose(footprintCenter, mount.LocalPosition + mount.LocalFlameDirection);   // one cell toward the wash
            Assert.Contains(Int3.Floor(mount.LocalPosition), Weak.Footprint(origin, o));
        }
    }

    [Fact]
    public void Wash_on_another_axis_is_followed()
    {
        // A made-up sideways thruster: bounds 3×1×1, model in the +X cell, so the wash is toward −X.
        var sideways = Shape(new(3, 1, 1), new(1, 1, 1), new(1, 0, 0));
        var mount = ThrusterMount.FromShape(sideways, Int3.Zero, Orientation.Default, WeakThrust);
        AssertClose(Vector3.UnitX, mount.LocalForceDirection);
        AssertClose(new Vector3(2.5f, 0.5f, 0.5f), mount.LocalPosition);
    }

    [Fact]
    public void Thruster_without_wash_falls_back_to_local_up_flame()
    {
        var full = Shape(Vector3.One);
        var mount = ThrusterMount.FromShape(full, Int3.Zero, Orientation.From(AxisDirection.PosZ, 0), WeakThrust);
        AssertClose(-Vector3.UnitZ, mount.LocalForceDirection);
    }

    [Fact]
    public void Bad_force_numbers_give_no_force()
    {
        Assert.Equal(0, ThrusterMount.FromShape(Weak, Int3.Zero, Orientation.Default, new ThrusterComponent(-5, 0, Vector3.Zero)).MaxForce);
        Assert.Equal(0, ThrusterMount.FromShape(Weak, Int3.Zero, Orientation.Default, new ThrusterComponent(float.NaN, 0, Vector3.Zero)).MaxForce);
        Assert.Equal(0, ThrusterMount.FromShape(Weak, Int3.Zero, Orientation.Default, WeakThrust, float.PositiveInfinity).MaxForce);
    }
}
