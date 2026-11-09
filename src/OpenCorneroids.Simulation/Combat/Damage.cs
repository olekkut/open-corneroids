using System.Numerics;

namespace OpenCorneroids.Simulation.Combat;

/// <summary>
/// How the player gets hurt, as in the original: armor takes off a share that grows with the logarithm of the armor
/// rating, hitting a surface hard hurts in proportion to the speed into it, and a respawn point lies a fixed distance
/// band away from where the player died. Every number is a parameter so the console settings can change it.
/// </summary>
public static class Damage
{
    /// <summary>Armor rating above this counts as this, as in the original.</summary>
    public const int ArmorCap = 60;
    /// <summary>The divisor in the armor rule, as in the original: reduction = ln(armor + 1) / 6.</summary>
    public const double ArmorScale = 6;
    /// <summary>Speed into a surface, m/s, from which a collision hurts, as in the original.</summary>
    public const float ImpactSpeed = 8;
    /// <summary>Damage per m/s above <see cref="ImpactSpeed"/>, as in the original.</summary>
    public const float ImpactDamagePerSpeed = 7;
    /// <summary>A shot hits a player whose centre it passes this close to: 1.5 × the 0.49 m body radius, as in the original.</summary>
    public const float ShotHitRadius = 0.735f;
    /// <summary>Respawn distance band from the death point, as in the original.</summary>
    public const float RespawnMin = 64, RespawnMax = 95;
    /// <summary>Health regained per second, as in the original (one point every two seconds).</summary>
    public const double HealthRegenPerSecond = 0.5;

    /// <summary>The fraction of damage <paramref name="armor"/> takes off: ln(armor + 1) / <paramref name="scale"/>,
    /// the armor first limited to 0..<paramref name="cap"/>, the fraction to 0..1.</summary>
    public static double ArmorReduction(int armor, int cap = ArmorCap, double scale = ArmorScale)
    {
        if (!(scale > 0) || !double.IsFinite(scale)) return 0;
        int a = Math.Clamp(armor, 0, Math.Max(0, cap));
        return Math.Clamp(Math.Log(a + 1) / scale, 0, 1);
    }

    /// <summary>What is left of <paramref name="damage"/> after armor, rounded down to a whole number, as in the original.</summary>
    public static int AfterArmor(double damage, int armor, int cap = ArmorCap, double scale = ArmorScale)
    {
        if (!(damage > 0) || !double.IsFinite(damage)) return 0;
        return (int)Math.Floor(damage * (1 - ArmorReduction(armor, cap, scale)));
    }

    /// <summary>
    /// Damage from hitting a surface at <paramref name="speedIntoSurface"/> m/s (only the part straight into it counts):
    /// nothing below <paramref name="threshold"/>, then <paramref name="perSpeed"/> per m/s above it, rounded down.
    /// </summary>
    public static int Impact(float speedIntoSurface, float threshold = ImpactSpeed, float perSpeed = ImpactDamagePerSpeed)
    {
        if (!float.IsFinite(speedIntoSurface) || speedIntoSurface < threshold || !(perSpeed > 0)) return 0;
        return (int)Math.Floor(perSpeed * (speedIntoSurface - threshold));
    }

    /// <summary>
    /// Where along the segment <paramref name="from"/>→<paramref name="to"/> it first comes within
    /// <paramref name="radius"/> of <paramref name="center"/>, as a fraction 0..1; null when it never does.
    /// </summary>
    public static float? SegmentHitsSphere(Vector3 from, Vector3 to, Vector3 center, float radius)
    {
        if (!(radius > 0)) return null;
        var d = to - from;
        var m = from - center;
        float c = m.LengthSquared() - radius * radius;
        if (c <= 0) return 0;                        // starts inside
        float a = d.LengthSquared();
        if (a <= 0) return null;
        float b = Vector3.Dot(m, d);
        if (b >= 0) return null;                     // heading away
        float disc = b * b - a * c;
        if (disc < 0) return null;
        float t = (-b - MathF.Sqrt(disc)) / a;
        return t <= 1 ? t : null;
    }

    /// <summary>
    /// A respawn point between <paramref name="min"/> and <paramref name="max"/> from <paramref name="death"/>, in a
    /// direction whose x, y and z are all positive, as in the original. <paramref name="u1"/>..<paramref name="u4"/> are
    /// uniform in [0, 1), so tests can pick them; the caller tries again while the point is inside a block.
    /// </summary>
    public static Vector3 RespawnPoint(Vector3 death, double u1, double u2, double u3, double u4,
        float min = RespawnMin, float max = RespawnMax)
    {
        if (max < min) (min, max) = (max, min);
        // At least a little of every axis, so the direction is never flat.
        var direction = Vector3.Normalize(new Vector3((float)u1 + 0.05f, (float)u2 + 0.05f, (float)u3 + 0.05f));
        float distance = Math.Max(0, min + (float)u4 * (max - min));
        return death + direction * distance;
    }
}
