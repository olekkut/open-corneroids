using System.Numerics;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Shapes;

/// <summary>A door's moving state. <see cref="Open"/> is the only part worth saving; see <see cref="Resting"/>.</summary>
/// <param name="Open">0 = closed, 1 = fully open.</param>
/// <param name="Opening">Where the door is heading: true towards open, false towards closed.</param>
/// <param name="HeldOpenMs">How long the door has been fully open, for the auto-close.</param>
public readonly record struct DoorState(float Open, bool Opening, float HeldOpenMs)
{
    public static DoorState Closed => default;

    /// <summary>
    /// The state to resume from a saved open fraction. The save keeps no direction, so: a fully open door stays open
    /// (and auto-closes as usual if its time says so); a part-open door finishes closing, because closed is where every
    /// door ends up and guessing "opening" would leave a door the player had closed standing open for good when it
    /// never auto-closes. Either way a world never loads with a door stuck half way (ours).
    /// </summary>
    public static DoorState Resting(float open)
    {
        var clamped = float.IsFinite(open) ? Math.Clamp(open, 0f, 1f) : 0f;
        return new DoorState(clamped, clamped >= 1, 0);
    }
}

/// <summary>
/// How doors move. The itemset gives <c>direction</c>, <c>distance</c>, <c>speed</c> and <c>time</c>; reading speed as
/// cells per second and time as milliseconds held open before closing again is ours (tunable; original behaviour
/// unknown). Vanilla blast doors (distance 2, speed 4, time 2000) then open in half a second and close after 2 s.
/// </summary>
public static class DoorMotion
{
    /// <summary>Used when a door's speed is zero, negative or not a number, so a typo can't freeze it. Cells/s, tunable.</summary>
    public const float FallbackSpeed = 4f;

    /// <summary>
    /// How much of its travel a door covers per second. The travel is the distance's magnitude: a negative distance
    /// slides the model along <c>-direction</c> (see <see cref="Offset"/>), at the same speed as a positive one (ours).
    /// </summary>
    public static float FractionPerSecond(DoorComponent door)
    {
        ArgumentNullException.ThrowIfNull(door);
        var speed = float.IsFinite(door.Speed) && door.Speed > 0 ? door.Speed : FallbackSpeed;
        var travel = float.IsFinite(door.Distance) ? MathF.Abs(door.Distance) : 0;
        // No travel: nothing to animate, so arrive at once.
        return travel > 0 ? speed / travel : float.PositiveInfinity;
    }

    /// <summary>Moves <paramref name="open"/> toward <paramref name="target"/> (both clamped to 0..1) without overshooting.</summary>
    public static float Step(float open, float target, DoorComponent door, float deltaSeconds)
    {
        open = float.IsFinite(open) ? Math.Clamp(open, 0f, 1f) : 0f;
        target = float.IsFinite(target) ? Math.Clamp(target, 0f, 1f) : 0f;
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0) return open;
        var step = FractionPerSecond(door) * deltaSeconds;
        return open < target ? Math.Min(target, open + step) : Math.Max(target, open - step);
    }

    /// <summary>
    /// One tick: move toward the target, and once the door has stood fully open for <see cref="DoorComponent.TimeMs"/>
    /// start closing it. A time of zero or less means the door stays open until toggled (ours).
    /// </summary>
    public static DoorState Advance(DoorState state, DoorComponent door, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(door);
        var open = Step(state.Open, state.Opening ? 1 : 0, door, deltaSeconds);
        if (!state.Opening || open < 1) return new DoorState(open, state.Opening, 0);

        var held = state.HeldOpenMs + (float.IsFinite(deltaSeconds) && deltaSeconds > 0 ? deltaSeconds * 1000 : 0);
        return door.TimeMs > 0 && held >= door.TimeMs ? new DoorState(open, false, 0) : new DoorState(open, true, held);
    }

    /// <summary>The player used the door: reverse its direction from wherever it is.</summary>
    public static DoorState Toggle(DoorState state) => state with { Opening = !state.Opening, HeldOpenMs = 0 };

    /// <summary>
    /// Local displacement of the model: <c>direction × distance × open</c>, for <see cref="BlockShape.ModelBox"/>. Taken
    /// literally, so a negative distance moves it the other way; a non-finite one does not move it.
    /// </summary>
    public static Vector3 Offset(float open, DoorComponent door)
    {
        ArgumentNullException.ThrowIfNull(door);
        if (!float.IsFinite(open) || !float.IsFinite(door.Distance)) return Vector3.Zero;
        var d = door.Direction;
        if (!float.IsFinite(d.X) || !float.IsFinite(d.Y) || !float.IsFinite(d.Z)) return Vector3.Zero;
        return d * door.Distance * Math.Clamp(open, 0f, 1f);
    }
}
