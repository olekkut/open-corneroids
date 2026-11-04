using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Combat;
using Num = System.Numerics;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Shots in flight, from hand weapons and ship guns. Each frame a shot moves along its velocity; the segment it
/// covers is cast through every grid (<see cref="GridSet.Raycast"/>), and the first block it meets either glances
/// it off (<see cref="Weapons.Ricochet"/>) or takes its damage, breaking when the damage reaches the block's health
/// (<see cref="BlockDamage{TKey}"/>). Broken blocks drop nothing: shooting is not mining. Shots live for their
/// projectile's <c>lifetime</c>. Rules are in the Simulation library; this node only moves and draws.
/// </summary>
public partial class Projectiles : Node3D
{
    /// <summary>More than this many shots in flight and the oldest go first, so a minigun battle can't pile up.</summary>
    const int MaxShots = 400;

    sealed class Shot
    {
        public required ProjectileDef Def;
        public required MeshInstance3D Mesh;
        public Vector3 Position;
        public Vector3 Velocity;
        public double Age;
        public int Bounces;
    }

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
    /// is the shooter's own velocity, which the shot keeps.</summary>
    public void Fire(ProjectileDef def, Vector3 from, Vector3 direction, Vector3 carrier)
    {
        if (direction == Vector3.Zero) return;
        if (_shots.Count >= MaxShots)
        {
            _shots[0].Mesh.QueueFree();
            _shots.RemoveAt(0);
        }
        var (mesh, material) = LookOf(def);
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(instance);
        var shot = new Shot { Def = def, Mesh = instance, Position = from, Velocity = direction.Normalized() * def.Speed + carrier };
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
        foreach (var gun in cached.Guns)
        {
            var key = (ship, gun.Obj, gun.Cell);
            if (!_gunTriggers.TryGetValue(key, out var trigger)) _gunTriggers[key] = trigger = new Trigger();
            if (!trigger.TryFire(now, gun.Data.ReloadMs)) continue;
            var turn = gun.Obj?.Orientation ?? ship.TurnOf(gun.Cell);
            var local = turn.Apply(Num.Vector3.UnitY).ToGodot();
            var center = gun.Obj?.ModelBox.GetCenter() ?? (Vector3)gun.Cell + new Vector3(0.5f, 0.5f, 0.5f);
            var muzzle = ship.ToGlobal(center + local * gun.Data.BarrelLength);
            var direction = ship.GlobalBasis * local;
            Fire(gun.Def, muzzle, direction, ship.PointVelocity(muzzle));
            fired++;
        }
        return fired;
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

    /// <summary>Moves a shot through one frame; false when it hit something and is spent.</summary>
    bool Step(Shot shot, float dt)
    {
        float travel = shot.Velocity.Length() * dt;
        if (travel <= 0) return true;
        if (!_grids.Raycast(shot.Position, shot.Velocity, travel, out var aim, out var distance))
        {
            shot.Position += shot.Velocity * dt;
            return true;
        }

        var direction = shot.Velocity.Normalized();
        var impact = shot.Position + direction * distance;
        var normal = aim.Grid.GlobalBasis * (Vector3)(aim.Before - aim.Hit);
        if (aim.Before != aim.Hit && normal != Vector3.Zero && shot.Bounces < 3 &&
            Weapons.Ricochet(shot.Velocity.ToNumerics(), normal.Normalized().ToNumerics(), shot.Def.RicochetAngle) is { } bounced)
        {
            // Off the face it struck, a hair outside it, heading away; the rest of this frame's travel is dropped.
            shot.Position = impact + normal.Normalized() * 0.01f;
            shot.Velocity = bounced.ToGodot();
            shot.Bounces++;
            return true;
        }

        var target = aim.Grid.TargetAt(aim.Hit);
        var block = aim.Grid.BlockOf(target);
        var item = aim.Grid.Palette.Item(block);
        var key = (aim.Grid, target.Object is { } obj ? obj.Origin.ToGodot() : target.Cell, block, target.Object);
        Spark(impact - direction * 0.05f, shot.Def);
        if (_damage.Hit(key, shot.Def.Damage, item.Block?.Health ?? 0))
        {
            _grids.Break(target);
            BlockDestroyed?.Invoke(item.Name);
        }
        return false;
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
