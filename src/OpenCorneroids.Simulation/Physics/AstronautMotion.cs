using System.Numerics;

namespace OpenCorneroids.Simulation.Physics;

/// <summary>
/// How the astronaut's velocity changes under the movement keys, as in the original: each held direction key pushes 5 m/s² along its own direction (they add up); boosting replaces
/// the forward key with 10 m/s² along the view and raises the soft speed limit from 4 to 8 m/s; above the soft limit
/// the player loses 8 m/s² along the travel direction; nothing is ever faster than 20 m/s; and with no push at all the
/// speed falls by 6 m/s² to a stop. Velocities are relative to whatever the player has boarded (a ship), or to the
/// world.
/// </summary>
public static class AstronautMotion
{
    public const float KeyAcceleration = 5f;
    public const float BoostAcceleration = 10f;
    public const float SpeedLimit = 4f;
    public const float BoostSpeedLimit = 8f;
    public const float OverLimitDeceleration = 8f;
    public const float IdleDeceleration = 6f;
    public const float HardSpeedLimit = 20f;
    /// <summary>Suit power per second while boosting.</summary>
    public const float BoostPowerPerSecond = 20f;

    /// <summary>The keys held this frame, as unit directions in world space.</summary>
    /// <param name="Keys">Sum of the held direction keys' unit vectors (forward, back, strafe, up, down), forward included.</param>
    /// <param name="Forward">The view's forward unit vector.</param>
    /// <param name="ForwardHeld">Whether the forward key is among <paramref name="Keys"/>, so a boost can replace it.</param>
    /// <param name="Boost">Boosting this frame: the key is held and the suit paid for it.</param>
    public readonly record struct Input(Vector3 Keys, Vector3 Forward, bool ForwardHeld, bool Boost);

    /// <summary>The velocity after <paramref name="dt"/> seconds.</summary>
    /// <param name="speedMultiplier">Optional debug speed multiplier (1 = original speed).</param>
    public static Vector3 Step(Vector3 velocity, Input input, float dt, float speedMultiplier = 1f)
    {
        if (dt <= 0) return velocity;
        float scale = float.IsFinite(speedMultiplier) ? Math.Clamp(speedMultiplier, 0.05f, 100f) : 1f;
        float keyAccel = KeyAcceleration * scale;
        float boostAccel = BoostAcceleration * scale;
        var push = input.Keys * keyAccel;
        if (input.Boost)
        {
            // The boost takes the forward key's place (and works without it).
            if (input.ForwardHeld) push -= input.Forward * keyAccel;
            push += input.Forward * boostAccel;
        }
        velocity += push * dt;

        float speed = velocity.Length();
        float limit = (input.Boost ? BoostSpeedLimit : SpeedLimit) * scale;
        // Over the soft limit the speed bleeds off (never below the limit itself; the original lets plain thrust hover
        // just over it, the same thing to the eye).
        if (speed > limit)
        {
            float slowed = MathF.Max(limit, speed - OverLimitDeceleration * scale * dt);
            velocity *= slowed / speed;
            speed = slowed;
        }
        // No push at all: coast to a stop.
        if (push == Vector3.Zero && speed > 0)
        {
            float slowed = speed - IdleDeceleration * scale * dt;
            velocity = slowed <= 0 ? Vector3.Zero : velocity * (slowed / speed);
            speed = MathF.Max(0, slowed);
        }
        float hardLimit = HardSpeedLimit * scale;
        if (speed > hardLimit) velocity *= hardLimit / speed;
        return velocity;
    }
}
