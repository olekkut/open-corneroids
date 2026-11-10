using System.Numerics;

namespace OpenCorneroids.Simulation.Items;

/// <summary>
/// How a floating item moves once thrown: in a straight line, slowing down by a fixed factor per tick, as in the
/// original (×0.98, <c>sv_item_drag</c>). The original applies the factor once per update; its update rate is not
/// known for certain, so the factor is taken per 1/60 s and applied smoothly, which makes the motion the same at any
/// frame rate. A thrown stack at 3 m/s comes to rest about 2.5 m away, out of pickup reach.
/// </summary>
public static class FloatingMotion
{
    /// <summary>The tick the drag factor is given per, in seconds. UNCERTAIN in the original; 1/60 s assumed.</summary>
    public const double TickSeconds = 1.0 / 60;

    /// <summary>Below this speed, in m/s, an item counts as stopped.</summary>
    public const float RestSpeed = 1e-3f;

    /// <summary>
    /// Moves an item for <paramref name="seconds"/>: speed falls as <c>drag^(seconds / tick)</c> and the position
    /// follows the exact distance covered while it falls. <paramref name="dragPerTick"/> is clamped to 0..1: 1 never
    /// slows down, 0 stops at once.
    /// </summary>
    public static (Vector3 Position, Vector3 Velocity) Step(Vector3 position, Vector3 velocity, double dragPerTick, double seconds)
    {
        if (seconds <= 0 || velocity == Vector3.Zero) return (position, velocity);
        double drag = Math.Clamp(dragPerTick, 0, 1);
        if (drag >= 1) return (position + velocity * (float)seconds, velocity);
        if (drag <= 0) return (position, Vector3.Zero);
        double rate = -Math.Log(drag) / TickSeconds;   // per second
        double keep = Math.Exp(-rate * seconds);
        var moved = velocity * (float)((1 - keep) / rate);
        var next = velocity * (float)keep;
        return (position + moved, next.Length() < RestSpeed ? Vector3.Zero : next);
    }

    /// <summary>How far an item thrown at <paramref name="speed"/> travels before it stops; infinite with no drag.</summary>
    public static double StoppingDistance(double speed, double dragPerTick)
    {
        double drag = Math.Clamp(dragPerTick, 0, 1);
        if (drag >= 1) return speed == 0 ? 0 : double.PositiveInfinity;
        if (drag <= 0) return 0;
        return Math.Abs(speed) * TickSeconds / -Math.Log(drag);
    }
}
