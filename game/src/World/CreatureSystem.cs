using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Content.Vfs;
using OpenCorneroids.Content.Xnb;
using OpenCorneroids.Game.Player;
using OpenCorneroids.Simulation.Creatures;
using OpenCorneroids.Simulation.Shapes;
using Num = System.Numerics;
using Rules = OpenCorneroids.Simulation.Settings.Cvars;

namespace OpenCorneroids.Game.World;

/// <summary>
/// The creatures of a world, as in the original: every second (<c>sv_creature_spawn_interval</c>), near asteroids,
/// mineral parasites and swarm eggs appear on asteroid and mineral surfaces 16 or more units from the player; they live,
/// think and move by the Simulation library's rules (<see cref="Creature"/>), are pushed out of blocks, hurt the player
/// on touch, take shots, and are drawn with the original's models and textures. Creatures are not saved (ours).
/// </summary>
public partial class CreatureSystem : Node3D, ICreatureWorld
{
    /// <summary>Creatures further than this are not drawn, as in the original.</summary>
    const float DrawDistance = 64;
    static readonly Color Blood = new(0.8f, 0.05f, 0.05f);
    static readonly Color Goo = new(0.35f, 0.9f, 0.25f);
    static readonly Color Trail = new(0.6f, 0.25f, 0.85f);

    sealed class Body
    {
        public required Creature Creature;
        public required Node3D Node;
        public float Phase;
    }

    GridSet _grids = null!;
    FlyCamera _player = null!;
    Projectiles _shots = null!;
    Specks _specks = null!;
    IContentSource? _content;
    readonly List<Body> _bodies = new();
    readonly Dictionary<BlockGrid, int> _ids = new();
    readonly List<BlockGrid?> _byId = new();
    readonly Dictionary<CreatureKind, (Mesh? Mesh, Material Material)> _looks = new();
    readonly HashSet<ushort> _food = new();
    double _sinceSpawn;
    ProjectileDef? _bolt;
    // Where every creature is, read once per frame for the shots (see HitsAlong).
    readonly Simulation.Combat.SphereHash _hitSpheres = new();
    readonly List<Creature> _hitCreatures = new();
    readonly List<(float At, int Index)> _hitScratch = new();
    ulong _hitFrame = ulong.MaxValue;

    public Random Random { get; } = new();

    /// <summary>Creatures alive now, for the console and tests.</summary>
    public int Count => _bodies.Count(b => !b.Creature.Removing);

    public void Setup(GridSet grids, IContentSource? content, FlyCamera player, Projectiles shots)
    {
        _grids = grids;
        _content = content;
        _player = player;
        _shots = shots;
        // One set of specks for the blood and goo here and the sparks of shots and drills.
        _specks = shots.Specks;
        foreach (var index in grids.Palette.Where(i => i.Get<AsteroidComponent>() is not null || i.Get<MineralComponent>() is not null))
            if (!grids.Palette.IsObject(index)) _food.Add(index);
        shots.Creatures = this;
    }

    // ---- ICreatureWorld ----

    public Num.Vector3? Player => _player.Targetable ? _player.BodyCenter.ToNumerics() : null;
    public bool PlayerPiloting => _player.PilotShip is not null;

    int IdOf(BlockGrid grid)
    {
        if (_ids.TryGetValue(grid, out var id)) return id;
        id = _byId.Count;
        _byId.Add(grid);
        _ids[grid] = id;
        return id;
    }

    BlockGrid? GridOf(int id) =>
        id >= 0 && id < _byId.Count && _byId[id] is { } g && IsInstanceValid(g) && _grids.Contains(g) ? g : null;

    public bool GridExists(int grid) => GridOf(grid) is not null;
    public bool IsSolid(int grid, Int3 cell) => GridOf(grid)?.IsSolid(cell.ToGodot()) ?? false;
    public bool IsFood(int grid, Int3 cell) => GridOf(grid) is { } g && _food.Contains(g.Get(cell.ToGodot()));
    public int BlockAt(int grid, Int3 cell) => GridOf(grid)?.Get(cell.ToGodot()) ?? 0;

    public void EatBlock(int grid, Int3 cell)
    {
        if (GridOf(grid) is not { } g || !g.IsSolid(cell.ToGodot())) return;
        var at = g.ToGlobal(cell.ToGodot() + new Vector3(0.5f, 0.5f, 0.5f));
        _grids.Break(g.TargetAt(cell.ToGodot()));
        _shots.Spark(at, new Color(0.75f, 0.75f, 0.75f));
    }

    public Num.Vector3 ToWorld(int grid, Num.Vector3 local) => GridOf(grid) is { } g ? g.ToGlobal(local.ToGodot()).ToNumerics() : local;
    public Num.Vector3 DirectionToWorld(int grid, Num.Vector3 local) =>
        GridOf(grid) is { } g ? (g.GlobalBasis * local.ToGodot()).ToNumerics() : local;
    public Num.Vector3 ToLocal(int grid, Num.Vector3 world) => GridOf(grid) is { } g ? g.ToLocal(world.ToGodot()).ToNumerics() : world;

    public void Add(Creature creature)
    {
        creature.AiClockMs = Spawning.StaggeredClock(_bodies.Count, creature.AiCycleMs);
        var node = new Node3D { Name = creature.Kind.ToString() };
        var (mesh, material) = LookOf(creature.Kind);
        node.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material });
        if (creature.Kind == CreatureKind.Scavenger)
            node.AddChild(new OmniLight3D { LightColor = new Color(1, 0.15f, 0.1f), OmniRange = 5, Position = new Vector3(0, 0, -2), ShadowEnabled = false });
        AddChild(node);
        // Ours: a random phase per body, so neighbouring creatures don't wobble in step.
        _bodies.Add(new Body { Creature = creature, Node = node, Phase = (float)(Random.NextDouble() * Math.Tau) });
        Place(_bodies[^1]);
        // Born after this frame's snapshot for the shots was taken: it can be hit this frame all the same.
        if (_hitFrame == Engine.GetProcessFrames()) AddHitSphere(creature);
    }

    public void FireBolt(Num.Vector3 from, Num.Vector3 direction)
    {
        float scale = Settings.GameSettings.Cvars.Float(Rules.CreatureDamageScale);
        // The scavenger's bolt, as in the original: speed 20, damage 5, 3 s, 0.1 × 0.7 × 0.1, yellow-green.
        _bolt ??= new ProjectileDef(-1, new Rgb8(154, 205, 50), 5, 3000, 0, 20, new Num.Vector3(0.1f, 0.7f, 0.1f), null);
        var bolt = _bolt with { Damage = (int)Math.Round(5 * scale) };
        _shots.Fire(bolt, from.ToGodot(), direction.ToGodot(), Vector3.Zero);
    }

    public void Emit(CreatureEffect effect, Num.Vector3 world, Num.Vector3 velocity)
    {
        switch (effect)
        {
            case CreatureEffect.Goo:
                _specks.Add(world.ToGodot(), velocity.ToGodot(), Goo, 0.1f, 1.0);
                break;
            case CreatureEffect.Trail:
                _specks.Add(world.ToGodot(), velocity.ToGodot(), Trail, 0.1f, 0.8 + Random.NextDouble() * 0.4);
                break;
        }
    }

    // ---- shots ----

    /// <summary>
    /// Creatures a shot from <paramref name="from"/> to <paramref name="to"/> passes within 1.5 × their radius of, as in
    /// the original, with where along the way (0..1), nearest first, into <paramref name="hits"/> (cleared first, so one
    /// list serves every shot). Where the creatures are is read once per frame, at the first shot's question, into a
    /// <see cref="Simulation.Combat.SphereHash"/> (ours, for speed: the answer is the same as testing every creature).
    /// </summary>
    public void HitsAlong(Vector3 from, Vector3 to, List<(float At, Creature Creature)> hits)
    {
        hits.Clear();
        ulong frame = Engine.GetProcessFrames();
        if (_hitFrame != frame)
        {
            _hitFrame = frame;
            _hitSpheres.Clear();
            _hitCreatures.Clear();
            foreach (var body in _bodies)
                if (!body.Creature.Removing) AddHitSphere(body.Creature);
        }
        if (_hitSpheres.Count == 0) return;
        _hitSpheres.SegmentHits(from.ToNumerics(), to.ToNumerics(), _hitScratch);
        foreach (var (at, index) in _hitScratch)
            if (_hitCreatures[index] is { Removing: false } c) hits.Add((at, c));
    }

    void AddHitSphere(Creature c)
    {
        _hitSpheres.Add(c.WorldPosition(this), c.Radius * 1.5f);
        _hitCreatures.Add(c);
    }

    /// <summary>A shot hits <paramref name="creature"/>; returns what the shot loses. Blood flies.</summary>
    public double Absorb(Creature creature, double damage, bool byPlayer)
    {
        var at = creature.WorldPosition(this).ToGodot();
        float shooter = byPlayer ? at.DistanceTo(_player.BodyCenter) : float.PositiveInfinity;
        double absorbed = creature.Absorb(damage, byPlayer, shooter, this);
        if (absorbed > 0) _specks.Burst(at, Blood, 16, 0.12f, 0.4, 0.7, 1.25f);
        return absorbed;
    }

    // ---- the frame ----

    public override void _Process(double delta)
    {
        if (_grids is null) return;
        using var timing = PerfMonitor.Measure(PerfMonitor.Creatures);
        var cvars = Settings.GameSettings.Cvars;
        bool enabled = cvars.Bool(Rules.Creatures);
        double dt = Math.Min(delta, 0.1);
        var eye = _player.BodyCenter;

        if (enabled && (_sinceSpawn += dt) >= cvars.Get(Rules.CreatureSpawnInterval))
        {
            _sinceSpawn = 0;
            SpawnCycle();
        }

        for (int i = _bodies.Count - 1; i >= 0; i--)
        {
            var body = _bodies[i];
            var c = body.Creature;
            if (!c.Removing && (!enabled || eye.DistanceTo(c.WorldPosition(this).ToGodot()) > Math.Min(c.UpkeepDistance, Spawning.MaxUpkeep)))
                c.Remove();
            if (c is MineralParasite parasite) parasite.EatTime = cvars.Get(Rules.ParasiteEatTime);
            c.Update(dt, this);
            if (!c.Removing && c.CollidesWithBlocks) PushOutOfBlocks(c);
            if (!c.Removing) TouchPlayer(c);
            if (c.Gone)
            {
                var at = c.WorldPosition(this).ToGodot();
                _specks.Burst(at, Blood, 24, 0.05f, 0.4, 0.6, 7.5f);
                _specks.Burst(at, Blood, 24, 0.05f, 0.4, 0.6, 7.5f);
                body.Node.QueueFree();
                _bodies.RemoveAt(i);
                continue;
            }
            Place(body);
        }
    }

    /// <summary>Removes every creature at once (a new rule set, a test).</summary>
    public int Clear()
    {
        int n = _bodies.Count;
        foreach (var body in _bodies) body.Node.QueueFree();
        _bodies.Clear();
        _hitFrame = ulong.MaxValue;
        return n;
    }

    /// <summary>
    /// One spawn cycle, as in the original: the grids whose bounds the player is within (plus 5) and that hold fewer
    /// parasites than their cap; if any has a block within 16 of the player, up to 4 mineral parasites or 2 eggs (even
    /// odds) are tried on random ones, else (with <c>sv_scavengers</c>) a scavenger in open space.
    /// </summary>
    void SpawnCycle()
    {
        if (Player is not { } playerNum) return;
        var player = playerNum.ToGodot();
        var cvars = Settings.GameSettings.Cvars;
        float perSector = cvars.Float(Rules.ParasitesPerSector);
        var candidates = new List<BlockGrid>();
        bool nearRock = false;
        foreach (var grid in _grids.All)
        {
            var (center, radius) = grid.Bounds;
            if (player.DistanceTo(center) > radius + Spawning.EntityMargin) continue;
            int id = IdOf(grid);
            int living = _bodies.Count(b => b.Creature.Grid == id && !b.Creature.Removing);
            if (living >= Spawning.ParasiteCap(grid.FilledChunks, perSector)) continue;
            candidates.Add(grid);
            nearRock |= grid.NearestSolidBlock(grid.ToLocal(player), Spawning.ParasiteRange) is not null;
        }

        if (nearRock)
        {
            bool parasites = Random.Next(2) == 0;
            int tries = parasites ? Spawning.MaxMineralParasitesPerCycle : Spawning.MaxEggsPerCycle;
            for (int i = 0; i < tries; i++)
            {
                var grid = candidates[Random.Next(candidates.Count)];
                var trial = Spawning.TrialPoint(player.ToNumerics(), Random.NextDouble(), Random.NextDouble(), Random.NextDouble()).ToGodot();
                if (_bodies.Any(b => !b.Creature.Removing && b.Creature.WorldPosition(this).ToGodot().DistanceTo(trial) < Spawning.MinCreatureDistance))
                    continue;
                int id = IdOf(grid);
                var spot = Spawning.FindSurface(grid.ToLocal(trial).ToNumerics(), c => grid.IsSolid(c.ToGodot()), c => _food.Contains(grid.Get(c.ToGodot())));
                if (spot is not { } s) continue;
                Add(parasites ? new MineralParasite(id, s.Cell, s.Floor, this, cvars.Get(Rules.ParasiteEatTime)) : new SwarmEgg(id, s.Cell, s.Floor));
                GD.Print(FormattableString.Invariant(
                    $"A {(parasites ? "mineral parasite" : "swarm egg")} appeared {grid.ToGlobal(s.Cell.ToGodot()).DistanceTo(player):0} m away"));
            }
        }
        else if (cvars.Bool(Rules.Scavengers))
        {
            int crowd = _bodies.Count(b => b.Creature.WorldPosition(this).ToGodot().DistanceTo(player) <= Spawning.FlyerCrowdRange);
            if (crowd > Spawning.FlyerCrowdLimit) return;
            for (int t = 0; t < Spawning.FlyerTries; t++)
            {
                var at = Spawning.FlyerPoint(player.ToNumerics(), Random.NextDouble(), Random.NextDouble(), Random.NextDouble()).ToGodot();
                if (_grids.All.Any(g => g.NearestSolidBlock(g.ToLocal(at), Spawning.ParasiteRange) is not null)) continue;
                Add(new Scavenger(at.ToNumerics(), Random));
                break;
            }
        }
    }

    /// <summary>Spawns <paramref name="kind"/> on the asteroid surface nearest the aim (the console's <c>spawn</c>).</summary>
    public string SpawnNear(CreatureKind kind, Vector3 eye, Vector3 forward)
    {
        if (kind is CreatureKind.Scavenger or CreatureKind.Needle)
        {
            var at = eye + forward * 12;
            Add(kind == CreatureKind.Scavenger ? new Scavenger(at.ToNumerics(), Random) : new Needle(at.ToNumerics(), Num.Vector3.Zero, target: true));
            return $"Spawned a {kind} 12 m ahead.";
        }
        if (!_grids.Raycast(eye, forward, 64, out var aim)) return "Aim at an asteroid within 64 m.";
        var grid = aim.Grid;
        int id = IdOf(grid);
        var cell = aim.Before.ToInt3();
        var floor = aim.Hit.ToInt3();
        if (!_food.Contains(grid.Get(aim.Hit))) return "That block is not asteroid rock or mineral.";
        Add(kind == CreatureKind.MineralParasite
            ? new MineralParasite(id, cell, floor, this, Settings.GameSettings.Cvars.Get(Rules.ParasiteEatTime))
            : new SwarmEgg(id, cell, floor));
        return $"Spawned a {kind} on the aimed block.";
    }

    /// <summary>Blocks push a creature out, as in the original's sphere test; the velocity into them is dropped.</summary>
    void PushOutOfBlocks(Creature c)
    {
        bool hit = false;
        if (c.Grid >= 0)
        {
            if (GridOf(c.Grid) is not { } grid) return;
            var pos = c.Position.ToGodot();
            var vel = c.Velocity.ToGodot();
            hit = PushOut(grid, ref pos, ref vel, c.Radius);
            c.Position = pos.ToNumerics();
            c.Velocity = vel.ToNumerics();
        }
        else
        {
            var world = c.Position.ToGodot();
            foreach (var grid in _grids.All)
            {
                var (center, radius) = grid.Bounds;
                if (world.DistanceTo(center) > radius + 5) continue;
                var pos = grid.ToLocal(world);
                var vel = grid.DirectionToLocal(c.Velocity.ToGodot());
                if (!PushOut(grid, ref pos, ref vel, c.Radius)) continue;
                hit = true;
                world = grid.ToGlobal(pos);
                c.Velocity = (grid.GlobalBasis * vel).ToNumerics();
            }
            c.Position = world.ToNumerics();
        }
        if (hit) c.HitBlock();
    }

    static bool PushOut(BlockGrid grid, ref Vector3 pos, ref Vector3 vel, float radius)
    {
        bool hit = false;
        int reach = (int)Math.Ceiling(radius) + 1;
        var c = new Vector3I(Mathf.FloorToInt(pos.X), Mathf.FloorToInt(pos.Y), Mathf.FloorToInt(pos.Z));
        for (int z = -reach; z <= reach; z++)
        for (int y = -reach; y <= reach; y++)
        for (int x = -reach; x <= reach; x++)
        {
            var cell = c + new Vector3I(x, y, z);
            if (!grid.IsSolid(cell)) continue;
            var min = (Vector3)cell;
            var closest = pos.Clamp(min, min + Vector3.One);
            var d = pos - closest;
            float dist = d.Length();
            if (dist >= radius) continue;
            Vector3 n;
            if (dist > 1e-5f) n = d / dist;
            else
            {
                // The centre is inside the cell: out through the nearest face.
                var local = pos - min - new Vector3(0.5f, 0.5f, 0.5f);
                var a = local.Abs();
                n = a.X >= a.Y && a.X >= a.Z ? new Vector3(Math.Sign(local.X), 0, 0)
                    : a.Y >= a.Z ? new Vector3(0, Math.Sign(local.Y), 0) : new Vector3(0, 0, Math.Sign(local.Z));
                if (n == Vector3.Zero) n = Vector3.Up;
                dist = -(0.5f - Math.Max(a.X, Math.Max(a.Y, a.Z)));
            }
            pos += n * (radius - dist);
            float into = vel.Dot(n);
            if (into < 0) vel -= n * into;
            hit = true;
        }
        return hit;
    }

    /// <summary>The player and a creature overlap: pushed apart and, with touch damage, hurt (see <see cref="FlyCamera.Touched"/>).</summary>
    void TouchPlayer(Creature c)
    {
        if (!_player.Targetable) return;
        var player = _player.BodyCenter;
        var at = c.WorldPosition(this).ToGodot();
        if (Spawning.Touch(player.ToNumerics(), PlayerMotor.BodyRadius, at.ToNumerics(), c.Radius) is not { } touch) return;
        var normal = touch.Normal.ToGodot();
        bool boarded = _player.Aboard;
        float playerShare = boarded ? touch.Depth : touch.Depth * 0.5f;
        float creatureShare = touch.Depth - playerShare;
        // The creature backs off by its half, in its own frame.
        var back = -normal * creatureShare;
        c.Position = c.Grid >= 0 && GridOf(c.Grid) is { } g ? c.Position + g.DirectionToLocal(back).ToNumerics() : c.Position + back.ToNumerics();
        double damage = c.TouchDamage * Settings.GameSettings.Cvars.Get(Rules.CreatureDamageScale);
        _player.Touched(normal, playerShare, damage, at);
    }

    void Place(Body body)
    {
        var c = body.Creature;
        var at = c.WorldPosition(this).ToGodot();
        body.Node.Visible = at.DistanceTo(_player.BodyCenter) <= DrawDistance;
        if (!body.Node.Visible) return;
        var facing = (c.Grid >= 0 ? DirectionToWorld(c.Grid, c.Facing) : c.Facing).ToGodot();
        if (facing.LengthSquared() < 1e-6f) facing = Vector3.Up;
        // As in the original: the mineral parasite wobbles ±5% per axis with phases 0, π/2 and π (frozen while it
        // attacks); the egg throbs along its own Z, which stands along the surface normal.
        var scale = Vector3.One * c.Scale;
        float t = (float)c.Age;
        if (c is MineralParasite { Attacking: false })
            scale *= new Vector3(1 + 0.05f * MathF.Sin(MathF.PI * t + body.Phase), 1 + 0.05f * MathF.Sin(MathF.PI * t + body.Phase + MathF.PI / 2),
                1 + 0.05f * MathF.Sin(MathF.PI * t + body.Phase + MathF.PI));
        if (c is SwarmEgg) scale *= new Vector3(1, 1, 1 + 0.05f * MathF.Sin(5 * t + body.Phase));
        // The model's own turn first, then the scale in the creature's own frame, then the look along the facing.
        var basis = Look(facing.Normalized(), c.Grid) * Basis.FromScale(scale) * BaseTurn(c.Kind);
        body.Node.GlobalTransform = new Transform3D(basis, at);
    }

    /// <summary>
    /// A basis whose -Z axis points along <paramref name="facing"/> with no roll about the +Y axis of the grid the
    /// creature is on (the world's in open space), as in the original. Looking straight along that axis the roll is
    /// undefined, so the grid's +Z serves as up instead.
    /// </summary>
    Basis Look(Vector3 facing, int grid)
    {
        var up = (grid >= 0 ? DirectionToWorld(grid, Num.Vector3.UnitY) : Num.Vector3.UnitY).ToGodot().Normalized();
        if (Math.Abs(facing.Dot(up)) > 0.999f)
            up = (grid >= 0 ? DirectionToWorld(grid, Num.Vector3.UnitZ) : Num.Vector3.UnitZ).ToGodot().Normalized();
        return Basis.LookingAt(facing, up);
    }

    /// <summary>The model's own turn about X, as in the original.</summary>
    static Basis BaseTurn(CreatureKind kind) => kind switch
    {
        CreatureKind.MineralParasite or CreatureKind.SwarmEgg => new Basis(Vector3.Right, MathF.PI),
        CreatureKind.Needle => new Basis(Vector3.Right, 3 * MathF.PI / 2),
        CreatureKind.Scavenger => new Basis(Vector3.Right, MathF.PI / 2),
        _ => Basis.Identity,
    };

    (Mesh?, Material) LookOf(CreatureKind kind)
    {
        if (_looks.TryGetValue(kind, out var look)) return look;
        var (model, texture) = kind switch
        {
            CreatureKind.MineralParasite => ("Models/mineralParasite.xnb", "Textures/mineralParasite.xnb"),
            CreatureKind.SwarmEgg => ("Models/egg.xnb", "Textures/swarmEgg.xnb"),
            CreatureKind.Needle => ("Models/needle.xnb", "Textures/needle.xnb"),
            _ => ("Models/scavenger.xnb", "Textures/scavenger.xnb"),
        };
        var material = new StandardMaterial3D { TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest };
        Mesh? mesh = null;
        try
        {
            if (Open(texture) is { } t)
            {
                var tex = XnbTexture.Read(t);
                var image = Image.CreateFromData(tex.Width, tex.Height, false, Image.Format.Rgba8, tex.Rgba);
                material.AlbedoTexture = ImageTexture.CreateFromImage(image);
            }
            if (Open(model) is { } m) mesh = MeshOf(XnbModel.Read(m));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or EndOfStreamException)
        {
            GD.PushWarning($"Creature look {kind}: {e.Message}");
        }
        // Without the model: a plain sphere of the creature's colour (ours).
        mesh ??= new SphereMesh { Radius = kind == CreatureKind.Scavenger ? 1 : 0.35f, Height = kind == CreatureKind.Scavenger ? 2 : 0.7f };
        if (material.AlbedoTexture is null) material.AlbedoColor = new Color(0.55f, 0.25f, 0.6f);
        return _looks[kind] = (mesh, material);
    }

    XnbFile? Open(string path)
    {
        if (_content is null || !_content.TryOpen(path, out var stream)) return null;
        using (stream) return XnbFile.Read(stream);
    }

    static ArrayMesh MeshOf(XnbModel model)
    {
        var mesh = new ArrayMesh();
        foreach (var part in model.Parts)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = part.Positions.Select(p => p.ToGodot()).ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = part.Normals.Select(n => n.ToGodot()).ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = part.Uvs.Select(u => new Vector2(u.X, u.Y)).ToArray();
            // XNA's front faces are clockwise, as Godot's: the index order stays.
            arrays[(int)Mesh.ArrayType.Index] = part.Indices;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        return mesh;
    }
}
