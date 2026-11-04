using System.Numerics;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>How hard the flight assistant chases the pilot's request. All ours, tunable; original behaviour unknown.</summary>
/// <param name="LinearResponse">Per second: a velocity error of 1 m/s asks for this many m/s² of acceleration.</param>
/// <param name="AngularResponse">Per second: an angular velocity error of 1 rad/s asks for this many rad/s².</param>
/// <param name="MaxLinearAcceleration">Cap on the requested acceleration, m/s². Usually above what a ship's thrusters
/// give (30 vs. a typical 2–10), so a large error simply means "full thrust this way"; the allocator puts torque first,
/// so an unreachable push never turns into a spin (see <see cref="ThrustAllocator"/>).</param>
/// <param name="MaxAngularAcceleration">Cap on the requested angular acceleration, rad/s².</param>
/// <param name="SpeedLimit">m/s: the assistant never pushes the ship faster than this, dampeners or not, and brakes
/// back down to it (without dampeners, legs flown in different directions would otherwise add up without bound).
/// Infinite or not positive: no limit.</param>
public readonly record struct FlightAssistGains(float LinearResponse, float AngularResponse, float MaxLinearAcceleration,
    float MaxAngularAcceleration, float SpeedLimit = float.PositiveInfinity)
{
    /// <summary>Reaches ~86% of a requested speed change in 1 s and of a turn rate in 1/3 s. Tunable.</summary>
    public static FlightAssistGains Default { get; } = new(2f, 6f, 30f, 12f);
}

/// <summary>The wrench the assistant wants, ship-local: force through the centre of mass and torque about it.</summary>
public readonly record struct FlightCommand(Vector3 ForceLocal, Vector3 TorqueLocal);

/// <summary>
/// Turns the pilot's requested velocities into a force and torque for <see cref="ThrustAllocator"/>. There are no
/// gyroscopes: whatever this asks for is only achieved as far as the ship's thrusters allow. Ours; original behaviour
/// unknown.
/// </summary>
public static class FlightAssist
{
    /// <summary>A requested speed below this (m/s) counts as "no request" when dampeners are off.</summary>
    public const float Deadband = 1e-3f;

    /// <summary>
    /// The command for one physics step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Linear, with <paramref name="dampeners"/> on: a proportional controller on the whole velocity error, so the ship
    /// settles at the requested velocity and any sideways drift is cancelled (no request = come to a stop). With
    /// dampeners off, only the error along the requested direction counts and only while the ship is slower than
    /// requested that way: the assistant accelerates toward the request but never brakes, and drift is left alone, so
    /// the ship keeps its momentum Newtonian-style.
    /// </para>
    /// <para>
    /// Angular: always the whole error, dampeners or not (ours: an undamped spin with no gyroscopes is unflyable), so
    /// letting go of the stick stops the rotation. The torque is <c>I · α</c> with the local inertia, so a heavy ship
    /// asks for proportionally more.
    /// </para>
    /// <para>
    /// Speed: never pushed past <see cref="FlightAssistGains.SpeedLimit"/>, and brought back to it when above.
    /// </para>
    /// </remarks>
    /// <param name="desiredLinearVelocityLocal">Requested velocity in the ship's local axes (m/s).</param>
    /// <param name="desiredAngularVelocityLocal">Requested angular velocity in the ship's local axes (rad/s).</param>
    public static FlightCommand Command(RigidBodyState state, MassProperties mass, Vector3 desiredLinearVelocityLocal,
        Vector3 desiredAngularVelocityLocal, bool dampeners, FlightAssistGains gains)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(mass);
        if (mass.IsMassless) return default;
        desiredLinearVelocityLocal = Finite(desiredLinearVelocityLocal);
        desiredAngularVelocityLocal = Finite(desiredAngularVelocityLocal);

        var velocity = Finite(state.DirectionToLocal(state.LinearVelocity));
        var error = desiredLinearVelocityLocal - velocity;
        if (!dampeners)
        {
            float speed = desiredLinearVelocityLocal.Length();
            if (speed < Deadband) error = Vector3.Zero;
            else
            {
                var direction = desiredLinearVelocityLocal / speed;
                error = direction * Math.Max(0, Vector3.Dot(error, direction));
            }
        }
        if (gains.SpeedLimit > 0 && float.IsFinite(gains.SpeedLimit))
        {
            float speed = velocity.Length();
            if (speed >= gains.SpeedLimit)
            {
                // At the limit: nothing that adds speed, and the excess is braked away like any other error.
                var along = velocity / speed;
                error -= along * Math.Max(0, Vector3.Dot(error, along));
                error -= along * (speed - gains.SpeedLimit);
            }
        }
        var acceleration = Cap(error * Positive(gains.LinearResponse), gains.MaxLinearAcceleration);

        var spin = Finite(state.DirectionToLocal(state.AngularVelocity));
        var angularAcceleration = Cap((desiredAngularVelocityLocal - spin) * Positive(gains.AngularResponse), gains.MaxAngularAcceleration);

        return new FlightCommand(acceleration * mass.Mass, MassProperties.Multiply(mass.Inertia, angularAcceleration));
    }

    static float Positive(float v) => float.IsFinite(v) ? Math.Max(0, v) : 0;

    static Vector3 Cap(Vector3 v, float max)
    {
        max = float.IsFinite(max) ? Math.Max(0, max) : float.MaxValue;
        float length = v.Length();
        return length > max ? v * (max / length) : v;
    }

    static Vector3 Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) ? v : Vector3.Zero;
}
