using Godot;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Physics;
using Num = System.Numerics;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// Zero-g astronaut movement: thrusting changes velocity rather than setting it, so the player drifts the
/// way a body in space should, with just enough damping that letting go of the keys brings you to rest.
/// Plain C# rather than a Node so the camera stays in charge of input and a save system can read and
/// restore <see cref="Velocity"/> and <see cref="Noclip"/> without touching the scene tree.
/// All numbers are ours (tunable; original behaviour unknown).
/// </summary>
public sealed class PlayerMotor
{
    /// <summary>Thrust in m/s². Reaches cruise speed in about half a second: responsive, still floaty.</summary>
    public const float Acceleration = 20f;
    /// <summary>Applies to both thrust and top speed while Shift is held.</summary>
    public const float BoostMultiplier = 3f;
    /// <summary>Velocity decay per second when no thrust is given (and while slowing from boost speed).</summary>
    public const float Damping = 1.5f;
    public const float MaxSpeed = 12f;
    /// <summary>A long frame (loading hitch, window drag) is treated as several short ones' worth at most,
    /// so a stall cannot fling the player across the asteroid in one step.</summary>
    const float MaxStep = 0.1f;

    /// <summary>Half the body size: 0.6 × 1.8 × 0.6, enough to fit through a one-block gap sideways and a
    /// two-block gap upright.</summary>
    public static readonly Num.Vector3 HalfExtents = new(0.3f, 0.9f, 0.3f);
    /// <summary>The camera sits this far above the body centre, roughly where a helmet visor would be.</summary>
    public const float EyeHeight = 0.7f;

    bool _needsFreeSpot = true;
    bool _noclip;

    public Vector3 Velocity { get; set; }

    /// <summary>Flies through rock. Turning it off re-checks for overlap, so the player is pushed out of
    /// whatever they were inside instead of being trapped.</summary>
    public bool Noclip
    {
        get => _noclip;
        set
        {
            if (_noclip && !value) _needsFreeSpot = true;
            _noclip = value;
        }
    }

    /// <summary>Advances one frame and returns the new eye position.</summary>
    /// <param name="thrust">Desired direction in world space (camera-relative already); any length, zero for none.</param>
    public Vector3 Step(Vector3 eye, Vector3 thrust, bool boost, float delta, VoxelWorld world)
    {
        float dt = Mathf.Clamp(delta, 0f, MaxStep);
        float cap = MaxSpeed * (boost ? BoostMultiplier : 1f);
        float decay = Mathf.Exp(-Damping * dt);
        bool thrusting = thrust.LengthSquared() > 1e-6f;
        var velocity = Velocity;
        float before = velocity.Length();

        // Speed left over from a boost bleeds off at the damping rate instead of snapping back the moment
        // Shift is released, and thrust is added on top of that, so braking against the motion is faster
        // than letting go.
        if (!thrusting)
            velocity *= decay;
        else if (before > cap)
            velocity *= Mathf.Max(cap, before * decay) / before;
        float allowed = Mathf.Max(cap, velocity.Length());

        if (thrusting)
            velocity += thrust.Normalized() * Acceleration * (boost ? BoostMultiplier : 1f) * dt;

        // Thrust may turn or slow the player but never push them past the cap (or past the bleeding boost speed).
        float speed = velocity.Length();
        if (speed > allowed)
            velocity *= allowed / speed;

        var body = ToNumerics(eye) - new Num.Vector3(0, EyeHeight, 0);
        if (_noclip)
        {
            body += ToNumerics(velocity) * dt;
        }
        else
        {
            bool IsSolid(int x, int y, int z) => world.Get(new Vector3I(x, y, z)) != BlockPalette.Air;
            if (_needsFreeSpot)
            {
                _needsFreeSpot = false;
                if (VoxelCollider.FindFreeSpot(body, HalfExtents, IsSolid) is { } free) body = free;
            }
            var result = VoxelCollider.Move(body, HalfExtents, ToNumerics(velocity) * dt, IsSolid);
            body = result.Position;
            if (result.HitX) velocity.X = 0;
            if (result.HitY) velocity.Y = 0;
            if (result.HitZ) velocity.Z = 0;
        }

        Velocity = velocity;
        return new Vector3(body.X, body.Y + EyeHeight, body.Z);
    }

    /// <summary>True when a block at <paramref name="cell"/> would intersect the body of a player whose eye is
    /// at <paramref name="eye"/>. Meant for block placing: refusing such a placement keeps the player from
    /// being embedded, which <see cref="VoxelCollider.Move"/> lets them leave but never resolves by itself.</summary>
    public static bool BodyOccupies(Vector3 eye, Vector3I cell) =>
        VoxelCollider.Overlaps(ToNumerics(eye) - new Num.Vector3(0, EyeHeight, 0), HalfExtents,
            (x, y, z) => x == cell.X && y == cell.Y && z == cell.Z);

    static Num.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
}
