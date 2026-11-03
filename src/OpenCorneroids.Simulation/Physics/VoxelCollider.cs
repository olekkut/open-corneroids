using System.Numerics;

namespace OpenCorneroids.Simulation.Physics;

/// <summary>
/// Axis-aligned box against a grid of unit blocks; block (x,y,z) occupies [x,x+1)×[y,y+1)×[z,z+1).
/// Each axis is swept cell by cell rather than stepped, so a fast body cannot skip over a thin wall no matter
/// how large one frame's movement is, and the cost grows with distance travelled, not with speed squared.
/// </summary>
public static class VoxelCollider
{
    /// <summary>Gap left between a stopped box and the face it hit. Without it, float rounding near the
    /// world's far corner (coordinates around 128) can leave the box a hair inside the block it touched.</summary>
    public const float Skin = 1e-4f;

    /// <summary>How deep an overlap must be before it counts. Larger than <see cref="Skin"/> so a box resting
    /// against a face is never mistaken for one inside the neighbouring block.</summary>
    const float Tolerance = 1e-3f;

    /// <summary>Longest distance one <see cref="Move"/> sweeps per axis; anything beyond is cut off.</summary>
    public const float MaxAxisDistance = 4096f;

    /// <summary>Boxes reaching past this coordinate are left alone. Beyond 2^24 a float cannot hold every
    /// block face, so collision there would be noise, and the cell indices must stay well inside int range
    /// or the sweeps would wrap around and never finish.</summary>
    public const float MaxCoordinate = 16_777_216f;

    /// <summary>
    /// Moves the box by <paramref name="delta"/>, X first, then Y, then Z, each from where the previous axis
    /// left it. Doing the axes separately is what makes a diagonal move into a wall slide along it.
    /// A box already embedded in rock (a block placed on the player, a bad save) may move in any direction
    /// that does not take it deeper into the blocks it overlaps, so it can get out without ever passing
    /// through them. Boxes that are not finite or lie beyond <see cref="MaxCoordinate"/> do not move.
    /// </summary>
    public static CollisionResult Move(Vector3 center, Vector3 halfExtents, Vector3 delta, Func<int, int, int, bool> isSolid)
    {
        var min = center - halfExtents;
        var max = center + halfExtents;
        if (!Usable(min, max)) return new CollisionResult(center, false, false, false);
        bool hitX = SweepAxis(0, ref min, ref max, Finite(delta.X), isSolid);
        bool hitY = SweepAxis(1, ref min, ref max, Finite(delta.Y), isSolid);
        bool hitZ = SweepAxis(2, ref min, ref max, Finite(delta.Z), isSolid);
        return new CollisionResult(min + halfExtents, hitX, hitY, hitZ);
    }

    /// <summary>True when the box intersects a solid block by more than a rounding-error sliver. Always false
    /// for a box <see cref="Move"/> would refuse to move (not finite, or beyond <see cref="MaxCoordinate"/>).</summary>
    public static bool Overlaps(Vector3 center, Vector3 halfExtents, Func<int, int, int, bool> isSolid)
    {
        var min = center - halfExtents;
        var max = center + halfExtents;
        if (!Usable(min, max)) return false;
        var (x0, x1) = Cells(min.X, max.X);
        var (y0, y1) = Cells(min.Y, max.Y);
        var (z0, z1) = Cells(min.Z, max.Z);
        for (int z = z0; z <= z1; z++)
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            if (isSolid(x, y, z)) return true;
        return false;
    }

    /// <summary>
    /// The nearest whole-block offset of <paramref name="center"/> where the box fits, or null if nothing
    /// within <paramref name="maxRadius"/> blocks does. Offsets are whole blocks so the box keeps its
    /// alignment to the grid. Shells grow outwards one block at a time; within a shell, straight up is tried
    /// first (spawning on top of an asteroid reads better than inside a side cave), then the remaining
    /// candidates nearest first, higher ones winning ties.
    /// </summary>
    public static Vector3? FindFreeSpot(Vector3 center, Vector3 halfExtents, Func<int, int, int, bool> isSolid, int maxRadius = 16)
    {
        if (!Overlaps(center, halfExtents, isSolid)) return center;
        var shell = new List<(int X, int Y, int Z)>();
        for (int r = 1; r <= maxRadius; r++)
        {
            var up = center + new Vector3(0, r, 0);
            if (!Overlaps(up, halfExtents, isSolid)) return up;

            shell.Clear();
            for (int dz = -r; dz <= r; dz++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) == r && (dx, dy, dz) != (0, r, 0))
                    shell.Add((dx, dy, dz));
            shell.Sort((a, b) =>
            {
                int byDistance = (a.X * a.X + a.Y * a.Y + a.Z * a.Z).CompareTo(b.X * b.X + b.Y * b.Y + b.Z * b.Z);
                if (byDistance != 0) return byDistance;
                // List.Sort is unstable; the remaining keys only make the choice repeatable.
                int byHeight = b.Y.CompareTo(a.Y);
                return byHeight != 0 ? byHeight : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X);
            });
            foreach (var (dx, dy, dz) in shell)
            {
                var candidate = center + new Vector3(dx, dy, dz);
                if (!Overlaps(candidate, halfExtents, isSolid)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Moves [min,max] along one axis, stopping at the first solid slab of cells in the way.</summary>
    static bool SweepAxis(int axis, ref Vector3 min, ref Vector3 max, float distance, Func<int, int, int, bool> isSolid)
    {
        if (distance == 0) return false;
        int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
        var (p0, p1) = Cells(Get(min, a1), Get(max, a1));
        var (q0, q1) = Cells(Get(min, a2), Get(max, a2));
        float lo = Get(min, axis), hi = Get(max, axis);
        float moved = distance;
        bool hit = false;

        // Cells the box is already in along this axis. Moving on is allowed only if it does not deepen the
        // overlap with a solid one: for +, that happens when the box reaches into the cell from below and
        // its leading face has not yet got through (and mirrored for -). Merely skipping these cells would
        // let a box that is a hair inside a wall slide straight through it.
        int inFirst = (int)MathF.Floor(lo + Tolerance), inLast = (int)MathF.Ceiling(hi - Tolerance) - 1;
        for (int i = inFirst; i <= inLast && !hit; i++)
        {
            bool deeper = distance > 0
                ? lo < i - Tolerance && hi < i + 1 - Tolerance
                : hi > i + 1 + Tolerance && lo > i + Tolerance;
            if (deeper && SlabSolid(axis, i, p0, p1, q0, q1, isSolid)) hit = true;
        }
        if (hit) return true;

        if (distance > 0)
        {
            for (int i = inLast + 1; i < hi + distance + Skin; i++)
            {
                if (!SlabSolid(axis, i, p0, p1, q0, q1, isSolid)) continue;
                moved = Math.Clamp(i - Skin - hi, 0, distance);
                hit = true;
                break;
            }
        }
        else
        {
            for (int i = inFirst - 1; i + 1 > lo + distance - Skin; i--)
            {
                if (!SlabSolid(axis, i, p0, p1, q0, q1, isSolid)) continue;
                moved = Math.Clamp(i + 1 + Skin - lo, distance, 0);
                hit = true;
                break;
            }
        }

        Set(ref min, axis, lo + moved);
        Set(ref max, axis, hi + moved);
        return hit;
    }

    static bool SlabSolid(int axis, int i, int p0, int p1, int q0, int q1, Func<int, int, int, bool> isSolid)
    {
        for (int q = q0; q <= q1; q++)
        for (int p = p0; p <= p1; p++)
        {
            bool solid = axis switch
            {
                0 => isSolid(i, p, q),
                1 => isSolid(q, i, p),
                _ => isSolid(p, q, i),
            };
            if (solid) return true;
        }
        return false;
    }

    /// <summary>Inclusive range of cell indices a span really occupies; touching a face does not count.
    /// A span too thin to reach into any cell by <see cref="Tolerance"/> (a flat or point-sized box on a
    /// cell boundary) still occupies the cell holding its middle, or it would collide with nothing at all.</summary>
    static (int First, int Last) Cells(float lo, float hi)
    {
        int first = (int)MathF.Floor(lo + Tolerance), last = (int)MathF.Ceiling(hi - Tolerance) - 1;
        if (last >= first) return (first, last);
        int middle = (int)MathF.Floor((lo + hi) * 0.5f);
        return (middle, middle);
    }

    static bool Usable(Vector3 min, Vector3 max) =>
        Within(min.X) && Within(min.Y) && Within(min.Z) && Within(max.X) && Within(max.Y) && Within(max.Z);

    static bool Within(float value) => float.IsFinite(value) && Math.Abs(value) <= MaxCoordinate;

    /// <summary>NaN or infinite input (a divide by a zero frame time upstream) must not freeze the game in an
    /// endless sweep, and no legitimate frame moves anything thousands of blocks.</summary>
    static float Finite(float value) => float.IsFinite(value) ? Math.Clamp(value, -MaxAxisDistance, MaxAxisDistance) : 0f;

    static float Get(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    static void Set(ref Vector3 v, int axis, float value)
    {
        switch (axis)
        {
            case 0: v.X = value; break;
            case 1: v.Y = value; break;
            default: v.Z = value; break;
        }
    }
}
