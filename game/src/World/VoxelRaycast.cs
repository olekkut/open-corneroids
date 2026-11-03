using Godot;

namespace OpenCorneroids.Game.World;

/// <summary>Grid traversal (Amanatides &amp; Woo): visits every cell a ray passes through, in order.</summary>
public static class VoxelRaycast
{
    /// <summary>
    /// Finds the first solid block within <paramref name="maxDistance"/>. <paramref name="before"/> is the empty
    /// cell the ray came from, where a new block would be placed.
    /// </summary>
    public static bool Cast(VoxelWorld world, Vector3 origin, Vector3 direction, float maxDistance, out Vector3I hit, out Vector3I before)
    {
        var dir = direction.Normalized();
        var cell = new Vector3I(Mathf.FloorToInt(origin.X), Mathf.FloorToInt(origin.Y), Mathf.FloorToInt(origin.Z));
        var step = new Vector3I(Math.Sign(dir.X), Math.Sign(dir.Y), Math.Sign(dir.Z));
        var tDelta = new Vector3(Delta(dir.X), Delta(dir.Y), Delta(dir.Z));
        var tMax = new Vector3(
            FirstBoundary(origin.X, cell.X, dir.X, tDelta.X),
            FirstBoundary(origin.Y, cell.Y, dir.Y, tDelta.Y),
            FirstBoundary(origin.Z, cell.Z, dir.Z, tDelta.Z));

        before = cell;
        float travelled = 0;
        while (travelled <= maxDistance)
        {
            if (world.Get(cell) != BlockPalette.Air)
            {
                hit = cell;
                return true;
            }
            before = cell;
            if (tMax.X < tMax.Y && tMax.X < tMax.Z) { cell.X += step.X; travelled = tMax.X; tMax.X += tDelta.X; }
            else if (tMax.Y < tMax.Z) { cell.Y += step.Y; travelled = tMax.Y; tMax.Y += tDelta.Y; }
            else { cell.Z += step.Z; travelled = tMax.Z; tMax.Z += tDelta.Z; }
        }
        hit = default;
        return false;
    }

    static float Delta(float d) => d == 0 ? float.PositiveInfinity : Math.Abs(1f / d);

    static float FirstBoundary(float origin, int cell, float d, float delta) =>
        d > 0 ? (cell + 1 - origin) * delta : d < 0 ? (origin - cell) * delta : float.PositiveInfinity;
}
