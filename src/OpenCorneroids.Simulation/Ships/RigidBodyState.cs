using System.Numerics;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// Where a rigid body is and how it moves, in world space. <see cref="Position"/> is the world position of the centre
/// of mass, so the body turns about it; a point at local (grid) coordinate p is at
/// <c>Position + rotate(Orientation, p − centreOfMass)</c> (see <see cref="ToWorld"/>).
/// </summary>
public sealed class RigidBodyState
{
    /// <summary>
    /// Longest integration step. A longer <see cref="Step"/> is split into equal sub-steps no longer than this, so the
    /// result barely depends on the frame rate. Tunable.
    /// </summary>
    public const float MaxSubStep = 1f / 60f;

    /// <summary>
    /// Longest time one <see cref="Step"/> simulates; the rest of a longer hitch is dropped, so a stalled frame cannot
    /// make the body jump or the loop run for long. Tunable.
    /// </summary>
    public const float MaxStep = 0.25f;

    /// <summary>
    /// Suggested linear damping, per second (velocity decays by <c>exp(−k·t)</c>): a small "space friction" so a
    /// forgotten ship eventually stops drifting. Ours, tunable; 0 is pure Newtonian motion.
    /// </summary>
    public const float DefaultLinearDamping = 0.01f;

    /// <summary>Suggested angular damping, per second; see <see cref="DefaultLinearDamping"/>. Ours, tunable.</summary>
    public const float DefaultAngularDamping = 0.05f;

    /// <summary>Maximum ship linear speed in units per second, as in the original.</summary>
    public const float MaxLinearSpeed = 20f;

    /// <summary>Maximum ship angular speed in radians per second, as in the original.</summary>
    public const float MaxAngularSpeed = 1.5f;

    /// <summary>Linear coasting deceleration in units/s² when no thruster is firing, as in the original.</summary>
    public const float CoastLinearDeceleration = 0.25f;

    /// <summary>Linear speed threshold below which a coasting ship stops completely, as in the original.</summary>
    public const float CoastLinearStop = 0.025f;

    /// <summary>Angular coasting deceleration in rad/s² when no thruster is firing, as in the original.</summary>
    public const float CoastAngularDeceleration = 0.03f;

    /// <summary>Angular speed threshold below which a coasting ship stops rotating, as in the original.</summary>
    public const float CoastAngularStop = 0.003f;

    public Vector3 Position { get; set; }
    public Quaternion Orientation { get; set; } = Quaternion.Identity;
    /// <summary>Velocity of the centre of mass, world space.</summary>
    public Vector3 LinearVelocity { get; set; }
    /// <summary>Angular velocity, world axes, radians per second (right-hand rule).</summary>
    public Vector3 AngularVelocity { get; set; }

    public RigidBodyState() { }

    public RigidBodyState(Vector3 position, Quaternion orientation, Vector3 linearVelocity = default, Vector3 angularVelocity = default)
    {
        Position = position;
        Orientation = orientation;
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
    }

    public RigidBodyState Clone() => new(Position, Orientation, LinearVelocity, AngularVelocity);

    /// <summary>World position of local (grid) point <paramref name="local"/> for a body whose centre of mass is at
    /// local <paramref name="centerOfMass"/>.</summary>
    public Vector3 ToWorld(Vector3 local, Vector3 centerOfMass) => Position + Vector3.Transform(local - centerOfMass, Orientation);

    /// <summary>Inverse of <see cref="ToWorld"/>.</summary>
    public Vector3 ToLocal(Vector3 world, Vector3 centerOfMass) =>
        Vector3.Transform(world - Position, Quaternion.Conjugate(Orientation)) + centerOfMass;

    /// <summary>A world direction expressed in the body's local axes.</summary>
    public Vector3 DirectionToLocal(Vector3 world) => Vector3.Transform(world, Quaternion.Conjugate(Orientation));

    /// <summary>A local direction expressed in world axes.</summary>
    public Vector3 DirectionToWorld(Vector3 local) => Vector3.Transform(local, Orientation);

    /// <summary>
    /// Advances by <paramref name="dt"/> seconds under a constant world-space <paramref name="force"/> (through the
    /// centre of mass) and <paramref name="torque"/> (about it).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Semi-implicit (symplectic) Euler per sub-step: velocities first, then position and orientation from the new
    /// velocities. Angular motion works on angular momentum L = I_world·ω: the torque changes L, the orientation turns
    /// by the spin L gives at the half-step orientation (midpoint rule; an exact axis–angle rotation, then
    /// renormalised), and ω is recomputed from L with the turned inertia, so a tumbling body keeps its momentum exactly
    /// and its energy closely without a separate gyroscopic term.
    /// </para>
    /// <para>
    /// A massless body (<see cref="MassProperties.IsMassless"/>) ignores force and torque and just coasts. Non-finite
    /// inputs are treated as zero and a non-positive or non-finite <paramref name="dt"/> does nothing, so the state never
    /// becomes NaN from bad input.
    /// </para>
    /// </remarks>
    /// <param name="linearDamping">Per second, ≥ 0; see <see cref="DefaultLinearDamping"/>.</param>
    /// <param name="angularDamping">Per second, ≥ 0; see <see cref="DefaultAngularDamping"/>.</param>
    public void Step(Vector3 force, Vector3 torque, MassProperties mass, float dt, float linearDamping = 0, float angularDamping = 0)
    {
        ArgumentNullException.ThrowIfNull(mass);
        if (!float.IsFinite(dt) || dt <= 0) return;
        dt = Math.Min(dt, MaxStep);
        force = Finite(force);
        torque = Finite(torque);
        linearDamping = float.IsFinite(linearDamping) ? Math.Max(0, linearDamping) : 0;
        angularDamping = float.IsFinite(angularDamping) ? Math.Max(0, angularDamping) : 0;

        int steps = Math.Max(1, (int)MathF.Ceiling(dt / MaxSubStep - 1e-4f));
        float h = dt / steps;
        float linearDecay = MathF.Exp(-linearDamping * h);
        float angularDecay = MathF.Exp(-angularDamping * h);
        bool dynamic = !mass.IsMassless;

        for (int i = 0; i < steps; i++)
        {
            var v = LinearVelocity;
            var w = AngularVelocity;
            var q = Orientation;
            if (dynamic)
            {
                v += force * (mass.InverseMass * h);
                v *= linearDecay;
                var momentum = (mass.ApplyWorldInertia(q, w) + torque * h) * angularDecay;
                Position += v * h;
                // Midpoint rule: turn by the spin the momentum gives at the half-turned orientation (second order,
                // so a free tumble keeps its energy far better than turning by the start-of-step spin).
                var half = Turn(q, mass.ApplyWorldInverseInertia(q, momentum), h * 0.5f);
                q = Turn(q, mass.ApplyWorldInverseInertia(half, momentum), h);
                w = mass.ApplyWorldInverseInertia(q, momentum);
            }
            else
            {
                v *= linearDecay;
                w *= angularDecay;
                Position += v * h;
                q = Turn(q, w, h);
            }
            LinearVelocity = v;
            AngularVelocity = w;
            Orientation = q;
        }
    }

    /// <summary>
    /// Advances by <paramref name="dt"/> seconds using the original's ship motion rules: linear speed capped at
    /// <see cref="MaxLinearSpeed"/> (20 u/s), angular speed capped at <see cref="MaxAngularSpeed"/> (1.5 rad/s), and
    /// linear/angular coasting slowdown when <paramref name="thrustersFiring"/> is <c>false</c>.
    /// </summary>
    public void StepOriginal(Vector3 force, Vector3 torque, MassProperties mass, float dt, bool thrustersFiring)
    {
        ArgumentNullException.ThrowIfNull(mass);
        if (!float.IsFinite(dt) || dt <= 0) return;
        dt = Math.Min(dt, MaxStep);
        force = Finite(force);
        torque = Finite(torque);

        int steps = Math.Max(1, (int)MathF.Ceiling(dt / MaxSubStep - 1e-4f));
        float h = dt / steps;
        bool dynamic = !mass.IsMassless;

        for (int i = 0; i < steps; i++)
        {
            var v = LinearVelocity;
            var w = AngularVelocity;
            var q = Orientation;
            if (dynamic)
            {
                v += force * (mass.InverseMass * h);
                w += mass.ApplyWorldInverseInertia(q, torque * h);
            }
            if (!thrustersFiring)
            {
                v = SlowDown(v, CoastLinearDeceleration * h, CoastLinearStop);
                w = SlowDown(w, CoastAngularDeceleration * h, CoastAngularStop);
            }
            v = ClampMagnitude(v, MaxLinearSpeed);
            w = ClampMagnitude(w, MaxAngularSpeed);
            Position += v * h;
            q = Turn(q, w, h);
            LinearVelocity = v;
            AngularVelocity = w;
            Orientation = q;
        }
    }

    static Vector3 SlowDown(Vector3 v, float delta, float stopThreshold)
    {
        float length = v.Length();
        if (length <= stopThreshold || length - delta <= stopThreshold)
            return Vector3.Zero;
        return v * ((length - delta) / length);
    }

    static Vector3 ClampMagnitude(Vector3 v, float maxLength)
    {
        float lengthSq = v.LengthSquared();
        if (lengthSq > maxLength * maxLength)
            return v * (maxLength / MathF.Sqrt(lengthSq));
        return v;
    }

    /// <summary>Kinetic energy, translational plus rotational: ½·m·v² + ½·ω·I_world·ω.</summary>
    public float KineticEnergy(MassProperties mass)
    {
        ArgumentNullException.ThrowIfNull(mass);
        return 0.5f * mass.Mass * LinearVelocity.LengthSquared()
            + 0.5f * Vector3.Dot(AngularVelocity, mass.ApplyWorldInertia(Orientation, AngularVelocity));
    }

    static Quaternion Turn(Quaternion q, Vector3 w, float h)
    {
        float speed = w.Length();
        if (speed * h > 1e-9f)
            q = Quaternion.CreateFromAxisAngle(w / speed, speed * h) * q;
        float length = q.Length();
        return length > 1e-6f && float.IsFinite(length) ? Quaternion.Multiply(q, 1f / length) : Quaternion.Identity;
    }

    static Vector3 Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) ? v : Vector3.Zero;
}
