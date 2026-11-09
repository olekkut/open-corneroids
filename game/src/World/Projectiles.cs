using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Combat;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;
using Num = System.Numerics;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Shots in flight, from hand weapons and ship guns. Each frame a shot moves along its velocity; the segment it
/// covers is cast through every grid (<see cref="GridSet.Raycast"/>) and tested against the player (<see cref="Player"/>,
/// within <c>sv_projectile_hit_radius</c>). What it meets takes from its remaining damage what it can soak up, and with
/// damage left it flies on, as in the original (with <c>sv_ricochet 1</c> it may glance off a block instead,
/// <see cref="Weapons.Ricochet"/>); a block breaks when the damage it took reaches its health
/// (<see cref="BlockDamage{TKey}"/>). Broken blocks drop nothing: shooting is not mining. Shots live for their
/// projectile's <c>lifetime</c>. Rules are in the Simulation library; this node only moves and draws.
/// </summary>
public partial class Projectiles : Node3D
{
    static Simulation.Settings.CvarStore Cvars => Settings.GameSettings.Cvars;

    /// <summary>More than this many shots in flight and the oldest go first, so a minigun battle can't pile up
    /// (<c>sv_projectile_limit</c>, 512 as in the original).</summary>
    static int MaxShots => Cvars.Int(Simulation.Settings.Cvars.ProjectileLimit);

    sealed class Shot
    {
        public required ProjectileDef Def;
        public required MeshInstance3D Mesh;
        public Vector3 Position;
        public Vector3 Velocity;
        public double Age;
        public int Bounces;
        /// <summary>Damage the shot still carries; what it hits takes from it, as in the original.</summary>
        public double Remaining;
        /// <summary>Fired by the player's own hand weapon, so it never hits them.</summary>
        public bool FromPlayer;
    }

    /// <summary>Something a shot can hit besides blocks: the player.</summary>
    public interface ITarget
    {
        /// <summary>Centre of the body, world space.</summary>
        Vector3 Center { get; }
        /// <summary>Whether shots meet the body at all now (alive, in survival, not noclipping).</summary>
        bool Hittable { get; }
        /// <summary>A shot carrying <paramref name="damage"/> hits, flying along <paramref name="direction"/>; returns
        /// how much of its damage the body soaks up.</summary>
        double Absorb(double damage, Vector3 direction);
    }

    /// <summary>The player, for shots to hit; set by the world.</summary>
    public ITarget? Player { get; set; }

    /// <summary>The creatures, for shots to hit (within 1.5 × their radius, as in the original); set by them.</summary>
    public CreatureSystem? Creatures { get; set; }

    GridSet _grids = null!;
    ItemsetDef _itemset = null!;
    readonly List<Shot> _shots = new();
    // A grid block is known by its cell and block value, a multi-block by the object itself, so a block put in the place
    // of a shot-up one starts whole.
    readonly BlockDamage<(BlockGrid Grid, Vector3I Cell, ushort Block, WorldObject? Object)> _damage = new();
    readonly Dictionary<int, (Mesh Mesh, Material Material)> _looks = new();
    readonly Dictionary<int, Mesh> _sparkMeshes = new();
    /// <summary>A gun on a ship: a multi-block (<see cref="Obj"/>) or a single-cell grid block at <see cref="Cell"/>.</summary>
    readonly record struct Gun(WorldObject? Obj, Vector3I Cell, GunComponent Data, ProjectileDef Def);

    readonly Dictionary<(ShipGrid Ship, WorldObject? Obj, Vector3I Cell), Trigger> _gunTriggers = new();
    readonly Dictionary<ShipGrid, (int Revision, List<Gun> Guns)> _shipGuns = new();
    double _sinceTidy;

    /// <summary>Raised when a shot breaks a block, with the block's name, for the HUD log.</summary>
    public event Action<string>? BlockDestroyed;

    public void Setup(GridSet grids, ItemsetDef itemset) => (_grids, _itemset) = (grids, itemset);

    public int InFlight => _shots.Count;

    /// <summary>Launches one shot from <paramref name="from"/> along <paramref name="direction"/>; <paramref name="carrier"/>
    /// is the shooter's own velocity, which the shot keeps only with <c>sv_projectile_inherit_velocity</c> (the
    /// original's shots fly at their own speed whatever the shooter does). At <c>sv_projectile_limit</c> shots in
    /// flight no new one is fired, as in the original. <paramref name="fromPlayer"/>: the player's own hand weapon.</summary>
    public void Fire(ProjectileDef def, Vector3 from, Vector3 direction, Vector3 carrier, bool fromPlayer = false)
    {
        if (direction == Vector3.Zero || _shots.Count >= Math.Max(1, MaxShots)) return;
        var (mesh, material) = LookOf(def);
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(instance);
        if (!Cvars.Bool(Simulation.Settings.Cvars.ProjectileInheritVelocity)) carrier = Vector3.Zero;
        var shot = new Shot
        {
            Def = def, Mesh = instance, Position = from, Velocity = direction.Normalized() * def.Speed + carrier,
            Remaining = Math.Max(0, def.Damage), FromPlayer = fromPlayer,
        };
        Place(shot);
        _shots.Add(shot);
    }

    /// <summary>
    /// Fires every gun on <paramref name="ship"/> that has reloaded: along the gun's long axis (its local +Y, turned
    /// by its orientation), from the end of its barrel. Multi-block guns and single-cell ones (turned grid blocks, such
    /// as the vanilla Anti-Personnel Cannon) alike. Returns how many fired.
    /// </summary>
    public int FireGuns(ShipGrid ship, double now)
    {
        int fired = 0;
        foreach (var gun in GunsOn(ship))
        {
            if (FireGun(ship, gun, aimForwardWorld: null, now)) fired++;
        }
        return fired;
    }

    /// <summary>
    /// Fires every gun on <paramref name="ship"/> whose canonical cell is in <paramref name="boundTargets"/>, if its
    /// power network is active and it has reloaded. When <paramref name="aimForwardWorld"/> is provided (seated at a
    /// gun console), aims the shot toward the player's view direction within the gun's hemisphere as in the original.
    /// </summary>
    public int FireBoundGuns(ShipGrid ship, IReadOnlySet<Int3> boundTargets, Vector3? aimForwardWorld, double now)
    {
        if (boundTargets.Count == 0) return 0;
        int fired = 0;
        foreach (var gun in GunsOn(ship))
        {
            if (!boundTargets.Contains(gun.Cell.ToInt3())) continue;
            if (FireGun(ship, gun, aimForwardWorld, now)) fired++;
        }
        return fired;
    }

    IReadOnlyList<Gun> GunsOn(ShipGrid ship)
    {
        // The guns are looked up again only after the ship was edited, not every frame the button is held.
        if (!_shipGuns.TryGetValue(ship, out var cached) || cached.Revision != ship.Revision)
        {
            var guns = new List<Gun>();
            foreach (var o in ship.Objects)
                if (ship.Palette.Item(o.Block).Get<GunComponent>() is { } g && _itemset.Projectiles.TryGetValue(g.ProjectileId, out var d))
                    guns.Add(new Gun(o, o.Origin.ToGodot(), g, d));
            foreach (var (cell, block) in ship.Blocks())
                if (ship.Palette.Item(block).Get<GunComponent>() is { } g && _itemset.Projectiles.TryGetValue(g.ProjectileId, out var d))
                    guns.Add(new Gun(null, cell, g, d));
            _shipGuns[ship] = cached = (ship.Revision, guns);
        }
        return cached.Guns;
    }

    bool FireGun(ShipGrid ship, Gun gun, Vector3? aimForwardWorld, double now)
    {
        if (!ship.Systems.IsBlockActive(new BlockTarget(ship, gun.Cell, gun.Obj))) return false;
        var key = (ship, gun.Obj, gun.Cell);
        if (!_gunTriggers.TryGetValue(key, out var trigger)) _gunTriggers[key] = trigger = new Trigger();
        if (!trigger.TryFire(now, gun.Data.ReloadMs)) return false;
        var turn = gun.Obj?.Orientation ?? ship.TurnOf(gun.Cell);
        var local = turn.Apply(Num.Vector3.UnitY).ToGodot();
        var center = gun.Obj?.ModelBox.GetCenter() ?? (Vector3)gun.Cell + new Vector3(0.5f, 0.5f, 0.5f);
        var muzzle = ship.ToGlobal(center + local * gun.Data.BarrelLength);
        var barrelWorld = ship.GlobalBasis * local;
        var direction = aimForwardWorld is { } aim
            ? ConsoleBinding.GunAimDirection(barrelWorld.ToNumerics(), aim.ToNumerics()).ToGodot()
            : barrelWorld;
        Fire(gun.Def, muzzle, direction, ship.PointVelocity(muzzle));
        return true;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];
            shot.Age += delta;
            if (shot.Age * 1000 >= shot.Def.LifetimeMs || !Step(shot, dt))
            {
                shot.Mesh.QueueFree();
                _shots.RemoveAt(i);
                continue;
            }
            Place(shot);
        }

        // Damage on blocks that are gone (mined, launched, their ship broken up) is forgotten now and then.
        if ((_sinceTidy += delta) > 2)
        {
            _sinceTidy = 0;
            _damage.ForgetWhere(k => !IsInstanceValid(k.Grid) || !_grids.Contains(k.Grid) ||
                                     (k.Object is { } o ? !k.Grid.Objects.Contains(o) : k.Grid.Get(k.Cell) != k.Block));
            foreach (var gun in _gunTriggers.Keys.Where(k => !IsAlive(k)).ToList()) _gunTriggers.Remove(gun);
            foreach (var ship in _shipGuns.Keys.Where(s => !IsInstanceValid(s) || !_grids.Contains(s)).ToList()) _shipGuns.Remove(ship);
        }
    }

    bool IsAlive((ShipGrid Ship, WorldObject? Obj, Vector3I Cell) gun) =>
        IsInstanceValid(gun.Ship) && _grids.Contains(gun.Ship) &&
        (gun.Obj is { } o ? gun.Ship.Objects.Contains(o)
                          : gun.Ship.Get(gun.Cell) is var b && b != BlockPalette.Air && gun.Ship.Palette.Item(b).Get<GunComponent>() is not null);

    /// <summary>
    /// Moves a shot through one frame; false when it is spent. Along the way it meets blocks and the player in the order
    /// it reaches them. Each takes from its remaining damage what it can soak up (a block its hit points left, the player
    /// their health); with damage left after breaking a block or wounding the player it flies on, as in the original
    /// (<c>sv_projectile_pass_through</c>).
    /// </summary>
    bool Step(Shot shot, float dt)
    {
        float travel = shot.Velocity.Length() * dt;
        if (travel <= 0) return true;
        var direction = shot.Velocity.Normalized();
        var start = shot.Position;
        bool passThrough = Cvars.Bool(Simulation.Settings.Cvars.ProjectilePassThrough);

        // Bodies along this frame's path, nearest first: the player (unless it is their own shot) and creatures.
        var end = start + direction * travel;
        var bodies = new List<(float At, Simulation.Creatures.Creature? Creature)>();
        if (!shot.FromPlayer && Player is { Hittable: true } player &&
            Damage.SegmentHitsSphere(start.ToNumerics(), end.ToNumerics(), player.Center.ToNumerics(),
                Cvars.Float(Simulation.Settings.Cvars.ProjectileHitRadius)) is { } t)
            bodies.Add((t * travel, null));
        if (Creatures is { } creatures)
            foreach (var (at, creature) in creatures.HitsAlong(start, end)) bodies.Add((at * travel, creature));
        bodies.Sort((a, b) => a.At.CompareTo(b.At));
        int next = 0;

        float done = 0;
        for (int hits = 0; hits < 64 && done < travel; hits++)
        {
            var from = start + direction * done;
            bool block = _grids.Raycast(from, shot.Velocity, travel - done, out var aim, out var distance);
            float blockAt = block ? done + distance : float.PositiveInfinity;

            if (next < bodies.Count && bodies[next].At <= blockAt)
            {
                var (bodyAt, creature) = bodies[next++];
                double absorbed = creature is null
                    ? (Player?.Absorb(shot.Remaining, direction) ?? 0)
                    : Creatures!.Absorb(creature, shot.Remaining, shot.FromPlayer);
                shot.Remaining -= Math.Max(0, absorbed);
                if (creature is null) Spark(start + direction * bodyAt, shot.Def);
                if (shot.Remaining <= 0 || !passThrough) return false;
                continue;
            }
            if (!block) break;

            var outcome = HitBlock(shot, aim, from + direction * distance, direction, passThrough);
            if (outcome is BlockOutcome.Spent) return false;
            if (outcome is BlockOutcome.Bounced) return true;
            done = blockAt + 0.01f;   // through the broken block
        }
        shot.Position = start + direction * travel;
        return true;
    }

    enum BlockOutcome { Spent, Bounced, Through }

    BlockOutcome HitBlock(Shot shot, Aim aim, Vector3 impact, Vector3 direction, bool passThrough)
    {
        var normal = aim.Grid.GlobalBasis * (Vector3)(aim.Before - aim.Hit);
        // Ricochets are ours and off by default (sv_ricochet): the original's shots hit whatever they meet.
        if (Cvars.Bool(Simulation.Settings.Cvars.Ricochet) && aim.Before != aim.Hit && normal != Vector3.Zero &&
            shot.Bounces < Cvars.Int(Simulation.Settings.Cvars.RicochetBounces) &&
            Weapons.Ricochet(shot.Velocity.ToNumerics(), normal.Normalized().ToNumerics(), shot.Def.RicochetAngle) is { } bounced)
        {
            // Off the face it struck, a hair outside it, heading away; the rest of this frame's travel is dropped.
            shot.Position = impact + normal.Normalized() * 0.01f;
            shot.Velocity = bounced.ToGodot();
            shot.Bounces++;
            return BlockOutcome.Bounced;
        }

        var target = aim.Grid.TargetAt(aim.Hit);
        var block = aim.Grid.BlockOf(target);
        var item = aim.Grid.Palette.Item(block);
        var key = (aim.Grid, target.Object is { } obj ? obj.Origin.ToGodot() : target.Cell, block, target.Object);
        Spark(impact - direction * 0.05f, shot.Def);
        double health = item.Block?.Health ?? 0;
        bool broken;
        if (passThrough)
        {
            (double absorbed, broken) = _damage.Absorb(key, shot.Remaining, health);
            shot.Remaining -= absorbed;
        }
        else
        {
            broken = _damage.Hit(key, shot.Remaining, health);
            shot.Remaining = 0;
        }
        if (broken)
        {
            _grids.Break(target);
            BlockDestroyed?.Invoke(item.Name);
        }
        return broken && shot.Remaining > 0 ? BlockOutcome.Through : BlockOutcome.Spent;
    }

    static void Place(Shot shot)
    {
        // The projectile's size is (width, length, width): its Y axis lies along the flight path.
        var forward = shot.Velocity.Normalized();
        var side = Math.Abs(forward.Dot(Vector3.Up)) < 0.95f ? forward.Cross(Vector3.Up).Normalized() : forward.Cross(Vector3.Right).Normalized();
        var basis = new Basis(side, forward, side.Cross(forward));
        shot.Mesh.GlobalTransform = new Transform3D(basis, shot.Position);
    }

    (Mesh, Material) LookOf(ProjectileDef def)
    {
        if (_looks.TryGetValue(def.ProjectileId, out var look)) return look;
        var color = new Color(def.Color.R / 255f, def.Color.G / 255f, def.Color.B / 255f);
        var size = def.Size.ToGodot();
        // Twice as thick as the data says (at least 0.1): a 5 cm bolt is under a pixel wide a few metres out.
        var mesh = new BoxMesh { Size = new Vector3(Math.Max(size.X * 2, 0.1f), Math.Max(size.Y, 0.3f), Math.Max(size.Z * 2, 0.1f)) };
        // Glowing and unshaded: shots are bolts of energy, readable against dark space and bright rock alike.
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color.Lightened(0.25f),
            EmissionEnabled = true,
            Emission = color,
        };
        return _looks[def.ProjectileId] = (mesh, material);
    }

    Mesh SparkMesh(int projectileId)
    {
        if (_sparkMeshes.TryGetValue(projectileId, out var mesh)) return mesh;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            VertexColorUseAsAlbedo = true,
        };
        return _sparkMeshes[projectileId] = new QuadMesh { Size = new Vector2(0.08f, 0.08f), Material = material };
    }

    void Spark(Vector3 at, ProjectileDef def) =>
        Spark(at, new Color(def.Color.R / 255f, def.Color.G / 255f, def.Color.B / 255f).Lightened(0.3f), def.ProjectileId);

    /// <summary>A short burst of specks: a hit, or a drill at work (grey when it bites, yellow when it is too weak, as
    /// in the original).</summary>
    public void Spark(Vector3 at, Color color, int look = -1)
    {
        var sparks = new CpuParticles3D
        {
            Emitting = true,
            OneShot = true,
            Amount = 10,
            Lifetime = 0.35,
            Explosiveness = 1,
            Direction = Vector3.Up,
            Spread = 180,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 4f,
            Color = color,
            Mesh = SparkMesh(look),
        };
        AddChild(sparks);
        sparks.GlobalPosition = at;
        sparks.Finished += sparks.QueueFree;
    }
}
