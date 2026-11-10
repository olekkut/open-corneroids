using System.Numerics;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Physics;

/// <summary>
/// A grid of unit blocks somewhere in the world, for <see cref="RiderCollision"/>: the static asteroid field or a ship.
/// Block (x,y,z) occupies [x,x+1)×[y,y+1)×[z,z+1) of the grid's own space.
/// </summary>
public interface IGridShape
{
    /// <summary>Grid space to world space (rigid: a turn and a move).</summary>
    Matrix4x4 ToWorld { get; }

    /// <summary>World space to grid space; the inverse of <see cref="ToWorld"/>.</summary>
    Matrix4x4 ToLocal { get; }

    /// <summary>Whether the cell at grid coordinates (x,y,z) is solid.</summary>
    Func<int, int, int, bool> Solid { get; }

    /// <summary>World centre and radius of a sphere holding every solid cell, so far grids are skipped. The static
    /// world, which is everywhere, gives an infinite radius.</summary>
    (Vector3 Center, float Radius) Extent { get; }

    /// <summary>World velocity of the grid's material at world point <paramref name="world"/> (zero for the static
    /// world).</summary>
    Vector3 PointVelocity(Vector3 world);
}

/// <summary>
/// The astronaut's body against every block grid at once: the static world, the ship they ride and the other ships.
/// Godot-free so it can be tested; the game's player motor is a thin adapter around it.
/// </summary>
/// <remarks>
/// <para>
/// As in the original, overlap is resolved against every grid, the ridden ship included: a body that a grid has come
/// to overlap (a ship closing on the player, a carried rider pressed into rock) is pushed out. Only the part of the
/// velocity going into the grid it was pushed out of is removed, so there is no bounce: relative to the body's own
/// frame for the static world and the ridden ship, relative to the ship for one that rams the body (which so shoves
/// it at its speed). The original pushes a sphere
/// out along the averaged contact normal; ours moves the body's stand-in cube (the cube around the original's sphere
/// of radius 0.49, in each grid's own axes) the shortest way along one of that grid's axes to a spot free of every
/// grid, trying first the axes most along the motion that caused the overlap (Ours).
/// </para>
/// <para>
/// Then the body sweeps its velocity × dt against each grid in turn (<see cref="VoxelCollider.Move"/> in that grid's
/// axes). Ours: the end is checked again against every grid, and if the move came to overlap one it did not overlap
/// at the start (sliding along one turned grid can bring back motion another stopped), the body goes back along the
/// move to the last free spot. Ours, last resort: a body still inside the static world after the push is moved to
/// the nearest free spot (<see cref="VoxelCollider.FindFreeSpot"/>). A push limit of 0 turns pushing off, the last
/// resort included.
/// </para>
/// <para>
/// Velocities are relative to the ridden ship while riding one (it has already carried the body this frame), else to
/// the world, as in the original.
/// </para>
/// </remarks>
public static class RiderCollision
{
    /// <summary>The original's body is a sphere of radius 0.49; collision uses the cube that bounds it.</summary>
    public const float BodyRadius = 0.49f;

    /// <summary>Half size of the body's stand-in cube, in each grid's own axes.</summary>
    public static readonly Vector3 HalfExtents = new(BodyRadius, BodyRadius, BodyRadius);

    /// <summary>Distance steps the push-out tries, in cells.</summary>
    public const float PushStep = 0.05f;

    /// <summary>How far one push moves the body at most, in cells: <c>sv_player_pushout_max</c> (Ours).</summary>
    public const float DefaultMaxPush = 2.5f;

    /// <summary>Beyond a grid's extent sphere by this much, nothing of it can touch the body (the cube's corner is
    /// 0.85 from its centre).</summary>
    const float Margin = 2f;

    /// <summary>The six axis directions of a grid, in the order that wins ties: up first.</summary>
    static readonly Vector3[] PushDirections =
        [Vector3.UnitY, -Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ];

    /// <summary>The body after one <see cref="Step"/>.</summary>
    /// <param name="Center">The body's centre, world space.</param>
    /// <param name="Velocity">Its velocity after the walls, relative to the ridden ship (or the world).</param>
    /// <param name="Impact">The velocity the sweep's walls took away: its length is the speed the body hit a surface
    /// with (only the part straight into it), its direction points into the surface. Zero without a hit; pushes do
    /// not count.</param>
    /// <param name="FreedFromRock">The last resort moved the body out of the static world.</param>
    public readonly record struct Result(Vector3 Center, Vector3 Velocity, Vector3 Impact, bool FreedFromRock);

    /// <summary>
    /// One frame of the body against every grid: push out of any grid it overlaps, sweep
    /// <paramref name="velocity"/> × <paramref name="dt"/>, check the end again.
    /// </summary>
    /// <param name="center">The body's centre, world space, after the ridden ship (if any) carried it.</param>
    /// <param name="velocity">Relative to <paramref name="riding"/>, or to the world.</param>
    /// <param name="world">The static world.</param>
    /// <param name="ships">Every ship, <paramref name="riding"/> included.</param>
    /// <param name="riding">The ship the body rides, or null.</param>
    /// <param name="maxPush">How far one push may move the body (<c>sv_player_pushout_max</c>).</param>
    public static Result Step(Vector3 center, Vector3 velocity, float dt, IGridShape world, IReadOnlyList<IGridShape> ships,
        IGridShape? riding = null, float maxPush = DefaultMaxPush)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ships);
        var frame = riding?.PointVelocity(center) ?? Vector3.Zero;
        int count = ships.Count + 1;

        for (int i = 0; i < count; i++)
        {
            var grid = Grid(i, world, ships);
            var approach = grid.PointVelocity(center) - frame;
            // A ship the body does not ride shoves it at its speed. The static world and the ridden ship only stop
            // the body going into them: its velocity is already relative to its frame, and the ship's speed is not
            // added (it would stay with the body after the ship itself stopped).
            bool shoves = !ReferenceEquals(grid, world) && !ReferenceEquals(grid, riding);
            center = PushOut(grid, center, ref velocity, approach, shoves ? approach : Vector3.Zero, alongFirst: false,
                world, ships, maxPush);
        }

        bool freed = false;
        if (PushSteps(maxPush) > 0 && Overlaps(world, center))
        {
            var local = Vector3.Transform(center, world.ToLocal);
            if (VoxelCollider.FindFreeSpot(local, HalfExtents, world.Solid) is { } free && free != local)
            {
                center = Vector3.Transform(free, world.ToWorld);
                freed = true;
            }
        }

        var start = center;
        var before = velocity;
        var end = start + velocity * dt;
        for (int i = 0; i < count; i++) end = MoveAgainst(Grid(i, world, ships), start, end, ref velocity);
        end = Recheck(start, end, ref velocity, world, ships);
        return new Result(end, velocity, before - velocity, freed);
    }

    /// <summary>
    /// Boarding <paramref name="ship"/>: first the body is pushed out of it, the ship's own motion choosing the way
    /// (a way out along that motion within <paramref name="maxPush"/> wins over a shorter one against it, so a ship
    /// that closed on the player faster than the boarding reach in one frame shoves them ahead of it rather than
    /// swallowing them), then the velocity becomes relative to the ship. A ship that came further than
    /// <paramref name="maxPush"/> into the body in one frame can still pass over it.
    /// </summary>
    /// <param name="velocity">World velocity (the body rides nothing yet).</param>
    /// <param name="ships">Every ship, <paramref name="ship"/> included.</param>
    public static (Vector3 Center, Vector3 Velocity) Board(Vector3 center, Vector3 velocity, IGridShape ship,
        IGridShape world, IReadOnlyList<IGridShape> ships, float maxPush = DefaultMaxPush)
    {
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ships);
        var approach = ship.PointVelocity(center);
        center = PushOut(ship, center, ref velocity, approach, approach, alongFirst: true, world, ships, maxPush);
        return (center, velocity - ship.PointVelocity(center));
    }

    /// <summary>Whether a body centred at world <paramref name="center"/> overlaps the static world or any ship but
    /// <paramref name="except"/>.</summary>
    public static bool Blocked(Vector3 center, IGridShape world, IReadOnlyList<IGridShape> ships, IGridShape? except = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ships);
        for (int i = 0; i <= ships.Count; i++)
        {
            var grid = Grid(i, world, ships);
            if (!ReferenceEquals(grid, except) && Overlaps(grid, center)) return true;
        }
        return false;
    }

    /// <summary>Whether a body centred at world <paramref name="center"/> overlaps a solid cell of
    /// <paramref name="grid"/> (the stand-in cube in the grid's axes, by more than a rounding sliver).</summary>
    public static bool Overlaps(IGridShape grid, Vector3 center)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return Near(grid, center, 0) && VoxelCollider.Overlaps(Vector3.Transform(center, grid.ToLocal), HalfExtents, grid.Solid);
    }

    /// <summary>
    /// When <paramref name="grid"/> overlaps the body, the shortest move along one of the grid's axes, up to
    /// <paramref name="maxPush"/>, to a spot free of every grid, trying first at each distance the axes most along
    /// <paramref name="approach"/> (the grid's motion relative to the body's frame, so a ship ramming the player shoves
    /// them ahead of it). With <paramref name="alongFirst"/>, a way out along the approach (within 60° of it) wins
    /// over any shorter one before the others are tried. The part of the velocity going into the grid, relative to
    /// <paramref name="reference"/>, is removed: with the ship's velocity a rammed body moves at least as fast as the
    /// ship along the push; with zero the body just stops going into the wall. With no free spot in reach the body
    /// stays where it is.
    /// </summary>
    static Vector3 PushOut(IGridShape grid, Vector3 center, ref Vector3 velocity, Vector3 approach, Vector3 reference,
        bool alongFirst, IGridShape world, IReadOnlyList<IGridShape> ships, float maxPush)
    {
        int steps = PushSteps(maxPush);
        if (steps == 0 || !Overlaps(grid, center)) return center;
        var local = Vector3.Transform(center, grid.ToLocal);
        var along = Vector3.TransformNormal(approach, grid.ToLocal);
        Span<int> order = stackalloc int[6];
        Order(along, order);

        int k = -1;
        Vector3 spot = default;
        if (alongFirst)
        {
            int leading = 0;
            float length = along.Length();
            while (leading < 6 && length > 0 && Vector3.Dot(PushDirections[order[leading]], along) > 0.5f * length) leading++;
            k = FindPush(grid, local, order[..leading], steps, world, ships, out spot);
        }
        if (k < 0) k = FindPush(grid, local, order, steps, world, ships, out spot);
        if (k < 0) return center;

        var normal = Vector3.Normalize(Vector3.TransformNormal(PushDirections[k], grid.ToWorld));
        float into = Vector3.Dot(velocity - reference, normal);
        if (into < 0) velocity -= normal * into;
        return spot;
    }

    /// <summary>The first of <paramref name="directions"/> (indices into <see cref="PushDirections"/>), at the
    /// shortest distance up to <paramref name="steps"/> × <see cref="PushStep"/>, that moves the body at grid-local
    /// <paramref name="local"/> to a <paramref name="spot"/> free of every grid; -1 when there is none.</summary>
    static int FindPush(IGridShape grid, Vector3 local, ReadOnlySpan<int> directions, int steps,
        IGridShape world, IReadOnlyList<IGridShape> ships, out Vector3 spot)
    {
        var solid = grid.Solid;
        for (int step = 1; step <= steps; step++)
        {
            float distance = step * PushStep;
            foreach (int k in directions)
            {
                var moved = local + PushDirections[k] * distance;
                if (VoxelCollider.Overlaps(moved, HalfExtents, solid)) continue;
                spot = Vector3.Transform(moved, grid.ToWorld);
                if (Blocked(spot, world, ships, grid)) continue;
                return k;
            }
        }
        spot = default;
        return -1;
    }

    /// <summary>Distance steps a push may take under <paramref name="maxPush"/>; 0 turns pushing off (the last
    /// resort out of rock too).</summary>
    static int PushSteps(float maxPush) =>
        float.IsFinite(maxPush) ? (int)MathF.Floor(Math.Clamp(maxPush, 0, 64) / PushStep + 1e-3f) : 0;

    /// <summary>The indices of <see cref="PushDirections"/> sorted by how much they point along
    /// <paramref name="along"/>, most first; equal ones keep their order (a stable insertion sort).</summary>
    static void Order(Vector3 along, Span<int> order)
    {
        Span<float> dots = stackalloc float[6];
        for (int i = 0; i < 6; i++)
        {
            float dot = Vector3.Dot(PushDirections[i], along);
            dots[i] = float.IsFinite(dot) ? dot : 0;
            order[i] = i;
        }
        for (int i = 1; i < 6; i++)
        {
            int k = order[i];
            int j = i - 1;
            for (; j >= 0 && dots[order[j]] < dots[k]; j--) order[j + 1] = order[j];
            order[j + 1] = k;
        }
    }

    /// <summary>
    /// The move from <paramref name="start"/> to <paramref name="end"/> (body centres, world space) as far as
    /// <paramref name="grid"/> allows it, swept in the grid's axes. A blocked axis takes the velocity along that axis's
    /// world direction away.
    /// </summary>
    static Vector3 MoveAgainst(IGridShape grid, Vector3 start, Vector3 end, ref Vector3 velocity)
    {
        if (!Near(grid, start, (end - start).Length())) return end;
        var localStart = Vector3.Transform(start, grid.ToLocal);
        var localDelta = Vector3.TransformNormal(end - start, grid.ToLocal);
        var result = VoxelCollider.Move(localStart, HalfExtents, localDelta, grid.Solid);
        if (!result.HitAny) return end;
        if (result.HitX) Stop(grid, Vector3.UnitX, ref velocity);
        if (result.HitY) Stop(grid, Vector3.UnitY, ref velocity);
        if (result.HitZ) Stop(grid, Vector3.UnitZ, ref velocity);
        return Vector3.Transform(result.Position, grid.ToWorld);
    }

    /// <summary>
    /// Ours: <paramref name="end"/> checked against every grid. If it overlaps one that <paramref name="start"/> did
    /// not, the body goes back along the move to the last fraction free of every grid it was free of at the start
    /// (<see cref="ShipContact.LastFree"/>), and the velocity along each axis of a grid it entered that would have
    /// taken it in is removed. When only the move as a whole went in (a corner), the velocity is removed along the
    /// axis the body went in least deep along, so the body slides on past the corner and only its speed into that face
    /// counts as an impact.
    /// </summary>
    internal static Vector3 Recheck(Vector3 start, Vector3 end, ref Vector3 velocity, IGridShape world,
        IReadOnlyList<IGridShape> ships)
    {
        if (!EntersAny(start, end, world, ships)) return end;
        // Grids the body was free of at the start: the move must not take it into any of them, even on the way.
        var wereFree = new List<IGridShape>();
        var entered = new List<IGridShape>();
        for (int i = 0; i <= ships.Count; i++)
        {
            var grid = Grid(i, world, ships);
            if (Overlaps(grid, start)) continue;
            wereFree.Add(grid);
            if (Overlaps(grid, end)) entered.Add(grid);
        }
        if (entered.Count == 0) return end;

        float t = ShipContact.LastFree(f => wereFree.Exists(g => Overlaps(g, Vector3.Lerp(start, end, f))));
        var free = Vector3.Lerp(start, end, t);
        foreach (var grid in entered)
        {
            var local = Vector3.Transform(free, grid.ToLocal);
            var rest = Vector3.TransformNormal(end - free, grid.ToLocal);
            bool stopped = false;
            for (int axis = 0; axis < 3; axis++)
            {
                var unit = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
                float part = Vector3.Dot(rest, unit);
                if (part == 0 || !VoxelCollider.Overlaps(local + unit * part, HalfExtents, grid.Solid)) continue;
                Stop(grid, unit, ref velocity);
                stopped = true;
            }
            // Only the move as a whole went in (a corner): stop along the shallowest way in.
            if (!stopped && Shallowest(grid, end, rest) is { } shallowest) Stop(grid, shallowest, ref velocity);
        }
        return free;
    }

    /// <summary>Whether the body at <paramref name="end"/> overlaps a grid it did not overlap at
    /// <paramref name="start"/> (checked without allocating; the usual answer is no).</summary>
    static bool EntersAny(Vector3 start, Vector3 end, IGridShape world, IReadOnlyList<IGridShape> ships)
    {
        for (int i = 0; i <= ships.Count; i++)
        {
            var grid = Grid(i, world, ships);
            if (Overlaps(grid, end) && !Overlaps(grid, start)) return true;
        }
        return false;
    }

    /// <summary>
    /// Of the grid's axes along which <paramref name="rest"/> (the part of the move that went in, grid space) moved,
    /// the one the body at world <paramref name="end"/> has to back off least along to leave the grid's cells, found
    /// by bisection; null when the rest did not move.
    /// </summary>
    static Vector3? Shallowest(IGridShape grid, Vector3 end, Vector3 rest)
    {
        var local = Vector3.Transform(end, grid.ToLocal);
        Vector3? best = null;
        float bestDepth = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            var unit = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            float part = Vector3.Dot(rest, unit);
            if (part == 0) continue;
            // The fraction of this axis's part to back off: in at 0, taken as out at 1.
            float inside = 0, outside = 1;
            for (int i = 0; i < ShipContact.BisectSteps; i++)
            {
                float mid = (inside + outside) * 0.5f;
                if (VoxelCollider.Overlaps(local - unit * (part * mid), HalfExtents, grid.Solid)) inside = mid;
                else outside = mid;
            }
            float depth = outside * MathF.Abs(part);
            if (depth < bestDepth)
            {
                bestDepth = depth;
                best = unit;
            }
        }
        return best;
    }

    /// <summary>Takes the motion along the grid's <paramref name="localAxis"/> out of the velocity.</summary>
    static void Stop(IGridShape grid, Vector3 localAxis, ref Vector3 velocity)
    {
        var axis = Vector3.Normalize(Vector3.TransformNormal(localAxis, grid.ToWorld));
        velocity -= axis * Vector3.Dot(velocity, axis);
    }

    /// <summary>Whether the body at <paramref name="point"/> may reach <paramref name="grid"/>'s blocks within
    /// <paramref name="extra"/> more metres.</summary>
    static bool Near(IGridShape grid, Vector3 point, float extra)
    {
        var (center, radius) = grid.Extent;
        if (float.IsPositiveInfinity(radius)) return true;
        float reach = radius + Margin + extra;
        return Vector3.DistanceSquared(center, point) <= reach * reach;
    }

    /// <summary>Grid <paramref name="index"/>: the world first, then the ships in order.</summary>
    static IGridShape Grid(int index, IGridShape world, IReadOnlyList<IGridShape> ships) =>
        index == 0 ? world : ships[index - 1];
}
