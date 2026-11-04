using System.Numerics;
using OpenCorneroids.Simulation.Physics;
using static OpenCorneroids.Simulation.Physics.AstronautMotion;

namespace OpenCorneroids.Simulation.Tests.Physics;

/// <summary>The original's rules: the original's movement numbers.</summary>
public class AstronautMotionTests
{
    static readonly Vector3 Forward = -Vector3.UnitZ;
    const float Dt = 0.008f;   // the original's update step

    static Vector3 Run(Vector3 velocity, AstronautMotion.Input input, float seconds)
    {
        for (float t = 0; t < seconds; t += Dt) velocity = Step(velocity, input, Dt);
        return velocity;
    }

    static AstronautMotion.Input Keys(Vector3 keys, bool forward = false, bool boost = false) => new(keys, Forward, forward, boost);

    [Fact]
    public void A_key_accelerates_at_five_and_settles_at_four()
    {
        var after = Step(Vector3.Zero, Keys(Forward, forward: true), 0.1f);
        Assert.Equal(0.5f, after.Length(), 4);

        var cruising = Run(Vector3.Zero, Keys(Forward, forward: true), 3);
        Assert.Equal(SpeedLimit, cruising.Length(), 3);
    }

    [Fact]
    public void Keys_add_up_so_a_diagonal_pushes_harder()
    {
        var after = Step(Vector3.Zero, Keys(Forward + Vector3.UnitX, forward: true), 0.1f);
        Assert.Equal(0.5f * MathF.Sqrt(2), after.Length(), 4);
    }

    [Fact]
    public void Boost_pushes_at_ten_along_the_view_without_the_forward_key_and_creeps_past_eight()
    {
        var after = Step(Vector3.Zero, Keys(Vector3.Zero, boost: true), 0.1f);
        Assert.Equal(new Vector3(0, 0, -1f), after);

        // With W also held the boost replaces it rather than adding to it.
        Assert.Equal(after, Step(Vector3.Zero, Keys(Forward, forward: true, boost: true), 0.1f));

        // Up to 8 at 10 m/s², then the 8 m/s² over-limit drag leaves 2 m/s² (as the spec says): 0.8 s + 2.2 s × 2.
        Assert.Equal(BoostSpeedLimit + 2.2f * 2, Run(Vector3.Zero, Keys(Vector3.Zero, boost: true), 3).Length(), 1);
    }

    [Fact]
    public void Letting_go_of_boost_bleeds_back_to_the_plain_limit()
    {
        var fast = new Vector3(0, 0, -8);
        var after = Step(fast, Keys(Forward, forward: true), 0.25f);
        Assert.Equal(7.25f, after.Length(), 3);   // 8 + 5·0.25 of thrust, then −8·0.25 over the limit
        Assert.Equal(SpeedLimit, Run(fast, Keys(Forward, forward: true), 2).Length(), 3);
    }

    [Fact]
    public void With_no_key_held_the_speed_falls_by_six_a_second_to_a_stop()
    {
        var after = Step(new Vector3(3, 0, 0), Keys(Vector3.Zero), 0.25f);
        Assert.Equal(1.5f, after.X, 4);
        Assert.Equal(Vector3.Zero, Step(new Vector3(0.01f, 0, 0), Keys(Vector3.Zero), Dt));
    }

    [Fact]
    public void Nothing_goes_faster_than_twenty()
    {
        Assert.Equal(HardSpeedLimit, Step(new Vector3(0, 0, -50), Keys(Vector3.Zero, boost: true), Dt).Length(), 3);
    }
}
