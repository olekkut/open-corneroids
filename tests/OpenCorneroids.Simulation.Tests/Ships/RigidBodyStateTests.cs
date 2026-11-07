using System.Numerics;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class RigidBodyStateTests
{
    const float Frame = 1f / 60f;

    static void AssertClose(Vector3 expected, Vector3 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");

    static void AssertFinite(RigidBodyState s)
    {
        foreach (var f in new[] { s.Position.X, s.Position.Y, s.Position.Z, s.LinearVelocity.X, s.LinearVelocity.Y,
                     s.LinearVelocity.Z, s.AngularVelocity.X, s.AngularVelocity.Y, s.AngularVelocity.Z, s.Orientation.X,
                     s.Orientation.Y, s.Orientation.Z, s.Orientation.W })
            Assert.True(float.IsFinite(f), $"non-finite state: {s.Position} {s.LinearVelocity} {s.AngularVelocity} {s.Orientation}");
    }

    /// <summary>Unit cube of mass 6: I = 1 on every axis.</summary>
    static readonly MassProperties Cube = MassProperties.From([(Vector3.Zero, Vector3.One, 6f)]);

    [Fact]
    public void Constant_force_gives_the_expected_velocity_and_distance()
    {
        var s = new RigidBodyState();
        for (int i = 0; i < 60; i++) s.Step(new Vector3(12, 0, 0), Vector3.Zero, Cube, Frame);
        AssertClose(new Vector3(2, 0, 0), s.LinearVelocity);               // a = 12/6 for 1 s
        // Semi-implicit Euler: x = a·h²·(1 + 2 + … + 60) = 2 · 1830 / 3600; the exact ½at² is 1.
        AssertClose(new Vector3(2f * 1830 / 3600, 0, 0), s.Position);
        Assert.Equal(Quaternion.Identity, s.Orientation);
    }

    [Fact]
    public void Pure_torque_about_a_principal_axis_spins_only_that_axis()
    {
        var s = new RigidBodyState();
        for (int i = 0; i < 60; i++) s.Step(Vector3.Zero, new Vector3(0, 0, 3), Cube, Frame);
        AssertClose(new Vector3(0, 0, 3), s.AngularVelocity);              // α = 3/1 for 1 s
        AssertClose(Vector3.Zero, s.LinearVelocity);
        // Turned by Σ α·h²·k = 3·1830/3600 rad about +Z.
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 3f * 1830 / 3600);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, s.Orientation)) > 0.99999f, $"{s.Orientation} vs {expected}");
    }

    [Fact]
    public void Heavier_axis_turns_slower()
    {
        // 4×1×1 bar, mass 12: Ixx = 2, Iyy = Izz = 17.
        var bar = MassProperties.From([(Vector3.Zero, new Vector3(4, 1, 1), 12f)]);
        var aboutX = new RigidBodyState();
        var aboutY = new RigidBodyState();
        aboutX.Step(Vector3.Zero, new Vector3(34, 0, 0), bar, Frame);
        aboutY.Step(Vector3.Zero, new Vector3(0, 34, 0), bar, Frame);
        AssertClose(new Vector3(17 * Frame, 0, 0), aboutX.AngularVelocity, 1e-4f);
        AssertClose(new Vector3(0, 2 * Frame, 0), aboutY.AngularVelocity, 1e-4f);
    }

    [Fact]
    public void Forces_are_world_space_whatever_the_orientation()
    {
        var s = new RigidBodyState(Vector3.Zero, Quaternion.CreateFromYawPitchRoll(0.7f, -0.4f, 1.1f));
        for (int i = 0; i < 60; i++) s.Step(new Vector3(0, 6, 0), Vector3.Zero, Cube, Frame);
        AssertClose(new Vector3(0, 1, 0), s.LinearVelocity);
    }

    [Fact]
    public void Long_frames_are_sub_stepped_so_the_result_matches_small_frames()
    {
        var bar = MassProperties.From([(Vector3.Zero, new Vector3(3, 1, 2), 5f)]);
        var big = new RigidBodyState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, new Vector3(0.5f, 1, -0.3f));
        var small = big.Clone();
        var force = new Vector3(1, -2, 3);
        var torque = new Vector3(0.4f, 0.1f, -0.2f);
        for (int i = 0; i < 6; i++) big.Step(force, torque, bar, 0.1f);
        for (int i = 0; i < 36; i++) small.Step(force, torque, bar, Frame);
        AssertClose(small.Position, big.Position, 1e-4f);
        AssertClose(small.LinearVelocity, big.LinearVelocity, 1e-4f);
        AssertClose(small.AngularVelocity, big.AngularVelocity, 1e-3f);
        Assert.True(MathF.Abs(Quaternion.Dot(small.Orientation, big.Orientation)) > 0.99999f);
    }

    [Fact]
    public void Free_tumble_conserves_energy_and_momentum_without_damping()
    {
        // An irregular box spinning about a non-principal axis, so it really tumbles.
        var body = MassProperties.From([(Vector3.Zero, new Vector3(1, 2, 3), 1f), (new Vector3(1, 1, 0), Vector3.One, 2f)]);
        var s = new RigidBodyState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, new Vector3(1, 2, 0.5f));
        float energy = s.KineticEnergy(body);
        var momentum = body.ApplyWorldInertia(s.Orientation, s.AngularVelocity);
        for (int i = 0; i < 1000; i++) s.Step(Vector3.Zero, Vector3.Zero, body, Frame);
        AssertFinite(s);
        Assert.InRange(s.KineticEnergy(body), energy * 0.99f, energy * 1.01f);
        AssertClose(momentum, body.ApplyWorldInertia(s.Orientation, s.AngularVelocity), momentum.Length() * 1e-3f);
        Assert.InRange(s.Orientation.Length(), 0.9999f, 1.0001f);
        // It did tumble: the spin axis moved away from where it started.
        Assert.True(Vector3.Distance(Vector3.Normalize(s.AngularVelocity), Vector3.Normalize(new Vector3(1, 2, 0.5f))) > 0.01f);
    }

    [Fact]
    public void Damping_decays_exponentially_per_second()
    {
        var s = new RigidBodyState(Vector3.Zero, Quaternion.Identity, new Vector3(10, 0, 0), new Vector3(0, 4, 0));
        for (int i = 0; i < 60; i++) s.Step(Vector3.Zero, Vector3.Zero, Cube, Frame, linearDamping: 0.5f, angularDamping: 1f);
        AssertClose(new Vector3(10 * MathF.Exp(-0.5f), 0, 0), s.LinearVelocity);
        AssertClose(new Vector3(0, 4 * MathF.Exp(-1f), 0), s.AngularVelocity);
    }

    [Fact]
    public void Massless_body_coasts_and_never_turns_nan()
    {
        var s = new RigidBodyState(Vector3.Zero, Quaternion.Identity, new Vector3(1, 0, 0), new Vector3(0, 1, 0));
        for (int i = 0; i < 60; i++) s.Step(new Vector3(1e9f, 0, 0), new Vector3(0, 0, 1e9f), MassProperties.Empty, Frame);
        AssertFinite(s);
        AssertClose(new Vector3(1, 0, 0), s.LinearVelocity);
        AssertClose(new Vector3(1, 0, 0), s.Position);
        AssertClose(new Vector3(0, 1, 0), s.AngularVelocity);

        var point = MassProperties.From([(Vector3.Zero, Vector3.Zero, 1f)]);
        s.Step(Vector3.Zero, new Vector3(5, 5, 5), point, Frame);
        AssertFinite(s);
    }

    [Fact]
    public void Bad_inputs_do_nothing()
    {
        var s = new RigidBodyState(Vector3.One, Quaternion.Identity, Vector3.UnitX);
        s.Step(new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0), Cube, Frame);
        AssertFinite(s);
        AssertClose(Vector3.UnitX, s.LinearVelocity);
        var before = s.Position;
        s.Step(Vector3.One, Vector3.One, Cube, 0);
        s.Step(Vector3.One, Vector3.One, Cube, -1);
        s.Step(Vector3.One, Vector3.One, Cube, float.NaN);
        Assert.Equal(before, s.Position);
    }

    [Fact]
    public void Huge_frame_is_capped()
    {
        var s = new RigidBodyState(Vector3.Zero, Quaternion.Identity, Vector3.UnitX);
        s.Step(Vector3.Zero, Vector3.Zero, Cube, 100);
        AssertClose(new Vector3(RigidBodyState.MaxStep, 0, 0), s.Position);
    }

    [Fact]
    public void Local_and_world_points_round_trip_about_the_centre_of_mass()
    {
        var com = new Vector3(1.5f, 0.5f, 2);
        var s = new RigidBodyState(new Vector3(10, 20, 30), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        AssertClose(new Vector3(10, 20, 30), s.ToWorld(com, com));
        // Local +X turned 90° about Y points to world −Z.
        AssertClose(new Vector3(10, 20, 29), s.ToWorld(com + Vector3.UnitX, com));
        var p = new Vector3(-3, 4, 7);
        AssertClose(p, s.ToLocal(s.ToWorld(p, com), com));
        AssertClose(-Vector3.UnitZ, s.DirectionToWorld(Vector3.UnitX));
        AssertClose(Vector3.UnitX, s.DirectionToLocal(-Vector3.UnitZ));
    }

    [Fact]
    public void Original_step_caps_linear_and_angular_speeds()
    {
        var s = new RigidBodyState();
        s.StepOriginal(new Vector3(100_000, 0, 0), new Vector3(0, 100_000, 0), Cube, Frame, thrustersFiring: true);
        Assert.Equal(RigidBodyState.MaxLinearSpeed, s.LinearVelocity.Length(), 1e-4f);
        Assert.Equal(RigidBodyState.MaxAngularSpeed, s.AngularVelocity.Length(), 1e-4f);
    }

    [Fact]
    public void Original_step_coasts_down_linearly_and_stops_at_thresholds_when_idle()
    {
        var s = new RigidBodyState(Vector3.Zero, Quaternion.Identity, new Vector3(1f, 0, 0), new Vector3(0, 0.1f, 0));
        // 1 second of coasting without thrusters: linear drops by 0.25 u/s, angular drops by 0.03 rad/s.
        for (int i = 0; i < 60; i++)
            s.StepOriginal(Vector3.Zero, Vector3.Zero, Cube, Frame, thrustersFiring: false);
        AssertClose(new Vector3(0.75f, 0, 0), s.LinearVelocity, 1e-3f);
        AssertClose(new Vector3(0, 0.07f, 0), s.AngularVelocity, 1e-3f);

        // While thrusters are firing, no coasting slowdown is applied.
        var firing = s.Clone();
        for (int i = 0; i < 60; i++)
            firing.StepOriginal(Vector3.Zero, Vector3.Zero, Cube, Frame, thrustersFiring: true);
        AssertClose(s.LinearVelocity, firing.LinearVelocity, 1e-4f);
        AssertClose(s.AngularVelocity, firing.AngularVelocity, 1e-4f);

        // Below the stop thresholds (0.025 linear, 0.003 angular), velocity snaps to zero.
        var nearStop = new RigidBodyState(Vector3.Zero, Quaternion.Identity, new Vector3(0.025f, 0, 0), new Vector3(0, 0.003f, 0));
        nearStop.StepOriginal(Vector3.Zero, Vector3.Zero, Cube, Frame, thrustersFiring: false);
        Assert.Equal(Vector3.Zero, nearStop.LinearVelocity);
        Assert.Equal(Vector3.Zero, nearStop.AngularVelocity);
    }
}
