using System.Numerics;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class FlightAssistTests
{
    /// <summary>Unit cube of mass 6: I = 1 on every axis.</summary>
    static readonly MassProperties Cube = MassProperties.From([(Vector3.Zero, Vector3.One, 6f)]);
    static readonly FlightAssistGains Gains = new(2, 4, 1000, 1000);

    static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"expected {expected}, got {actual}");

    [Fact]
    public void Request_from_rest_pushes_that_way()
    {
        var s = new RigidBodyState();
        var c = FlightAssist.Command(s, Cube, new Vector3(0, 0, -5), Vector3.Zero, dampeners: true, Gains);
        AssertClose(new Vector3(0, 0, -5 * 2 * 6), c.ForceLocal);    // error · response · mass
        AssertClose(Vector3.Zero, c.TorqueLocal);
        var off = FlightAssist.Command(s, Cube, new Vector3(0, 0, -5), Vector3.Zero, dampeners: false, Gains);
        AssertClose(c.ForceLocal, off.ForceLocal);
    }

    [Fact]
    public void Command_is_in_the_ship_frame()
    {
        // Ship turned 90° about Y, drifting along world −Z = its local +X.
        var s = new RigidBodyState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new Vector3(0, 0, -3));
        var c = FlightAssist.Command(s, Cube, Vector3.Zero, Vector3.Zero, dampeners: true, Gains);
        AssertClose(new Vector3(-3 * 2 * 6, 0, 0), c.ForceLocal);
    }

    [Fact]
    public void Dampeners_counter_drift_and_without_them_drift_is_kept()
    {
        var s = new RigidBodyState(Vector3.Zero, Quaternion.Identity, new Vector3(4, 0, 0));
        Assert.True(FlightAssist.Command(s, Cube, Vector3.Zero, Vector3.Zero, true, Gains).ForceLocal.X < 0);
        AssertClose(Vector3.Zero, FlightAssist.Command(s, Cube, Vector3.Zero, Vector3.Zero, false, Gains).ForceLocal);

        // Requesting forward while drifting sideways: with dampeners the sideways drift is cancelled too.
        var on = FlightAssist.Command(s, Cube, new Vector3(0, 0, -2), Vector3.Zero, true, Gains).ForceLocal;
        var off = FlightAssist.Command(s, Cube, new Vector3(0, 0, -2), Vector3.Zero, false, Gains).ForceLocal;
        Assert.True(on.X < 0 && on.Z < 0, $"{on}");
        Assert.True(MathF.Abs(off.X) < 1e-4f && off.Z < 0, $"{off}");
    }

    [Fact]
    public void Without_dampeners_the_assistant_never_brakes()
    {
        var fast = new RigidBodyState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, -10));
        AssertClose(Vector3.Zero, FlightAssist.Command(fast, Cube, new Vector3(0, 0, -5), Vector3.Zero, false, Gains).ForceLocal);
        Assert.True(FlightAssist.Command(fast, Cube, new Vector3(0, 0, -5), Vector3.Zero, true, Gains).ForceLocal.Z > 0);
        var slow = new RigidBodyState(Vector3.Zero, Quaternion.Identity, new Vector3(0, 0, -1));
        AssertClose(new Vector3(0, 0, -4 * 2 * 6), FlightAssist.Command(slow, Cube, new Vector3(0, 0, -5), Vector3.Zero, false, Gains).ForceLocal);
    }

    [Fact]
    public void Rotation_is_always_damped()
    {
        var spinning = new RigidBodyState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, new Vector3(0, 2, 0));
        foreach (var dampeners in new[] { true, false })
        {
            var c = FlightAssist.Command(spinning, Cube, Vector3.Zero, Vector3.Zero, dampeners, Gains);
            AssertClose(new Vector3(0, -2 * 4, 0), c.TorqueLocal);   // I = 1
        }
        var turn = FlightAssist.Command(new RigidBodyState(), Cube, Vector3.Zero, new Vector3(0.5f, 0, 0), false, Gains);
        AssertClose(new Vector3(0.5f * 4, 0, 0), turn.TorqueLocal);
    }

    [Fact]
    public void Torque_scales_with_inertia()
    {
        var bar = MassProperties.From([(Vector3.Zero, new Vector3(4, 1, 1), 12f)]);   // Ixx 2, Iyy 17
        var c = FlightAssist.Command(new RigidBodyState(), bar, Vector3.Zero, new Vector3(1, 1, 0), true, Gains);
        AssertClose(new Vector3(2 * 4, 17 * 4, 0), c.TorqueLocal);
    }

    [Fact]
    public void Requests_are_capped()
    {
        var gains = new FlightAssistGains(2, 4, 3, 1);
        var c = FlightAssist.Command(new RigidBodyState(), Cube, new Vector3(100, 0, 0), new Vector3(0, 0, 100), true, gains);
        AssertClose(new Vector3(3 * 6, 0, 0), c.ForceLocal);
        AssertClose(new Vector3(0, 0, 1), c.TorqueLocal);
        Assert.True(FlightAssistGains.Default.LinearResponse > 0 && FlightAssistGains.Default.AngularResponse > 0);
    }

    [Fact]
    public void Massless_or_bad_input_gives_nothing_or_finite_output()
    {
        Assert.Equal(default, FlightAssist.Command(new RigidBodyState(), MassProperties.Empty, Vector3.One, Vector3.One, true, Gains));
        var c = FlightAssist.Command(new RigidBodyState(), Cube, new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0), true, Gains);
        AssertClose(Vector3.Zero, c.ForceLocal);
        AssertClose(Vector3.Zero, c.TorqueLocal);
    }

    [Fact]
    public void Closed_loop_with_real_thrusters_reaches_the_requested_speed()
    {
        // Six axis thrusters around a 3×3×3 ship; fly forward (−Z) at 4 m/s for 5 s with dampeners.
        var mass = MassProperties.From(
            from x in Enumerable.Range(0, 3) from y in Enumerable.Range(0, 3) from z in Enumerable.Range(0, 3)
            select (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), Vector3.One, 2f));
        var com = mass.CenterOfMass;
        ThrusterMount[] thrusters =
        [
            new(com + new Vector3(-2, 0, 0), Vector3.UnitX, 200), new(com + new Vector3(2, 0, 0), -Vector3.UnitX, 200),
            new(com + new Vector3(0, -2, 0), Vector3.UnitY, 200), new(com + new Vector3(0, 2, 0), -Vector3.UnitY, 200),
            new(com + new Vector3(0, 0, -2), Vector3.UnitZ, 200), new(com + new Vector3(0, 0, 2), -Vector3.UnitZ, 200),
        ];
        var s = new RigidBodyState(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f), new Vector3(1, 0, 0));
        for (int i = 0; i < 300; i++)
        {
            var cmd = FlightAssist.Command(s, mass, new Vector3(0, 0, -4), Vector3.Zero, true, FlightAssistGains.Default);
            var throttles = ThrustAllocator.Allocate(thrusters, com, cmd.ForceLocal, cmd.TorqueLocal, 1);
            var (force, torque) = ThrustAllocator.Wrench(thrusters, com, throttles);
            s.Step(s.DirectionToWorld(force), s.DirectionToWorld(torque), mass, 1f / 60);
        }
        var local = s.DirectionToLocal(s.LinearVelocity);
        Assert.True(Vector3.Distance(new Vector3(0, 0, -4), local) < 0.05f, $"{local}");
    }
    [Fact]
    public void Holding_forward_flies_straight_on_an_asymmetric_ship()
    {
        // A 5×1×8 slab (centre of mass x = 2.5) with its two rear thrusters at x = 1 and x = 5, a reverse thruster and
        // up/down thrusters, but nothing pushing along X, so there is no force-free way to make a yaw couple. The
        // default gains ask for far more forward push than the two rear thrusters give.
        var mass = MassProperties.From(
            from x in Enumerable.Range(0, 5) from z in Enumerable.Range(0, 8)
            select (new Vector3(x + 0.5f, 0.5f, z + 0.5f), Vector3.One, 2.8f));
        var com = mass.CenterOfMass;
        ThrusterMount[] thrusters =
        [
            new(new(1, 0.5f, 0), Vector3.UnitZ, 200), new(new(5, 0.5f, 0), Vector3.UnitZ, 200),
            new(new(2.5f, 0.5f, 8), -Vector3.UnitZ, 200),
            new(new(2.5f, 0, 4), Vector3.UnitY, 200), new(new(2.5f, 1, 4), -Vector3.UnitY, 200),
        ];
        foreach (var dampeners in new[] { true, false })
        {
            var s = new RigidBodyState();
            for (int i = 0; i < 600; i++)
            {
                var cmd = FlightAssist.Command(s, mass, new Vector3(0, 0, 30), Vector3.Zero, dampeners, FlightAssistGains.Default);
                var throttles = ThrustAllocator.Allocate(thrusters, com, cmd.ForceLocal, cmd.TorqueLocal, 1);
                var (force, torque) = ThrustAllocator.Wrench(thrusters, com, throttles);
                s.Step(s.DirectionToWorld(force), s.DirectionToWorld(torque), mass, 1f / 60);
            }
            var heading = s.DirectionToWorld(Vector3.UnitZ);
            Assert.True(heading.Z > MathF.Cos(2 * MathF.PI / 180), $"dampeners {dampeners}: heading {heading}");
            Assert.True(s.AngularVelocity.Length() < 0.01f, $"dampeners {dampeners}: spin {s.AngularVelocity}");
            Assert.True(s.LinearVelocity.Z > 20, $"dampeners {dampeners}: velocity {s.LinearVelocity}");
        }
    }

    [Fact]
    public void Speed_limit_holds_without_dampeners()
    {
        var limited = Gains with { SpeedLimit = 60 };
        // At the limit along +X, asking for full speed along −Z: no push that adds speed along X.
        var atLimit = new RigidBodyState(Vector3.Zero, Quaternion.Identity, new Vector3(60, 0, 0));
        var turn = FlightAssist.Command(atLimit, Cube, new Vector3(0, 0, -60), Vector3.Zero, false, limited).ForceLocal;
        Assert.True(turn.Z < 0 && turn.X <= 1e-4f, $"{turn}");
        // Above it: braked back.
        var over = new RigidBodyState(Vector3.Zero, Quaternion.Identity, new Vector3(70, 0, 0));
        Assert.True(FlightAssist.Command(over, Cube, Vector3.Zero, Vector3.Zero, false, limited).ForceLocal.X < 0);
        // Legs flown at right angles with dampeners off stay near the limit instead of adding up.
        var mass = Cube;
        var s = new RigidBodyState();
        Vector3[] legs = [new(0, 0, -60), new(60, 0, 0), new(0, 0, 60), new(-60, 0, 0), new(0, 60, 0), new(0, 0, -60)];
        foreach (var leg in legs)
            for (int i = 0; i < 1200; i++)
            {
                var cmd = FlightAssist.Command(s, mass, leg, Vector3.Zero, false, FlightAssistGains.Default with { SpeedLimit = 60 });
                // An ideal ship that can push 5 m/s² any way.
                var force = cmd.ForceLocal.Length() > 5 * mass.Mass ? Vector3.Normalize(cmd.ForceLocal) * 5 * mass.Mass : cmd.ForceLocal;
                s.Step(s.DirectionToWorld(force), Vector3.Zero, mass, 1f / 60);
            }
        Assert.True(s.LinearVelocity.Length() < 61, $"{s.LinearVelocity} ({s.LinearVelocity.Length()} m/s)");
    }
}
