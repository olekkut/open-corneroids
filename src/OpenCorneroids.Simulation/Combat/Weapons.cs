using System.Numerics;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Combat;

/// <summary>
/// Firing rules for hand weapons and ship guns. The numbers come from the itemset (<c>reload</c>, <c>projectileId</c>,
/// <c>pushback</c>, <c>spread</c>, the item's <c>powerUsage</c>, and the projectile's speed, damage, lifetime and
/// ricochet angle); how they combine is ours and tunable (docs/design/gameplay-observations.md §3).
/// </summary>
public static class Weapons
{
    /// <summary>
    /// Suit power one shot costs: the weapon's <c>powerUsage</c>. One vanilla weapon has a negative value (the Heavy
    /// Plasma Launcher, -25); a weapon that gives power back makes no sense, so the size is what counts.
    /// </summary>
    public static int ShotCost(ItemDef weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return Math.Abs(weapon.PowerUsage);
    }

    /// <summary>Scales <c>pushback</c> × projectile speed into m/s. Ours, tunable: at 1 a held SMG pushed the player
    /// back about 20 m in five seconds of fire, and at 0.2 a minigun still did.</summary>
    public const float RecoilScale = 0.05f;

    /// <summary>
    /// The shooter's velocity change for one shot, opposite the shot: <c>pushback</c> times the projectile's speed times
    /// <see cref="RecoilScale"/>. The original's unit for pushback is unknown; this makes a pistol shot a nudge
    /// (about 0.06 m/s) and the plasma launcher a shove (0.25 m/s).
    /// </summary>
    /// <param name="scale">m/s per unit of pushback × speed (<c>sv_recoil_scale</c>).</param>
    public static Vector3 Recoil(Vector3 direction, float pushback, ProjectileDef projectile, float scale = RecoilScale)
    {
        ArgumentNullException.ThrowIfNull(projectile);
        if (direction == Vector3.Zero) return Vector3.Zero;
        return -Vector3.Normalize(direction) * pushback * projectile.Speed * scale;
    }

    /// <summary>
    /// <paramref name="forward"/> turned by a random angle of up to <paramref name="spread"/> radians, evenly over the
    /// cone's solid angle. <paramref name="u1"/> and <paramref name="u2"/> are uniform in [0, 1), so tests can pick them.
    /// </summary>
    public static Vector3 Spread(Vector3 forward, float spread, double u1, double u2)
    {
        var f = Vector3.Normalize(forward);
        if (spread <= 0) return f;
        double cosMax = Math.Cos(Math.Min(spread, Math.PI));
        double cos = 1 - u1 * (1 - cosMax);
        double sin = Math.Sqrt(Math.Max(0, 1 - cos * cos));
        double phi = 2 * Math.PI * u2;
        // Any two axes perpendicular to f.
        var helper = MathF.Abs(f.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var a = Vector3.Normalize(Vector3.Cross(f, helper));
        var b = Vector3.Cross(f, a);
        return Vector3.Normalize(f * (float)cos + a * (float)(sin * Math.Cos(phi)) + b * (float)(sin * Math.Sin(phi)));
    }

    /// <summary>
    /// A projectile glances off a surface when it meets it at less than <paramref name="ricochetAngle"/> radians (the
    /// itemset's <c>ricochetAngle</c>; 0 never glances). Returns the mirrored velocity then, or null when it hits.
    /// </summary>
    /// <param name="normal">The surface's outward unit normal.</param>
    public static Vector3? Ricochet(Vector3 velocity, Vector3 normal, float ricochetAngle)
    {
        if (ricochetAngle <= 0 || velocity == Vector3.Zero) return null;
        var v = Vector3.Normalize(velocity);
        float into = -Vector3.Dot(v, normal);   // sine of the angle between the path and the surface
        if (into <= 0) return null;              // moving away from the surface: not a hit at all
        if (MathF.Asin(Math.Clamp(into, 0, 1)) >= ricochetAngle) return null;
        return velocity - 2 * Vector3.Dot(velocity, normal) * normal;
    }
}

/// <summary>A weapon's reload: it can fire again <c>reload</c> ms after the last shot.</summary>
public sealed class Trigger
{
    double ready;

    /// <summary>Seconds until the next shot is allowed.</summary>
    public double Wait(double now) => Math.Max(0, ready - now);

    /// <summary>Fires if reloaded, starting the next reload. <paramref name="now"/> is game time in seconds.</summary>
    public bool TryFire(double now, int reloadMs)
    {
        if (now < ready) return false;
        ready = now + Math.Max(0, reloadMs) / 1000.0;
        return true;
    }
}

/// <summary>
/// Damage that blocks have taken but not yet enough to break: a block breaks when the damage it took reaches its
/// <c>health</c> (a block with health 0 breaks at the first hit). Keyed by whatever identifies a block to the caller.
/// Not saved: a world reloads with every block whole. Ours.
/// </summary>
public sealed class BlockDamage<TKey> where TKey : notnull
{
    readonly Dictionary<TKey, double> taken = new();

    public double Taken(TKey block) => taken.GetValueOrDefault(block);

    /// <summary>Adds <paramref name="damage"/>; true when that breaks the block, which is then forgotten.</summary>
    public bool Hit(TKey block, double damage, double health)
    {
        var total = taken.GetValueOrDefault(block) + Math.Max(0, damage);
        if (total >= health)
        {
            taken.Remove(block);
            return true;
        }
        taken[block] = total;
        return false;
    }

    /// <summary>
    /// A shot with <paramref name="damage"/> left meets the block, as in the original: the block has hit points starting
    /// at its <paramref name="health"/> (at least 1); the shot takes off the smaller of the block's remaining hit points
    /// and its own damage, and loses that much. Returns how much the shot lost and whether the block broke (then it is
    /// forgotten). A shot with damage left after breaking a block flies on through.
    /// </summary>
    public (double Absorbed, bool Broken) Absorb(TKey block, double damage, double health)
    {
        double left = Math.Max(1, health) - taken.GetValueOrDefault(block);
        double absorbed = Math.Clamp(damage, 0, Math.Max(0, left));
        if (absorbed >= left)
        {
            taken.Remove(block);
            return (absorbed, true);
        }
        taken[block] = taken.GetValueOrDefault(block) + absorbed;
        return (absorbed, false);
    }

    /// <summary>Forgets every block <paramref name="gone"/> says is no longer there.</summary>
    public void ForgetWhere(Func<TKey, bool> gone)
    {
        foreach (var key in taken.Keys.Where(gone).ToList()) taken.Remove(key);
    }

    public int Count => taken.Count;
}
