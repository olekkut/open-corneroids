using System.Numerics;

namespace OpenCorneroids.Simulation.Physics;

/// <summary>
/// A small box (a thrown torch) moving through every block grid at once: the static world and each ship, each in its
/// own axes. Unlike <see cref="RiderCollision"/> nothing rides anything here: the velocity is a world velocity, and a
/// ship that moves into the box pushes it out without carrying it along, as the original treats a thrown torch.
/// </summary>
public static class GridSweep
{
    /// <summary>Distance steps the push out of an overlapping grid tries, in cells.</summary>
    public const float PushStep = 0.02f;

    /// <summary>Beyond a grid's extent sphere by this much (plus the box's own size) nothing of it can touch the box.</summary>
    const float Margin = 1f;

    /// <summary>
    /// Moves the box at <paramref name="center"/> (world space, half size <paramref name="halfExtents"/> along each
    /// grid's own axes) by <paramref name="delta"/>. First, a grid that has come to overlap the box pushes it out the
    /// shortest way along one of its axes (up to <paramref name="maxPush"/>) and the part of the velocity going into
    /// that grid is removed. Then the move is swept against each grid in turn (<see cref="VoxelCollider.Move"/>); a
    /// blocked axis takes the velocity along that axis away, so the box slides along a wall and never bounces.
    /// </summary>
    /// <returns>The new centre.</returns>
    public static Vector3 Move(Vector3 center, Vector3 halfExtents, Vector3 delta, ref Vector3 velocity,
        IGridShape world, IReadOnlyList<IGridShape> ships, float maxPush = 1f)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ships);
        int count = ships.Count + 1;
        float size = halfExtents.Length();
        for (int i = 0; i < count; i++) center = PushOut(i, world, ships, center, halfExtents, size, ref velocity, maxPush);

        var end = center + delta;
        for (int i = 0; i < count; i++)
        {
            var grid = Grid(i, world, ships);
            if (!Near(grid, center, size + (end - center).Length())) continue;
            var local = Vector3.Transform(center, grid.ToLocal);
            var localDelta = Vector3.TransformNormal(end - center, grid.ToLocal);
            var result = VoxelCollider.Move(local, halfExtents, localDelta, grid.Solid);
            if (!result.HitAny) continue;
            if (result.HitX) Stop(grid, Vector3.UnitX, ref velocity);
            if (result.HitY) Stop(grid, Vector3.UnitY, ref velocity);
            if (result.HitZ) Stop(grid, Vector3.UnitZ, ref velocity);
            // The grids after this one sweep from the same start to where this one stopped the box.
            end = Vector3.Transform(result.Position, grid.ToWorld);
        }
        return end;
    }

    /// <summary>
    /// When <paramref name="grid"/> overlaps the box, the shortest move along one of the grid's six axis directions
    /// (up first on a tie) to a spot free of that grid and of every other grid, so a ship pressing the box against
    /// rock does not push it into the rock; the velocity going into the grid along that way is removed. With no free
    /// spot within <paramref name="maxPush"/> the box stays where it is.
    /// </summary>
    static Vector3 PushOut(int index, IGridShape world, IReadOnlyList<IGridShape> ships, Vector3 center, Vector3 half,
        float size, ref Vector3 velocity, float maxPush)
    {
        var grid = Grid(index, world, ships);
        if (!Near(grid, center, size)) return center;
        var local = Vector3.Transform(center, grid.ToLocal);
        var solid = grid.Solid;
        if (!VoxelCollider.Overlaps(local, half, solid)) return center;
        int steps = float.IsFinite(maxPush) ? (int)MathF.Floor(Math.Clamp(maxPush, 0, 16) / PushStep + 1e-3f) : 0;
        for (int step = 1; step <= steps; step++)
        {
            float distance = step * PushStep;
            foreach (var direction in Directions)
            {
                var moved = local + direction * distance;
                if (VoxelCollider.Overlaps(moved, half, solid)) continue;
                var spot = Vector3.Transform(moved, grid.ToWorld);
                if (BlockedElsewhere(index, world, ships, spot, half, size)) continue;
                var normal = Vector3.Normalize(Vector3.TransformNormal(direction, grid.ToWorld));
                float into = Vector3.Dot(velocity, normal);
                if (into < 0) velocity -= normal * into;
                return spot;
            }
        }
        return center;
    }

    /// <summary>Whether the box at <paramref name="center"/> overlaps any grid other than the one at
    /// <paramref name="index"/>.</summary>
    static bool BlockedElsewhere(int index, IGridShape world, IReadOnlyList<IGridShape> ships, Vector3 center,
        Vector3 half, float size)
    {
        for (int i = 0; i <= ships.Count; i++)
        {
            if (i == index) continue;
            var other = Grid(i, world, ships);
            if (Near(other, center, size) && VoxelCollider.Overlaps(Vector3.Transform(center, other.ToLocal), half, other.Solid))
                return true;
        }
        return false;
    }

    static readonly Vector3[] Directions =
        [Vector3.UnitY, -Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ];

    /// <summary>Takes the motion along the grid's <paramref name="localAxis"/> out of the velocity.</summary>
    static void Stop(IGridShape grid, Vector3 localAxis, ref Vector3 velocity)
    {
        var axis = Vector3.Normalize(Vector3.TransformNormal(localAxis, grid.ToWorld));
        velocity -= axis * Vector3.Dot(velocity, axis);
    }

    static bool Near(IGridShape grid, Vector3 point, float extra)
    {
        var (center, radius) = grid.Extent;
        if (float.IsPositiveInfinity(radius)) return true;
        float reach = radius + Margin + extra;
        return Vector3.DistanceSquared(center, point) <= reach * reach;
    }

    static IGridShape Grid(int index, IGridShape world, IReadOnlyList<IGridShape> ships) =>
        index == 0 ? world : ships[index - 1];
}
