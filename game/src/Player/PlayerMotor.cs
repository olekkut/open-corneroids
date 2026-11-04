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

    /// <summary>The whole velocity, world space, m/s: the player's own plus <see cref="Drift"/>.</summary>
    public Vector3 Velocity { get; set; }

    /// <summary>
    /// The part of <see cref="Velocity"/> that is momentum the player was given (stepping off a moving ship, or shoved by
    /// one) rather than their own movement: the damping that brings the player's own movement to rest leaves it alone,
    /// so it is really kept. Thrusting against it eats into it at the thrust's rate, and a wall takes away the part into
    /// the wall. Ours, tunable.
    /// </summary>
    public Vector3 Drift { get; set; }

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
    /// <remarks>
    /// Collides with the static world first, then with each ship in its own local space, the move each grid allows
    /// being what the next one starts from. In a ship's space the body is the axis-aligned box that bounds it there
    /// (<see cref="LocalBody"/>): exact while the ship is turned by whole quarter turns from the world (as launched),
    /// slightly larger otherwise. Our approximation; one move is cut grid by grid, so a corner where a turned ship meets
    /// rock can stop the player a little early.
    /// </remarks>
    public Vector3 Step(Vector3 eye, Vector3 thrust, bool boost, float delta, GridSet grids)
    {
        float dt = Mathf.Clamp(delta, 0f, MaxStep);
        float cap = MaxSpeed * (boost ? BoostMultiplier : 1f);
        float decay = Mathf.Exp(-Damping * dt);
        bool thrusting = thrust.LengthSquared() > 1e-6f;
        var drift = Drift;
        // The player's own movement; the drift is added back for the move.
        var velocity = Velocity - drift;
        float before = velocity.Length();
        float push = Acceleration * (boost ? BoostMultiplier : 1f) * dt;
        if (thrusting)
        {
            // Thrusting against the drift spends the thrust on it first, so a player can always stop.
            var direction = thrust.Normalized();
            float against = -drift.Dot(direction);
            if (against > 0)
            {
                float spent = Mathf.Min(against, push);
                drift += direction * spent;
                push -= spent;
            }
        }

        // Speed left over from a boost bleeds off at the damping rate instead of snapping back the moment
        // Shift is released, and thrust is added on top of that, so braking against the motion is faster
        // than letting go.
        if (!thrusting)
            velocity *= decay;
        else if (before > cap)
            velocity *= Mathf.Max(cap, before * decay) / before;
        float allowed = Mathf.Max(cap, velocity.Length());

        if (thrusting)
            velocity += thrust.Normalized() * push;

        // Thrust may turn or slow the player but never push them past the cap (or past the bleeding boost speed).
        float speed = velocity.Length();
        if (speed > allowed)
            velocity *= allowed / speed;
        velocity += drift;

        var body = ToNumerics(eye) - new Num.Vector3(0, EyeHeight, 0);
        if (_noclip)
        {
            body += ToNumerics(velocity) * dt;
        }
        else
        {
            // The grids' own SolidQuery delegates and an indexed loop: this runs every frame and should not allocate.
            var worldSolid = grids.World.SolidQuery;
            if (_needsFreeSpot)
            {
                _needsFreeSpot = false;
                if (VoxelCollider.FindFreeSpot(body, HalfExtents, worldSolid) is { } free) body = free;
            }
            // A ship that moved into the player since the last frame pushes them out first (and along).
            var ships = grids.Ships;
            for (int i = 0; i < ships.Count; i++)
                body = PushOut(ships[i], body, ref velocity, ref drift, grids);

            var start = body;
            var result = VoxelCollider.Move(body, HalfExtents, ToNumerics(velocity) * dt, worldSolid);
            body = result.Position;
            if (result.HitX) velocity.X = drift.X = 0;
            if (result.HitY) velocity.Y = drift.Y = 0;
            if (result.HitZ) velocity.Z = drift.Z = 0;

            for (int i = 0; i < ships.Count; i++)
                body = MoveAgainst(ships[i], start, body, ref velocity, ref drift);
        }

        Velocity = velocity;
        Drift = drift.LengthSquared() < 1e-6f ? Vector3.Zero : drift;
        return new Vector3(body.X, body.Y + EyeHeight, body.Z);
    }

    /// <summary>Distance steps and the furthest a moving ship pushes the body out of its cells in one frame (cells).</summary>
    const float PushStep = 0.05f, MaxPush = 2.5f;

    static readonly Vector3[] PushDirections = [Vector3.Up, Vector3.Down, Vector3.Right, Vector3.Left, Vector3.Back, Vector3.Forward];

    /// <summary>
    /// When <paramref name="grid"/> (a moving ship) has come to overlap the body (centre <paramref name="body"/>, world
    /// space), the shortest move along one of the grid's axes that frees it, trying first the axes most along the
    /// ship's own motion there, so a ship ramming the player shoves them ahead of it. The player then moves at least as
    /// fast as the ship along that axis, and that speed is <see cref="Drift"/> (kept, not damped). The spot must also be
    /// free of the static world and the other ships: a player caught between a ship and rock is pushed aside (another
    /// axis) or, with no free spot, left where they are, inside the ship's cells rather than inside the rock (the ship
    /// is not stopped by the player). Ours: a player standing in a moving ship is not carried by it (no friction or
    /// "floor" frame), the ship's walls just push; a body deeper than <see cref="MaxPush"/> is left for
    /// <see cref="VoxelCollider.Move"/>, which lets it out but never back in.
    /// </summary>
    static Num.Vector3 PushOut(ShipGrid grid, Num.Vector3 body, ref Vector3 velocity, ref Vector3 drift, GridSet grids)
    {
        var (center, radius) = grid.WorldExtent;
        var bodyWorld = ToGodot(body);
        if (bodyWorld.DistanceSquaredTo(center) > (radius + 2) * (radius + 2)) return body;
        var (local, half) = LocalBody(grid, bodyWorld);
        var solid = grid.SolidQuery;
        if (!VoxelCollider.Overlaps(local, half, solid)) return body;

        var shipVelocity = grid.PointVelocity(bodyWorld);
        var along = grid.DirectionToLocal(shipVelocity);
        var order = PushDirections.OrderByDescending(d => d.Dot(along)).ToArray();
        for (float distance = PushStep; distance <= MaxPush; distance += PushStep)
        {
            foreach (var direction in order)
            {
                var moved = local + ToNumerics(direction) * distance;
                if (VoxelCollider.Overlaps(moved, half, solid)) continue;
                var world = grid.ToGlobal(ToGodot(moved));
                if (BlockedElsewhere(grid, world, grids)) continue;
                var normal = (grid.GlobalTransform.Basis * direction).Normalized();
                float ours = velocity.Dot(normal), theirs = shipVelocity.Dot(normal);
                if (ours < theirs) velocity += normal * (theirs - ours);
                float given = drift.Dot(normal);
                if (given < theirs) drift += normal * (theirs - given);
                return ToNumerics(world);
            }
        }
        return body;
    }

    /// <summary>Whether a body centred at world <paramref name="bodyCenter"/> overlaps the static world or any ship but
    /// <paramref name="pusher"/>.</summary>
    static bool BlockedElsewhere(ShipGrid pusher, Vector3 bodyCenter, GridSet grids)
    {
        if (VoxelCollider.Overlaps(ToNumerics(bodyCenter), HalfExtents, grids.World.SolidQuery)) return true;
        var ships = grids.Ships;
        for (int i = 0; i < ships.Count; i++)
        {
            var other = ships[i];
            if (other == pusher) continue;
            var (center, radius) = other.WorldExtent;
            if (bodyCenter.DistanceSquaredTo(center) > (radius + 2) * (radius + 2)) continue;
            var (local, half) = LocalBody(other, bodyCenter);
            if (VoxelCollider.Overlaps(local, half, other.SolidQuery)) return true;
        }
        return false;
    }

    /// <summary>
    /// The move from <paramref name="start"/> to <paramref name="end"/> (body centres, world space) as far as
    /// <paramref name="grid"/> allows it, swept in the grid's local space. A blocked local axis takes the velocity along
    /// that axis's world direction away.
    /// </summary>
    static Num.Vector3 MoveAgainst(BlockGrid grid, Num.Vector3 start, Num.Vector3 end, ref Vector3 velocity, ref Vector3 drift)
    {
        var (center, radius) = grid.Bounds;
        var startWorld = ToGodot(start);
        float reach = radius + 2 + (end - start).Length();
        if (startWorld.DistanceSquaredTo(center) > reach * reach) return end;

        var (localStart, half) = LocalBody(grid, startWorld);
        var localDelta = grid.DirectionToLocal(ToGodot(end - start));
        var result = VoxelCollider.Move(localStart, half, ToNumerics(localDelta), grid.SolidQuery);
        if (!result.HitX && !result.HitY && !result.HitZ) return end;
        var basis = grid.GlobalTransform.Basis;
        if (result.HitX) Stop(basis.X.Normalized(), ref velocity, ref drift);
        if (result.HitY) Stop(basis.Y.Normalized(), ref velocity, ref drift);
        if (result.HitZ) Stop(basis.Z.Normalized(), ref velocity, ref drift);
        return ToNumerics(grid.ToGlobal(ToGodot(result.Position)));
    }

    /// <summary>Takes the motion along the unit <paramref name="axis"/> out of both velocities.</summary>
    static void Stop(Vector3 axis, ref Vector3 velocity, ref Vector3 drift)
    {
        velocity -= axis * velocity.Dot(axis);
        drift -= axis * drift.Dot(axis);
    }

    /// <summary>
    /// The body (centre <paramref name="bodyCenter"/>, world space) in a grid's local space: its centre there, and the
    /// half extents of the axis-aligned box that bounds the turned body (|Rᵀ|·h). For the world grid, and any grid turned
    /// by whole quarter turns, that is the body itself.
    /// </summary>
    static (Num.Vector3 Center, Num.Vector3 HalfExtents) LocalBody(BlockGrid grid, Vector3 bodyCenter)
    {
        var basis = grid.GlobalTransform.Basis.Orthonormalized();
        var h = ToGodot(HalfExtents);
        var half = new Vector3(basis.X.Abs().Dot(h), basis.Y.Abs().Dot(h), basis.Z.Abs().Dot(h));
        return (ToNumerics(grid.ToLocal(bodyCenter)), ToNumerics(half));
    }

    /// <summary>Whether any cell of <paramref name="grid"/> that <paramref name="contains"/> accepts (grid-local cells)
    /// intersects the body of a player whose eye is at world <paramref name="eye"/>; see <see cref="LocalBody"/>.</summary>
    public static bool BodyOverlaps(BlockGrid grid, Vector3 eye, Func<Vector3I, bool> contains)
    {
        var (center, half) = LocalBody(grid, eye - new Vector3(0, EyeHeight, 0));
        return VoxelCollider.Overlaps(center, half, (x, y, z) => contains(new Vector3I(x, y, z)));
    }

    /// <summary>Whether a block at grid-local <paramref name="cell"/> of <paramref name="grid"/> would be in the body.</summary>
    public static bool BodyOccupies(BlockGrid grid, Vector3 eye, Vector3I cell) => BodyOverlaps(grid, eye, p => p == cell);

    /// <summary>True when a block at <paramref name="cell"/> would intersect the body of a player whose eye is
    /// at <paramref name="eye"/>. Meant for block placing: refusing such a placement keeps the player from
    /// being embedded, which <see cref="VoxelCollider.Move"/> lets them leave but never resolves by itself.</summary>
    public static bool BodyOccupies(Vector3 eye, Vector3I cell) => BodyOverlaps(eye, p => p == cell);

    /// <summary>True when any cell <paramref name="contains"/> accepts intersects the body: placing a multi-block, or a
    /// door about to move into new cells.</summary>
    public static bool BodyOverlaps(Vector3 eye, Func<Vector3I, bool> contains) =>
        VoxelCollider.Overlaps(ToNumerics(eye) - new Num.Vector3(0, EyeHeight, 0), HalfExtents,
            (x, y, z) => contains(new Vector3I(x, y, z)));

    static Num.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
    static Vector3 ToGodot(Num.Vector3 v) => new(v.X, v.Y, v.Z);
}
