using System.Numerics;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Creatures;

public enum CreatureKind { MineralParasite, SwarmEgg, Needle, Scavenger }

/// <summary>Particles a creature asks the game to show.</summary>
public enum CreatureEffect
{
    /// <summary>Green goo from a hatching egg.</summary>
    Goo,
    /// <summary>A purple trail speck behind a needle.</summary>
    Trail,
}

/// <summary>
/// What a creature can see and change. Creatures that sit on an asteroid or ship ("boarded") keep their position in
/// that grid's own cell coordinates, so they move with it; free flyers use world coordinates (grid −1).
/// </summary>
public interface ICreatureWorld
{
    Random Random { get; }

    /// <summary>The player's centre in world space; null when there is none to go after (dead, creative, gone).</summary>
    Vector3? Player { get; }

    /// <summary>The player sits at a console, piloting.</summary>
    bool PlayerPiloting { get; }

    bool GridExists(int grid);
    bool IsSolid(int grid, Int3 cell);

    /// <summary>An asteroid or mineral block: what parasites live on and eat.</summary>
    bool IsFood(int grid, Int3 cell);

    /// <summary>Something that identifies the block in the cell (a palette value), to notice it was replaced; 0 = air.</summary>
    int BlockAt(int grid, Int3 cell);

    /// <summary>A parasite has eaten the block: it goes, dropping nothing.</summary>
    void EatBlock(int grid, Int3 cell);

    Vector3 ToWorld(int grid, Vector3 local);
    Vector3 DirectionToWorld(int grid, Vector3 local);
    Vector3 ToLocal(int grid, Vector3 world);

    /// <summary>A new creature (a hatched needle).</summary>
    void Add(Creature creature);

    /// <summary>A scavenger's bolt, world space.</summary>
    void FireBolt(Vector3 from, Vector3 direction);

    void Emit(CreatureEffect effect, Vector3 world, Vector3 velocity);
}

/// <summary>
/// A creature, as in the original: a sphere of mass 1 with health that comes back a point every two seconds, a top
/// speed it bleeds back down to (8 u/s²) and friction when nothing pushes it (6 u/s²). Every <see cref="AiCycleMs"/>
/// it thinks, and that frame it does not move. Removal (death, despawn, expiry) shrinks it to nothing in 0.2 s.
/// </summary>
public abstract class Creature
{
    /// <summary>How long the shrink before a creature bursts takes, as in the original.</summary>
    public const double RemoveSeconds = 0.2;
    /// <summary>Health regained every <see cref="RegenSeconds"/>, as in the original.</summary>
    public const double RegenSeconds = 2;
    public const float DefaultMaxSpeed = 4;
    public const float OverSpeedDeceleration = 8;
    public const float Friction = 6;

    protected Creature(CreatureKind kind, float radius, double health, double maxHealth, int aiCycleMs, int grid, Vector3 position)
    {
        Kind = kind;
        Radius = radius;
        Health = health;
        MaxHealth = maxHealth;
        AiCycleMs = aiCycleMs;
        Grid = grid;
        Position = position;
    }

    public CreatureKind Kind { get; }
    public float Radius { get; }
    public double Health { get; private set; }
    public double MaxHealth { get; }
    public int AiCycleMs { get; }

    /// <summary>The grid it sits on, or −1 flying free in world space.</summary>
    public int Grid { get; protected set; }

    /// <summary>In <see cref="Grid"/>'s cell coordinates, or world space when free.</summary>
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }

    /// <summary>Which way it faces (its model's up), in the same frame as <see cref="Position"/>.</summary>
    public Vector3 Facing { get; protected set; } = Vector3.UnitY;

    public virtual float MaxSpeed => DefaultMaxSpeed;

    /// <summary>Damage on touching the player (before armor); 0 when it doesn't hurt (yet).</summary>
    public virtual int TouchDamage => 0;

    /// <summary>Whether blocks push it out; off for eggs and for a parasite while it eats.</summary>
    public virtual bool CollidesWithBlocks => true;

    /// <summary>Gone from the despawn distance: parasites 32, flyers 64.</summary>
    public abstract float UpkeepDistance { get; }

    /// <summary>The player is its target (chosen when it turned hostile).</summary>
    public bool HasTarget { get; protected set; }

    public bool Removing { get; private set; }
    double _removeTime;

    /// <summary>Done shrinking: burst it and drop it.</summary>
    public bool Gone => Removing && _removeTime >= RemoveSeconds;

    /// <summary>Model scale: 1, shrinking to 0 while it is removed.</summary>
    public float Scale => Removing ? (float)Math.Clamp(1 - _removeTime / RemoveSeconds, 0, 1) : 1;

    /// <summary>Milliseconds since its last think; starts staggered so creatures don't all think together.</summary>
    public double AiClockMs { get; set; }

    double _regen;
    Vector3 _force;
    bool _forced;

    /// <summary>Age in seconds, for animation and lifetimes.</summary>
    public double Age { get; private set; }

    public void Remove() => Removing = true;

    /// <summary>Applies a force (mass 1, so an acceleration) for this frame.</summary>
    protected void Push(Vector3 force)
    {
        _force += force;
        _forced = true;
    }

    /// <summary>Advances one frame of <paramref name="dt"/> seconds.</summary>
    public void Update(double dt, ICreatureWorld world)
    {
        if (dt <= 0) return;
        if (Removing)
        {
            _removeTime += dt;
            return;
        }
        Age += dt;
        if (Grid >= 0 && !world.GridExists(Grid))
        {
            Remove();
            return;
        }
        _regen += dt;
        while (_regen >= RegenSeconds)
        {
            _regen -= RegenSeconds;
            if (Health < MaxHealth) Health = Math.Min(MaxHealth, Health + 1);
        }

        AiClockMs += dt * 1000;
        if (AiClockMs >= AiCycleMs)
        {
            AiClockMs -= AiCycleMs;
            Think(world);
            return;   // that frame it does not move, as in the original
        }
        Act(dt, world);
        if (Removing) return;
        Integrate((float)dt);
    }

    void Integrate(float dt)
    {
        var v = Velocity;
        if (_forced) v += _force * dt;
        float speed = v.Length();
        if (speed > MaxSpeed)
        {
            float slowed = Math.Max(MaxSpeed, speed - OverSpeedDeceleration * dt);
            v *= slowed / speed;
        }
        else if (!_forced && speed > 0)
        {
            float slowed = speed - Friction * dt;
            v = slowed <= 0 ? Vector3.Zero : v * (slowed / speed);
        }
        Velocity = v;
        Position += v * dt;
        _force = Vector3.Zero;
        _forced = false;
    }

    /// <summary>The periodic decision, every <see cref="AiCycleMs"/>.</summary>
    protected abstract void Think(ICreatureWorld world);

    /// <summary>What it does every other frame (forces, timers, effects).</summary>
    protected virtual void Act(double dt, ICreatureWorld world) { }

    /// <summary>
    /// A shot carrying <paramref name="damage"/> hits: it loses the smaller of its damage and this creature's health
    /// (creatures have no armor), as in the original. Returns what the shot lost.
    /// </summary>
    /// <param name="byPlayer">The player's own weapon fired it.</param>
    /// <param name="shooterDistance">How far the shooter is; infinite when unknown.</param>
    public double Absorb(double damage, bool byPlayer, float shooterDistance, ICreatureWorld world)
    {
        if (Removing || !(damage > 0)) return 0;
        double absorbed = Math.Min(Health, damage);
        Health -= absorbed;
        if (Health <= 0)
        {
            Health = 0;
            Killed(world);
            Remove();
        }
        else Shot(byPlayer, shooterDistance, world);
        return absorbed;
    }

    /// <summary>Reaction to a hit it survived.</summary>
    protected virtual void Shot(bool byPlayer, float shooterDistance, ICreatureWorld world) { }

    /// <summary>Reaction to being killed (before the shrink).</summary>
    protected virtual void Killed(ICreatureWorld world) { }

    /// <summary>Blocks pushed it back; needles burst on it.</summary>
    public virtual void HitBlock() { }

    /// <summary>World position.</summary>
    public Vector3 WorldPosition(ICreatureWorld world) => Grid >= 0 ? world.ToWorld(Grid, Position) : Position;

    /// <summary>Distance to the player, or infinity without one.</summary>
    protected float PlayerDistance(ICreatureWorld world) =>
        world.Player is { } p ? Vector3.Distance(WorldPosition(world), p) : float.PositiveInfinity;

    /// <summary>The player in this creature's frame.</summary>
    protected Vector3? PlayerLocal(ICreatureWorld world) =>
        world.Player is { } p ? (Grid >= 0 ? world.ToLocal(Grid, p) : p) : null;

    /// <summary>The six axis directions in the original's order: +X, −X, +Y, −Y, +Z, −Z.</summary>
    public static readonly Int3[] Directions =
    [
        Int3.UnitX, -Int3.UnitX, Int3.UnitY, -Int3.UnitY, Int3.UnitZ, -Int3.UnitZ,
    ];

    protected static Vector3 Center(Int3 cell) => cell + new Vector3(0.5f);
}
