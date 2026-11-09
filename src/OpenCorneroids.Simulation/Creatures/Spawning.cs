using System.Numerics;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Creatures;

/// <summary>Where a parasite can sit: an empty cell, the asteroid or mineral block under it, and the way out.</summary>
public readonly record struct SurfaceSpot(Int3 Cell, Int3 Floor, Int3 Normal);

/// <summary>
/// The original's spawning rules, as pure functions the game feeds with its grids. Every second, near asteroids,
/// parasites appear on asteroid and mineral surfaces 16 to ~24 units from the player; far from any, scavengers would
/// (they never do in the original).
/// </summary>
public static class Spawning
{
    /// <summary>Seconds between spawn cycles, as in the original.</summary>
    public const double CycleSeconds = 1;
    /// <summary>Half the side of the cube around the player where spawn points are tried.</summary>
    public const float TrialHalfSize = 14;
    /// <summary>No creature appears closer to the player than this.</summary>
    public const float MinPlayerDistance = 16;
    /// <summary>No creature appears within this of another.</summary>
    public const float MinCreatureDistance = 8;
    /// <summary>How many cells out from a buried trial point the surface is looked for.</summary>
    public const int SurfaceSteps = 5;
    /// <summary>An entity takes parasites while the player is within its bounding sphere plus this.</summary>
    public const float EntityMargin = 5;
    /// <summary>Parasites near an entity whose nearest block is within this of the player; else flyers.</summary>
    public const float ParasiteRange = 16;
    /// <summary>Parasites allowed per 16³ sector of blocks an entity has.</summary>
    public const float ParasitesPerSector = 1;
    public const int MaxMineralParasitesPerCycle = 4;
    public const int MaxEggsPerCycle = 2;
    /// <summary>Creatures further than this from the player are always removed.</summary>
    public const float MaxUpkeep = 128;
    /// <summary>Flyers only appear while at most two creatures are within this of the player.</summary>
    public const float FlyerCrowdRange = 64;
    public const int FlyerCrowdLimit = 2;
    public const float FlyerMinDistance = 32, FlyerMaxDistance = 64;
    public const int FlyerTries = 101;

    /// <summary>
    /// A trial point for a parasite: the player plus a uniform offset in a cube of ±<see cref="TrialHalfSize"/>; a point
    /// closer than <see cref="MinPlayerDistance"/> is pushed out to exactly that distance along the same direction.
    /// </summary>
    public static Vector3 TrialPoint(Vector3 player, double u1, double u2, double u3)
    {
        var offset = new Vector3((float)(u1 * 2 - 1), (float)(u2 * 2 - 1), (float)(u3 * 2 - 1)) * TrialHalfSize;
        float distance = offset.Length();
        if (distance < 1e-4f) offset = new Vector3(0, MinPlayerDistance, 0);
        else if (distance < MinPlayerDistance) offset *= MinPlayerDistance / distance;
        return player + offset;
    }

    /// <summary>
    /// From <paramref name="local"/> (inside a solid cell of the grid) walks each axis in the original's order up to
    /// <see cref="SurfaceSteps"/> cells; the first empty cell whose cell behind it is food is the spot. Null when the
    /// point is not in rock or no surface is near.
    /// </summary>
    public static SurfaceSpot? FindSurface(Vector3 local, Func<Int3, bool> isSolid, Func<Int3, bool> isFood)
    {
        var start = Int3.Floor(local);
        if (!isSolid(start)) return null;
        foreach (var dir in Creature.Directions)
        {
            var cell = start;
            for (int step = 0; step < SurfaceSteps; step++)
            {
                cell += dir;
                if (isSolid(cell)) continue;
                var floor = cell - dir;
                if (isFood(floor)) return new SurfaceSpot(cell, floor, dir);
            }
        }
        return null;
    }

    /// <summary>How many parasites an entity with <paramref name="sectors"/> sectors of blocks may hold.</summary>
    public static int ParasiteCap(int sectors, float perSector = ParasitesPerSector) =>
        (int)Math.Floor(Math.Max(0, sectors) * Math.Max(0, perSector));

    /// <summary>A flyer's trial point: a random direction, <see cref="FlyerMinDistance"/>..<see cref="FlyerMaxDistance"/> away.</summary>
    public static Vector3 FlyerPoint(Vector3 player, double u1, double u2, double u3)
    {
        double z = u1 * 2 - 1, phi = u2 * Math.PI * 2, s = Math.Sqrt(Math.Max(0, 1 - z * z));
        var direction = new Vector3((float)(s * Math.Cos(phi)), (float)(s * Math.Sin(phi)), (float)z);
        return player + direction * (float)(FlyerMinDistance + u3 * (FlyerMaxDistance - FlyerMinDistance));
    }

    /// <summary>
    /// The touch between the player (a sphere of <paramref name="playerRadius"/>) and a creature, as in the original:
    /// how deep they overlap and the unit direction from the creature to the player; null when they don't touch.
    /// </summary>
    public static (float Depth, Vector3 Normal)? Touch(Vector3 player, float playerRadius, Vector3 creature, float creatureRadius)
    {
        var between = player - creature;
        float distance = between.Length();
        float depth = playerRadius + creatureRadius - distance;
        if (depth <= 0) return null;
        return (depth, distance > 1e-5f ? between / distance : Vector3.UnitY);
    }

    /// <summary>The AI clock a new creature starts with, so creatures don't all think in the same frame.</summary>
    public static double StaggeredClock(int creatureCount, int aiCycleMs) => aiCycleMs <= 0 ? 0 : creatureCount * 397 % aiCycleMs;
}
