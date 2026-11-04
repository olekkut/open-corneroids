using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// A thruster as the flight model sees it, in the ship's local (grid) coordinates: where its force acts, which way it
/// pushes the ship, and how hard at full throttle.
/// </summary>
/// <param name="LocalPosition">Point of application: the centre of the thruster's model box.</param>
/// <param name="LocalForceDirection">Unit direction of the force on the ship (opposite to the flame).</param>
/// <param name="MaxForce">Force at throttle 1 with full power, ≥ 0.</param>
public readonly record struct ThrusterMount(Vector3 LocalPosition, Vector3 LocalForceDirection, float MaxForce)
{
    /// <summary>
    /// Multiplier from an itemset's <c>&lt;force&gt;</c> to our force units (mass units × m/s²). The game's demo ship
    /// (a 5×3×7 hull of vanilla blocks, a nuclear power block and two weak hydrogen thrusters per face, mass about 300)
    /// gets 2 × 20 × 30 / 300 ≈ 4 m/s² along each axis; a bigger ship with standard thrusters (force 60) lands in the
    /// same 2–10 m/s² range. Ours, tunable; original behaviour unknown.
    /// </summary>
    public const float DefaultThrustScale = 30f;

    /// <summary>Which way the exhaust leaves: the opposite of <see cref="LocalForceDirection"/>.</summary>
    public Vector3 LocalFlameDirection => -LocalForceDirection;

    /// <summary>
    /// The mount for a thruster block placed at <paramref name="origin"/> with orientation
    /// <paramref name="orientation"/> in the ship grid.
    /// </summary>
    /// <remarks>
    /// The thrust-direction rule (ours; original behaviour unknown): a thruster's placement bounds reserve room for
    /// its exhaust beside the model ("thruster wash" in the DarthNox v21 readme). The flame points from the model box
    /// centre toward the bounds centre, snapped to the dominant local axis, then turned with the block; the force on
    /// the ship is the opposite direction, applied at the model box centre. The vanilla weak hydrogen thruster (bounds
    /// 1×3×1, model in the bottom cell) therefore flames along local +Y and pushes along −Y. A thruster whose model
    /// fills its bounds has no wash to point at, and is taken to flame along local +Y like the vanilla ones. Magnitude:
    /// <c>max(0, thruster.Force) · thrustScale</c>; a non-finite force or scale gives 0.
    /// </remarks>
    public static ThrusterMount FromShape(BlockShape shape, Int3 origin, Orientation orientation, ThrusterComponent thruster,
        float thrustScale = DefaultThrustScale)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(thruster);
        var flameLocal = DominantAxis(shape.Size.ToVector3() * 0.5f - shape.ModelCenter) ?? Vector3.UnitY;
        var flame = orientation.Apply(flameLocal);
        var position = shape.ModelBox(origin, orientation).Center;
        float force = float.IsFinite(thruster.Force) && float.IsFinite(thrustScale) ? Math.Max(0, thruster.Force * thrustScale) : 0;
        return new ThrusterMount(position, -flame, force);
    }

    /// <summary>The signed unit axis with the largest component, or null when the vector is (nearly) zero.
    /// Ties go to X, then Y, then Z.</summary>
    static Vector3? DominantAxis(Vector3 v)
    {
        var a = Vector3.Abs(v);
        const float tiny = 1e-4f;
        if (a.X < tiny && a.Y < tiny && a.Z < tiny) return null;
        if (a.X >= a.Y && a.X >= a.Z) return new Vector3(MathF.Sign(v.X), 0, 0);
        if (a.Y >= a.Z) return new Vector3(0, MathF.Sign(v.Y), 0);
        return new Vector3(0, 0, MathF.Sign(v.Z));
    }
}
