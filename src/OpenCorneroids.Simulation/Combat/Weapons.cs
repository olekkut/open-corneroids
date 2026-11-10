using System.Numerics;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Combat;

/// <summary>
/// Firing rules for hand weapons and ship guns. The numbers come from the itemset (<c>reload</c>, <c>projectileId</c>,
/// <c>pushback</c>, <c>spread</c>, the item's <c>powerUsage</c>, and the projectile's speed, damage, lifetime and
/// ricochet angle). Spread, where a shot starts and what it does to suit power are as in the original; recoil that
/// moves the shooter and ricochets are ours and tunable (docs/design/gameplay-observations.md §3).
/// </summary>
public static class Weapons
{
    /// <summary>
    /// Suit power one shot costs: the weapon's <c>powerUsage</c>. One vanilla weapon has a negative value (the Heavy
    /// Plasma Launcher, -25): as in the original, such a shot always fires and gives that much power back
    /// (<c>sv_weapon_negative_power 1</c>). Ours, with <paramref name="negativeGivesPower"/> false: the size is what it costs.
    /// </summary>
    public static int ShotCost(ItemDef weapon, bool negativeGivesPower = true)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return negativeGivesPower ? weapon.PowerUsage : Math.Abs(weapon.PowerUsage);
    }

    /// <summary>How far right of the eye a hand weapon's shot starts, m, as in the original.</summary>
    public const float ShotRight = 0.3f;

    /// <summary>How far below the eye a hand weapon's shot starts, m, as in the original.</summary>
    public const float ShotDown = 0.2f;

    /// <summary>
    /// Where a hand weapon's shot starts, as in the original: the gun sits 0.3 m right of the eye and 0.2 m below it,
    /// and the shot leaves <paramref name="forwardOffset"/> m (<c>sv_shot_forward_offset</c>, 1) further along the aim.
    /// <paramref name="right"/> and <paramref name="up"/> are the view's unit axes.
    /// </summary>
    public static Vector3 ShotOrigin(Vector3 eye, Vector3 aim, Vector3 right, Vector3 up, float forwardOffset = 1)
    {
        var forward = aim == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(aim);
        return eye + right * ShotRight - up * ShotDown + forward * forwardOffset;
    }

    /// <summary>Seed of the original's fixed list of spread directions.</summary>
    public const int SpreadSeed = 24092004;

    /// <summary>Entries in the list of spread directions; the index wraps here.</summary>
    public const int SpreadCount = 256;

    static readonly Vector3[] spreadVectors = BuildSpreadVectors();

    /// <summary>
    /// The original's fixed list of spread directions: <see cref="SpreadCount"/> unit vectors, each made from three
    /// uniform numbers in -0.5..0.5 from a generator seeded with <see cref="SpreadSeed"/>, then normalised. They come
    /// from a cube, so they are not spread evenly over the sphere.
    /// </summary>
    public static IReadOnlyList<Vector3> SpreadVectors => spreadVectors;

    static Vector3[] BuildSpreadVectors()
    {
        // A seeded System.Random keeps the same sequence across .NET versions; the tests pin a few entries.
        var random = new Random(SpreadSeed);
        var list = new Vector3[SpreadCount];
        for (int i = 0; i < list.Length; i++)
        {
            var v = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
            list[i] = v == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(v);
        }
        return list;
    }

    /// <summary>
    /// A shot's direction as in the original (<c>sv_weapon_spread_mode 0</c>): the unit aim plus
    /// <paramref name="spread"/> (held within 0..0.5) times entry <paramref name="index"/> of <see cref="SpreadVectors"/>
    /// (wrapping at 256). It is not normalised: the shot flies at its projectile's speed times this length, so the speed
    /// varies by up to ±spread.
    /// </summary>
    public static Vector3 OriginalSpread(Vector3 aim, float spread, int index)
    {
        var forward = aim == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(aim);
        if (!float.IsFinite(spread) || spread <= 0) return forward;
        int i = ((index % SpreadCount) + SpreadCount) % SpreadCount;
        return forward + Math.Min(spread, WeaponComponent.MaxSpread) * spreadVectors[i];
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
    /// Ours (<c>sv_weapon_spread_mode 1</c>): <paramref name="forward"/> turned by a random angle of up to
    /// <paramref name="spread"/> radians, evenly over the cone's solid angle, at full speed. <paramref name="u1"/> and <paramref name="u2"/> are uniform in [0, 1), so tests can pick them.
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

/// <summary>
/// Which entry of <see cref="Weapons.SpreadVectors"/> each weapon type uses next, as in the original: one index per
/// weapon item, moving on by one per shot and wrapping at 256.
/// </summary>
public sealed class SpreadSequence
{
    readonly Dictionary<int, int> next = new();

    /// <summary>The index the next shot of weapon <paramref name="weaponId"/> uses.</summary>
    public int Peek(int weaponId) => next.GetValueOrDefault(weaponId);

    /// <summary>The direction of the next shot of weapon <paramref name="weaponId"/> (<see cref="Weapons.OriginalSpread"/>),
    /// moving its index on.</summary>
    public Vector3 Next(int weaponId, Vector3 aim, float spread)
    {
        int index = next.GetValueOrDefault(weaponId);
        next[weaponId] = (index + 1) % Weapons.SpreadCount;
        return Weapons.OriginalSpread(aim, spread, index);
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
