using System.Numerics;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Simulation.Tests.Items;

public class FloatingMotionTests
{
    static (Vector3 Position, Vector3 Velocity) Run(Vector3 velocity, double drag, double seconds, double frame)
    {
        var state = (Position: Vector3.Zero, Velocity: velocity);
        for (double t = 0; t < seconds - 1e-9; t += frame)
            state = FloatingMotion.Step(state.Position, state.Velocity, drag, Math.Min(frame, seconds - t));
        return state;
    }

    [Fact]
    public void Speed_falls_by_the_drag_factor_every_sixtieth_of_a_second()
    {
        var (_, velocity) = FloatingMotion.Step(Vector3.Zero, new Vector3(3, 0, 0), 0.98, 10 * FloatingMotion.TickSeconds);

        Assert.Equal(3 * Math.Pow(0.98, 10), velocity.X, 4);
    }

    [Fact]
    public void A_stack_thrown_at_three_metres_a_second_stops_out_of_pickup_reach()
    {
        var (position, velocity) = Run(new Vector3(0, 0, -3), 0.98, 30, 1.0 / 60);

        Assert.Equal(Vector3.Zero, velocity);
        Assert.InRange(-position.Z, 2.4f, 2.5f);
        Assert.True(-position.Z > 1.5f, "beyond sv_item_pickup_reach");
        Assert.Equal(-position.Z, FloatingMotion.StoppingDistance(3, 0.98), 2);
    }

    [Theory]
    [InlineData(1.0 / 15)]
    [InlineData(1.0 / 144)]
    public void The_path_is_the_same_at_any_frame_rate(double frame)
    {
        var reference = Run(new Vector3(2, 1, 0), 0.98, 1.5, 1.0 / 60);
        var other = Run(new Vector3(2, 1, 0), 0.98, 1.5, frame);

        Assert.True(Vector3.Distance(reference.Position, other.Position) < 1e-3f);
        Assert.True(Vector3.Distance(reference.Velocity, other.Velocity) < 1e-3f);
    }

    [Fact]
    public void No_drag_keeps_the_speed_and_full_drag_stops_at_once()
    {
        var free = FloatingMotion.Step(Vector3.Zero, Vector3.UnitX, 1, 2);
        Assert.Equal((new Vector3(2, 0, 0), Vector3.UnitX), free);

        var stuck = FloatingMotion.Step(Vector3.One, Vector3.UnitX, 0, 2);
        Assert.Equal((Vector3.One, Vector3.Zero), stuck);
    }

    [Fact]
    public void An_item_at_rest_stays_put()
    {
        Assert.Equal((Vector3.One, Vector3.Zero), FloatingMotion.Step(Vector3.One, Vector3.Zero, 0.98, 1));
    }
}
