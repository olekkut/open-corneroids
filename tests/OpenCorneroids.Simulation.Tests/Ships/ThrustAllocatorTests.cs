using System.Numerics;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class ThrustAllocatorTests
{
    const float Tolerance = 2e-3f;

    /// <summary>Six thrusters 2 cells out on each axis, each pushing along an axis straight through the origin.</summary>
    static readonly ThrusterMount[] Six =
    [
        new(new(-2, 0, 0), Vector3.UnitX, 100),     // 0: +X
        new(new(2, 0, 0), -Vector3.UnitX, 100),     // 1: −X
        new(new(0, -2, 0), Vector3.UnitY, 100),     // 2: +Y
        new(new(0, 2, 0), -Vector3.UnitY, 100),     // 3: −Y
        new(new(0, 0, -2), Vector3.UnitZ, 100),     // 4: +Z
        new(new(0, 0, 2), -Vector3.UnitZ, 100),     // 5: −Z
    ];

    static void AssertThrottles(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(MathF.Abs(expected[i] - actual[i]) <= Tolerance, $"throttle {i}: expected {expected[i]}, got {actual[i]} ({string.Join(", ", actual)})");
    }

    [Theory]
    [InlineData(50, 0, 0, 0)]
    [InlineData(-30, 0, 0, 1)]
    [InlineData(0, 70, 0, 2)]
    [InlineData(0, -10, 0, 3)]
    [InlineData(0, 0, 100, 4)]
    [InlineData(0, 0, -100, 5)]
    public void Pure_force_fires_only_the_matching_thruster(float x, float y, float z, int which)
    {
        var request = new Vector3(x, y, z);
        var throttles = ThrustAllocator.Allocate(Six, Vector3.Zero, request, Vector3.Zero, 1);
        var expected = new float[6];
        expected[which] = request.Length() / 100;
        AssertThrottles(expected, throttles);
        var (force, torque) = ThrustAllocator.Wrench(Six, Vector3.Zero, throttles);
        Assert.True(Vector3.Distance(request, force) < 0.5f, $"{force}");
        Assert.True(torque.Length() < 1e-3f);
    }

    [Fact]
    public void Diagonal_force_combines_the_axes()
    {
        var throttles = ThrustAllocator.Allocate(Six, Vector3.Zero, new Vector3(30, -40, 0), Vector3.Zero, 1);
        AssertThrottles([0.3f, 0, 0, 0.4f, 0, 0], throttles);
    }

    [Fact]
    public void Too_much_saturates_at_full_throttle()
    {
        var throttles = ThrustAllocator.Allocate(Six, Vector3.Zero, new Vector3(0, 0, 500), Vector3.Zero, 1);
        AssertThrottles([0, 0, 0, 0, 1, 0], throttles);
    }

    [Fact]
    public void Unreachable_direction_gets_zero_throttle()
    {
        ThrusterMount[] onlyForward = [new(new(0, 0, 3), -Vector3.UnitZ, 100)];
        AssertThrottles([0], ThrustAllocator.Allocate(onlyForward, Vector3.Zero, new Vector3(0, 0, 50), Vector3.Zero, 1));
        AssertThrottles([0], ThrustAllocator.Allocate(onlyForward, Vector3.Zero, new Vector3(50, 0, 0), Vector3.Zero, 1));
        AssertThrottles([0.5f], ThrustAllocator.Allocate(onlyForward, Vector3.Zero, new Vector3(0, 0, -50), Vector3.Zero, 1));
    }

    [Fact]
    public void Thrusters_through_the_centre_cannot_make_torque()
    {
        var throttles = ThrustAllocator.Allocate(Six, Vector3.Zero, Vector3.Zero, new Vector3(0, 0, 100), 1);
        AssertThrottles(new float[6], throttles);
    }

    /// <summary>Two thrusters pushing +Z, one cell either side of the centre on X.</summary>
    static readonly ThrusterMount[] Pair =
    [
        new(new(1, 0, 0), Vector3.UnitZ, 100),      // torque (1,0,0)×(0,0,100) = (0,−100,0)
        new(new(-1, 0, 0), Vector3.UnitZ, 100),     // torque (0,+100,0)
    ];

    [Fact]
    public void Offset_pair_turns_by_firing_one_side()
    {
        var plus = ThrustAllocator.Allocate(Pair, Vector3.Zero, Vector3.Zero, new Vector3(0, 50, 0), 1);
        Assert.Equal(0, plus[0]);
        Assert.True(plus[1] > 0.1f, $"{plus[1]}");
        var minus = ThrustAllocator.Allocate(Pair, Vector3.Zero, Vector3.Zero, new Vector3(0, -50, 0), 1);
        Assert.True(minus[0] > 0.1f, $"{minus[0]}");
        Assert.Equal(0, minus[1]);
        // The torque goes the requested way.
        Assert.True(ThrustAllocator.Wrench(Pair, Vector3.Zero, plus).Torque.Y > 0);
    }

    [Fact]
    public void Offset_pair_pushes_straight_by_firing_both_equally()
    {
        AssertThrottles([0.5f, 0.5f], ThrustAllocator.Allocate(Pair, Vector3.Zero, new Vector3(0, 0, 100), Vector3.Zero, 1));
    }

    [Fact]
    public void Off_centre_mass_shifts_the_balance()
    {
        // COM at x = 0.5: the +X thruster has arm 0.5, the −X one 1.5, so a straight push needs 3:1.
        var throttles = ThrustAllocator.Allocate(Pair, new Vector3(0.5f, 0, 0), new Vector3(0, 0, 100), Vector3.Zero, 1);
        AssertThrottles([0.75f, 0.25f], throttles);
    }

    [Fact]
    public void Full_rcs_reaches_combined_force_and_torque()
    {
        // Pairs of thrusters on every axis, offset so each axis can both push and turn.
        var list = new List<ThrusterMount>();
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            var across = axis == Vector3.UnitX ? Vector3.UnitY : axis == Vector3.UnitY ? Vector3.UnitZ : Vector3.UnitX;
            var other = Vector3.Cross(axis, across);
            foreach (var sign in new[] { 1f, -1f })
                foreach (var offset in new[] { across, -across, other, -other })
                    list.Add(new ThrusterMount(offset * 2 - axis * sign * 2, axis * sign, 100));
        }
        var force = new Vector3(40, -20, 60);
        var torque = new Vector3(30, 50, -40);
        var throttles = ThrustAllocator.Allocate(list, Vector3.Zero, force, torque, 1);
        Assert.All(throttles, t => Assert.InRange(t, 0, 1));
        var achieved = ThrustAllocator.Wrench(list, Vector3.Zero, throttles);
        Assert.True(Vector3.Distance(force, achieved.Force) < 1f, $"force {achieved.Force}");
        Assert.True(Vector3.Distance(torque, achieved.Torque) < 1f, $"torque {achieved.Torque}");
    }

    [Fact]
    public void Lower_availability_needs_more_throttle()
    {
        var request = new Vector3(40, 0, 0);
        AssertThrottles([0.4f, 0, 0, 0, 0, 0], ThrustAllocator.Allocate(Six, Vector3.Zero, request, Vector3.Zero, 1));
        AssertThrottles([0.8f, 0, 0, 0, 0, 0], ThrustAllocator.Allocate(Six, Vector3.Zero, request, Vector3.Zero, 0.5f));
        AssertThrottles([1, 0, 0, 0, 0, 0], ThrustAllocator.Allocate(Six, Vector3.Zero, request, Vector3.Zero, 0.25f));
        AssertThrottles(new float[6], ThrustAllocator.Allocate(Six, Vector3.Zero, request, Vector3.Zero, 0));
        AssertThrottles(new float[6], ThrustAllocator.Allocate(Six, Vector3.Zero, request, Vector3.Zero, float.NaN));
        var halfPower = ThrustAllocator.Allocate(Six, Vector3.Zero, request, Vector3.Zero, 0.5f);
        Assert.True(Vector3.Distance(request, ThrustAllocator.Wrench(Six, Vector3.Zero, halfPower, 0.5f).Force) < 0.5f);
    }

    [Fact]
    public void Same_input_gives_the_same_throttles()
    {
        var a = ThrustAllocator.Allocate(Six, new Vector3(0.1f, 0, 0), new Vector3(10, 20, -30), new Vector3(1, 2, 3), 0.7f);
        var b = ThrustAllocator.Allocate(Six, new Vector3(0.1f, 0, 0), new Vector3(10, 20, -30), new Vector3(1, 2, 3), 0.7f);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Edge_cases_are_safe()
    {
        Assert.Empty(ThrustAllocator.Allocate([], Vector3.Zero, Vector3.One, Vector3.One, 1));
        AssertThrottles(new float[6], ThrustAllocator.Allocate(Six, Vector3.Zero, new Vector3(float.NaN, 0, 0), Vector3.Zero, 1));
        ThrusterMount[] broken = [new(Vector3.Zero, Vector3.UnitX, 0), new(Vector3.Zero, Vector3.UnitX, float.NaN), new(Vector3.Zero, Vector3.UnitX, 10)];
        AssertThrottles([0, 0, 0.5f], ThrustAllocator.Allocate(broken, Vector3.Zero, new Vector3(5, 0, 0), Vector3.Zero, 1));
        var span = new float[7];
        span[6] = 9;
        ThrustAllocator.Allocate(Six, Vector3.Zero, new Vector3(50, 0, 0), Vector3.Zero, 1, span);
        Assert.Equal(9, span[6]);
        Assert.Throws<ArgumentException>(() => ThrustAllocator.Allocate(Six, Vector3.Zero, Vector3.One, Vector3.One, 1, new float[3]));
    }

    [Fact]
    public void Reachable_wrenches_are_reached_almost_exactly()
    {
        // Random axis-aligned ships; each request is the wrench of some in-range throttles, so zero error is possible.
        var random = new Random(1234);
        Vector3[] axes = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        for (int trial = 0; trial < 3000; trial++)
        {
            int n = random.Next(4, 20);
            var list = new ThrusterMount[n];
            var target = new float[n];
            for (int i = 0; i < n; i++)
            {
                var position = new Vector3(random.Next(-8, 9), random.Next(-8, 9), random.Next(-8, 9));
                list[i] = new ThrusterMount(position, axes[random.Next(6)], random.Next(100, 401));
                target[i] = 0.1f + 0.8f * (float)random.NextDouble();
            }
            var (force, torque) = ThrustAllocator.Wrench(list, Vector3.Zero, target);
            var achieved = ThrustAllocator.Wrench(list, Vector3.Zero, ThrustAllocator.Allocate(list, Vector3.Zero, force, torque, 1));
            float scale = 1 + force.Length() + torque.Length();
            float error = (Vector3.Distance(force, achieved.Force) + Vector3.Distance(torque, achieved.Torque)) / scale;
            Assert.True(error < 1e-3f, $"trial {trial}: force {force} -> {achieved.Force}, torque {torque} -> {achieved.Torque}");
        }
    }

    [Fact]
    public void Turning_takes_priority_over_an_unreachable_push()
    {
        // Two rear thrusters unevenly placed about the centre of mass (arms −1.5 and +2.5); far more push is requested
        // than they give. Both at full throttle would spin the ship; the straight-flying answer is 0.6 : 1.
        ThrusterMount[] rear = [new(new(-1.5f, 0, -4), Vector3.UnitZ, 200), new(new(2.5f, 0, -4), Vector3.UnitZ, 200)];
        var throttles = ThrustAllocator.Allocate(rear, Vector3.Zero, new Vector3(0, 0, 3360), Vector3.Zero, 1);
        var (force, torque) = ThrustAllocator.Wrench(rear, Vector3.Zero, throttles);
        Assert.True(torque.Length() < 1f, $"torque {torque} ({string.Join(", ", throttles)})");
        Assert.True(force.Z > 300, $"force {force}");
    }

    [Fact]
    public void Rear_thrust_above_the_centre_of_mass_still_pushes()
    {
        // Both rear thrusters sit a cell above the centre of mass and nothing can trim the pitch they make. Torque
        // first alone would leave them near zero (no zero-torque push exists); the ship should accelerate anyway.
        ThrusterMount[] rear = [new(new(-1, 1, -4), Vector3.UnitZ, 200), new(new(1, 1, -4), Vector3.UnitZ, 200)];
        var throttles = ThrustAllocator.Allocate(rear, Vector3.Zero, new Vector3(0, 0, 3000), Vector3.Zero, 1);
        var (force, _) = ThrustAllocator.Wrench(rear, Vector3.Zero, throttles);
        Assert.True(force.Z > 350, $"force {force} ({string.Join(", ", throttles)})");
        // Both alike: the side-to-side balance still holds.
        Assert.True(MathF.Abs(throttles[0] - throttles[1]) < 1e-3f, string.Join(", ", throttles));
    }

    [Fact]
    public void A_partial_zero_torque_push_is_kept_without_blending()
    {
        // The lopsided pair can push straight at 0.6 : 1, well over half of what it gives at all: no fallback.
        ThrusterMount[] rear = [new(new(-1.5f, 0, -4), Vector3.UnitZ, 200), new(new(2.5f, 0, -4), Vector3.UnitZ, 200)];
        var throttles = ThrustAllocator.Allocate(rear, Vector3.Zero, new Vector3(0, 0, 3360), Vector3.Zero, 1);
        AssertThrottles([1, 0.6f], throttles);
    }
}

