using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Shapes;

public class DoorMotionTests
{
    // The real vanilla blast door and DarthNox bay door components.
    static readonly DoorComponent Blast = new(Vector3.UnitY, 2, 4, 2000);
    static readonly DoorComponent Bay = new(Vector3.UnitY, 11, 4, 20000);

    static DoorState Run(DoorState state, DoorComponent door, float seconds, float step = 1 / 60f)
    {
        for (float t = 0; t < seconds - 1e-6f; t += step) state = DoorMotion.Advance(state, door, step);
        return state;
    }

    [Fact]
    public void Offset_is_direction_times_distance_times_open()
    {
        Assert.Equal(Vector3.Zero, DoorMotion.Offset(0, Blast));
        Assert.Equal(new Vector3(0, 1, 0), DoorMotion.Offset(0.5f, Blast));
        Assert.Equal(new Vector3(0, 2, 0), DoorMotion.Offset(1, Blast));
        Assert.Equal(new Vector3(0, 11, 0), DoorMotion.Offset(1, Bay));
        Assert.Equal(new Vector3(0, 2, 0), DoorMotion.Offset(7, Blast));
        Assert.Equal(Vector3.Zero, DoorMotion.Offset(float.NaN, Blast));
        Assert.Equal(Vector3.Zero, DoorMotion.Offset(1, Blast with { Direction = new Vector3(float.NaN, 0, 0) }));
    }

    [Fact]
    public void Step_moves_at_speed_in_cells_per_second_without_overshooting()
    {
        // 4 cells/s over 2 cells: half the travel per 0.25 s.
        Assert.Equal(0.5f, DoorMotion.Step(0, 1, Blast, 0.25f), 5);
        Assert.Equal(1f, DoorMotion.Step(0.9f, 1, Blast, 1));
        Assert.Equal(0.5f, DoorMotion.Step(1, 0, Blast, 0.25f), 5);
        Assert.Equal(0f, DoorMotion.Step(0.1f, 0, Blast, 1));
        Assert.Equal(0.3f, DoorMotion.Step(0.3f, 1, Blast, 0));
        Assert.Equal(0.3f, DoorMotion.Step(0.3f, 1, Blast, float.NaN));
        Assert.Equal(4f / 11, DoorMotion.FractionPerSecond(Bay), 5);
    }

    [Fact]
    public void Bad_speed_or_distance_does_not_freeze_the_door()
    {
        Assert.Equal(DoorMotion.FallbackSpeed / 2, DoorMotion.FractionPerSecond(Blast with { Speed = 0 }));
        Assert.Equal(DoorMotion.FallbackSpeed / 2, DoorMotion.FractionPerSecond(Blast with { Speed = float.NaN }));
        Assert.Equal(1f, DoorMotion.Step(0, 1, Blast with { Distance = 0 }, 0.001f));
    }

    [Fact]
    public void Door_opens_holds_then_closes_by_itself()
    {
        var state = DoorMotion.Toggle(DoorState.Closed);
        Assert.True(state.Opening);

        state = Run(state, Blast, 0.5f);
        Assert.Equal(1f, state.Open, 3);
        Assert.True(state.Opening);

        state = Run(state, Blast, 1.9f);
        Assert.True(state.Opening, "still held open before 2 s");
        state = Run(state, Blast, 0.2f);
        Assert.False(state.Opening, "auto-close after 2 s");

        state = Run(state, Blast, 0.6f);
        Assert.Equal(0f, state.Open);
        Assert.False(state.Opening);
    }

    [Fact]
    public void Bay_door_takes_its_distance_over_speed()
    {
        var state = Run(DoorMotion.Toggle(DoorState.Closed), Bay, 2.5f);
        Assert.InRange(state.Open, 0.85f, 0.95f);
        state = Run(state, Bay, 0.3f);
        Assert.Equal(1f, state.Open);
    }

    [Fact]
    public void Zero_time_keeps_the_door_open_until_toggled()
    {
        var door = Blast with { TimeMs = 0 };
        var state = Run(DoorMotion.Toggle(DoorState.Closed), door, 30);
        Assert.Equal(1f, state.Open);
        Assert.True(state.Opening);
        state = Run(DoorMotion.Toggle(state), door, 1);
        Assert.Equal(0f, state.Open);
    }

    [Fact]
    public void Toggle_mid_way_reverses()
    {
        var state = Run(DoorMotion.Toggle(DoorState.Closed), Blast, 0.25f);
        Assert.InRange(state.Open, 0.4f, 0.6f);
        var before = state.Open;
        state = DoorMotion.Advance(DoorMotion.Toggle(state), Blast, 0.05f);
        Assert.True(state.Open < before);
    }

    [Fact]
    public void A_door_saved_while_closing_does_not_reopen_on_load()
    {
        // "Stays open until toggled": the player closed it, the world was saved half way, then loaded.
        var door = Blast with { TimeMs = 0 };
        var open = Run(DoorMotion.Toggle(DoorState.Closed), door, 30);
        var closing = Run(DoorMotion.Toggle(open), door, 0.25f);
        Assert.InRange(closing.Open, 0.4f, 0.6f);

        var loaded = Run(DoorState.Resting(closing.Open), door, 30);
        Assert.Equal(0f, loaded.Open);
    }

    [Fact]
    public void A_door_saved_fully_open_stays_open_and_still_auto_closes()
    {
        Assert.Equal(1f, Run(DoorState.Resting(1), Blast with { TimeMs = 0 }, 30).Open);
        var state = Run(DoorState.Resting(1), Blast, 1.9f);
        Assert.Equal(1f, state.Open);
        Assert.Equal(0f, Run(state, Blast, 1).Open);
    }

    [Fact]
    public void Negative_distance_slides_the_other_way_at_the_normal_speed()
    {
        var reversed = Blast with { Distance = -2 };
        Assert.Equal(DoorMotion.FractionPerSecond(Blast), DoorMotion.FractionPerSecond(reversed));
        Assert.Equal(0.5f, DoorMotion.Step(0, 1, reversed, 0.25f), 5);
        Assert.Equal(new Vector3(0, -1, 0), DoorMotion.Offset(0.5f, reversed));
        Assert.Equal(new Vector3(0, -2, 0), DoorMotion.Offset(1, reversed));
    }

    [Fact]
    public void Resting_resumes_a_saved_fraction()
    {
        Assert.Equal(DoorState.Closed, DoorState.Resting(0));
        // The save keeps no direction, so a part-open door finishes closing, the state every door returns to.
        Assert.Equal(new DoorState(0.5f, false, 0), DoorState.Resting(0.5f));
        Assert.Equal(new DoorState(1, true, 0), DoorState.Resting(3));
        Assert.Equal(DoorState.Closed, DoorState.Resting(float.NaN));
    }
}
