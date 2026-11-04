using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;
using Num = System.Numerics;

namespace OpenCorneroids.Game.World;

/// <summary>What the pilot asks of the ship, in the ship's local (grid) axes: the velocity and spin to reach, and
/// whether the flight assistant also cancels drift (<see cref="FlightAssist.Command"/>).</summary>
public readonly record struct PilotRequest(Num.Vector3 LinearVelocity, Num.Vector3 AngularVelocity, bool Dampeners);

/// <summary>One thruster of a ship as flight and display see it.</summary>
public sealed class ShipThruster
{
    internal ShipThruster(WorldObject obj, ThrusterComponent component, ThrusterMount mount, int node)
    {
        Object = obj;
        Component = component;
        Mount = mount;
        Node = node;
    }

    public WorldObject Object { get; }
    public ThrusterComponent Component { get; }
    /// <summary>At full power; the force actually available is scaled by its network's satisfaction.</summary>
    public ThrusterMount Mount { get; }
    /// <summary>Its power node, or −1 when the block has no power value (then it always has full force).</summary>
    public int Node { get; }
    /// <summary>0..1, from the last physics step.</summary>
    public float Throttle { get; internal set; }
    /// <summary>Firing: draws power. Every thruster that pushes is lit; one stays lit for
    /// <see cref="GridSystems.LitHold"/> after its throttle drops to zero, so a throttle hovering near zero does not
    /// make the network's demand (and every thruster's availability) flicker.</summary>
    public bool Lit { get; internal set; }
    /// <summary>Seconds a lit thruster at zero throttle has left before it goes dark.</summary>
    internal float DarkIn { get; set; }
    /// <summary>Fraction of its force that power allows (its network's satisfaction; 1 without a power value).</summary>
    public float Availability { get; internal set; } = 1;
    internal MeshInstance3D? Flame { get; set; }
}

/// <summary>
/// A grid's power and, on a ship, its thrusters: which blocks form networks, how much each thruster may push, the throttles the flight
/// assistant picks, and what that looks like (active or unpowered textures, thruster flames). All rules are ours
/// (tunable; original behaviour unknown); see <see cref="PowerNetwork"/>, <see cref="FlightAssist"/> and
/// <see cref="ThrustAllocator"/> for the parts that live in the simulation library. The static world has one too, for
/// its power textures only: nobody pilots it, so its thrusters always idle.
/// </summary>
/// <remarks>
/// Power is solved for the thrusters that are lit; lighting changes demand, so after allocating, the networks are solved
/// again and, if a thruster's availability changed, the throttles are allocated once more against the new
/// availabilities. Thrusters that light up in that second pass are lit too and the networks solved once more, so the
/// force applied is what the final throttles give at the availabilities of the final lit set: a thruster never pushes
/// without being lit (drawing power, a flame), and never pushes with power it does not get. A throttle at or
/// below <see cref="LightUp"/> counts as zero.
/// </remarks>
public sealed class GridSystems
{
    /// <summary>Throttles at or below this are not fired at all (no push, no power, no flame): the allocator's tiny
    /// leftovers. Small enough that the dampeners still settle a ship to well under a centimetre per second. Tunable.</summary>
    public const float LightUp = 0.001f;
    /// <summary>Seconds a thruster stays lit (drawing power) after its throttle drops to zero.
    /// Tunable; original behaviour unknown.</summary>
    public const float LitHold = 0.25f;
    /// <summary>Flame length, in cells at full throttle, for a thruster whose <c>flameLength</c> is 0 (all vanilla
    /// ones), on top of its nozzle's width. Tunable; original behaviour unknown.</summary>
    public const float DefaultFlameLength = 1.2f;

    // Per ship rather than static: Godot resources held in static fields outlive the engine at shutdown.
    Mesh? _flameMesh;
    Material? _flameMaterial, _coreMaterial;

    readonly BlockGrid _grid;
    readonly List<ShipThruster> _thrusters = [];
    int _revision = -1;
    PowerNode[] _nodes = [];
    /// <summary>What each power node is on screen: a grid cell, or an object (then the cell is its origin).</summary>
    (Vector3I Cell, WorldObject? Object)[] _nodeBlocks = [];
    /// <summary>Index into <see cref="_thrusters"/> per node, −1 for the rest.</summary>
    int[] _thrusterOfNode = [];
    PowerSolution? _power;
    bool _powerDirty = true;
    bool[] _shown = [];
    bool _shownValid;
    ThrusterMount[] _scaled = [];
    float[] _throttles = [];

    public GridSystems(BlockGrid grid) => _grid = grid;

    /// <summary>The flight assistant's gains: the defaults, with the speed limit at the pilot's top boosted request, so
    /// no ship under control gets near the speed where collisions can skip a wall (see <see cref="ShipPhysics"/>).</summary>
    static readonly FlightAssistGains Gains = FlightAssistGains.Default with { SpeedLimit = Player.FlyCamera.PilotSpeed * Player.FlyCamera.PilotBoost };

    public IReadOnlyList<ShipThruster> Thrusters
    {
        get
        {
            Refresh();
            return _thrusters;
        }
    }

    /// <summary>Thrusters lit right now.</summary>
    public int Firing => _thrusters.Count(t => t.Lit);

    /// <summary>Supply and demand summed over every network of the ship, and the lowest satisfaction of a network that
    /// has demand (1 when nothing demands and something supplies, 0 when nothing supplies).</summary>
    public (float Supply, float Demand, float Satisfaction) PowerSummary
    {
        get
        {
            var power = Solve();
            float supply = 0, demand = 0, satisfaction = 1;
            bool anyDemand = false;
            foreach (var network in power.Networks)
            {
                supply += network.Supply;
                demand += network.Demand;
                if (network.Demand <= 0) continue;
                anyDemand = true;
                satisfaction = Math.Min(satisfaction, network.Satisfaction);
            }
            if (!anyDemand) satisfaction = supply > 0 ? 1 : 0;
            return (supply, demand, satisfaction);
        }
    }

    /// <summary>
    /// The wrench (ship-local force through the centre of mass and torque about it) the thrusters give this step. With
    /// no <paramref name="request"/> (nobody at the controls) every thruster idles.
    /// </summary>
    /// <param name="step">Length of the physics step, seconds (for <see cref="LitHold"/>).</param>
    public (Num.Vector3 Force, Num.Vector3 Torque) Thrust(RigidBodyState state, MassProperties mass, PilotRequest? request, float step)
    {
        Refresh();
        int n = _thrusters.Count;
        if (request is not { } r || n == 0 || mass.IsMassless)
        {
            Idle();
            return default;
        }
        if (_scaled.Length != n)
        {
            _scaled = new ThrusterMount[n];
            _throttles = new float[n];
        }

        Scale();
        var command = FlightAssist.Command(state, mass, r.LinearVelocity, r.AngularVelocity, r.Dampeners, Gains);
        ThrustAllocator.Allocate(_scaled, mass.CenterOfMass, command.ForceLocal, command.TorqueLocal, 1, _throttles);
        if (SetThrottles(step) && Scale())
        {
            // Lighting changed the demand and with it what the thrusters can give: allocate once more against that,
            // light what that needs, and push with what the final lit set really gets.
            ThrustAllocator.Allocate(_scaled, mass.CenterOfMass, command.ForceLocal, command.TorqueLocal, 1, _throttles);
            if (SetThrottles(0)) Scale();
        }
        return ThrustAllocator.Wrench(_scaled, mass.CenterOfMass, _throttles);
    }

    /// <summary>Every thruster at zero (nobody piloting); the power networks still count for the textures.</summary>
    public void Idle()
    {
        Refresh();
        foreach (var t in _thrusters)
        {
            t.Throttle = 0;
            if (t.Lit)
            {
                t.Lit = false;
                _powerDirty = true;
            }
        }
    }

    /// <summary>
    /// Brings textures and flames up to date, once per frame: blocks whose state changed are remeshed (their chunk, or
    /// the object's own mesh), nothing else; flames follow the throttles.
    /// </summary>
    public void UpdateDisplay()
    {
        Refresh();
        var power = Solve();
        if (_shown.Length != _nodes.Length)
        {
            _shown = new bool[_nodes.Length];
            _shownValid = false;
        }
        bool changed = !_shownValid;
        for (int i = 0; i < _nodes.Length; i++)
        {
            bool active = IsActive(i, power);
            if (active == _shown[i]) continue;
            _shown[i] = active;
            changed = true;
        }
        if (changed)
        {
            var cells = new HashSet<Vector3I>();
            var objects = new HashSet<WorldObject>();
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_shown[i]) continue;
                if (_nodeBlocks[i].Object is { } obj) objects.Add(obj);
                else cells.Add(_nodeBlocks[i].Cell);
            }
            _grid.SetInactive(cells, objects);
            _shownValid = true;
        }
        foreach (var t in _thrusters) ShowFlame(t);
    }

    /// <summary>
    /// Drawn active: its network delivers something (<see cref="PowerSolution.IsPowered"/>). An idle thruster on a
    /// powered network is active too (it is ready to fire; its flame shows when it does), and the rule is the same in
    /// the static world and on ships, so launching a structure never changes how it looks. Ours (tunable; original
    /// behaviour unknown).
    /// </summary>
    static bool IsActive(int node, PowerSolution power) => power.IsPowered(node);

    /// <summary>Rebuilds the thruster and power-node lists after the ship was edited.</summary>
    void Refresh()
    {
        if (_revision == _grid.Revision) return;
        _revision = _grid.Revision;
        foreach (var t in _thrusters) t.Flame?.QueueFree();
        _thrusters.Clear();

        var palette = _grid.Palette;
        var nodes = new List<PowerNode>();
        var blocks = new List<(Vector3I, WorldObject?)>();
        var thrusterOf = new List<int>();
        foreach (var (cell, block) in _grid.PoweredBlocks())
        {
            if (palette.Item(block).Block!.Power is not { } value) continue;
            nodes.Add(PowerNode.FromPower(nodes.Count, [cell.ToInt3()], value));
            blocks.Add((cell, null));
            thrusterOf.Add(-1);
        }
        foreach (var obj in _grid.Objects.OrderBy(o => o.Id))
        {
            var item = palette.Item(obj.Block);
            var thruster = item.Get<ThrusterComponent>();
            int node = -1;
            if (item.Block!.Power is { } value)
            {
                node = nodes.Count;
                // Conducts through its model (closed, for a door), not its exhaust or travel space.
                nodes.Add(PowerNode.FromPower(node, [.. obj.Shape.ModelCells(obj.Origin, obj.Orientation)], value));
                blocks.Add((obj.Origin.ToGodot(), obj));
                thrusterOf.Add(thruster is null ? -1 : _thrusters.Count);
            }
            if (thruster is not null)
                _thrusters.Add(new ShipThruster(obj, thruster, ThrusterMount.FromShape(obj.Shape, obj.Origin, obj.Orientation, thruster), node));
        }
        _nodes = [.. nodes];
        _nodeBlocks = [.. blocks];
        _thrusterOfNode = [.. thrusterOf];
        _powerDirty = true;
        _shownValid = false;
    }

    /// <summary>The power solution for the lit thrusters, solved again only when something changed.</summary>
    PowerSolution Solve()
    {
        if (!_powerDirty && _power is not null) return _power;
        var nodes = new PowerNode[_nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
            nodes[i] = _thrusterOfNode[i] is var t && t >= 0 && !_thrusters[t].Lit ? _nodes[i] with { Demand = 0 } : _nodes[i];
        _power = PowerNetwork.Solve(nodes);
        _powerDirty = false;
        return _power;
    }

    /// <summary>Each thruster's mount with its force scaled by its availability. Returns whether any availability changed.</summary>
    bool Scale()
    {
        var power = Solve();
        bool changed = false;
        for (int i = 0; i < _thrusters.Count; i++)
        {
            var t = _thrusters[i];
            float availability = t.Node < 0 ? 1 : power.SatisfactionOf(t.Node);
            if (availability != t.Availability) changed = true;
            t.Availability = availability;
            _scaled[i] = t.Mount with { MaxForce = t.Mount.MaxForce * availability };
        }
        return changed;
    }

    /// <summary>Stores the allocated throttles (zeroing those at or below <see cref="LightUp"/>), lights every thruster that
    /// pushes and darkens those idle for <see cref="LitHold"/> (counted down by <paramref name="elapsed"/>). Returns
    /// whether any thruster's lit state changed (and with it the power demand).</summary>
    bool SetThrottles(float elapsed)
    {
        bool changed = false;
        for (int i = 0; i < _thrusters.Count; i++)
        {
            var t = _thrusters[i];
            if (!(_throttles[i] > LightUp)) _throttles[i] = 0;
            t.Throttle = _throttles[i];
            bool lit;
            if (t.Throttle > 0)
            {
                t.DarkIn = LitHold;
                lit = true;
            }
            else
            {
                t.DarkIn -= elapsed;
                lit = t.Lit && t.DarkIn > 0;
            }
            if (lit == t.Lit) continue;
            t.Lit = lit;
            changed = true;
        }
        if (changed) _powerDirty = true;
        return changed;
    }

    /// <summary>
    /// An additive cone out of the nozzle along the flame direction: from the model box's face on that side (moved by
    /// <c>flameOffset</c>, read as cells in the block's own axes), as long as <c>flameLength</c> cells, or
    /// <see cref="DefaultFlameLength"/> plus the nozzle width when that is 0, times the throttle. Ours.
    /// </summary>
    void ShowFlame(ShipThruster t)
    {
        bool visible = t.Lit && t.Availability > 0 && t.Throttle > 0;
        if (!visible)
        {
            if (t.Flame is not null) t.Flame.Visible = false;
            return;
        }
        if (t.Flame is null)
        {
            t.Flame = CreateFlame();
            _grid.AddChild(t.Flame);
        }

        var obj = t.Object;
        var box = obj.Shape.ModelBox(obj.Origin, obj.Orientation);
        var dir = t.Mount.LocalFlameDirection.ToGodot();
        var size = box.Size.ToGodot();
        int a = (int)dir.Abs().MaxAxisIndex();
        float along = size[a];
        // Width across the flame: the smaller of the two other model edges.
        float width = Math.Max(0.2f, Math.Min(size[(a + 1) % 3], size[(a + 2) % 3]));
        float full = t.Component.FlameLength > 0 && float.IsFinite(t.Component.FlameLength) ? t.Component.FlameLength : DefaultFlameLength + width;
        float length = Math.Max(0.05f, full * Math.Clamp(t.Throttle * t.Availability, 0, 1));
        var offset = obj.Orientation.Apply(t.Component.FlameOffset).ToGodot();
        if (!offset.IsFinite()) offset = Vector3.Zero;
        var start = box.Center.ToGodot() + dir * (along * 0.5f) + offset;

        var side = Mathf.Abs(dir.Y) < 0.9f ? dir.Cross(Vector3.Up).Normalized() : dir.Cross(Vector3.Right).Normalized();
        var third = side.Cross(dir);
        float radius = width * 0.42f;
        t.Flame.Transform = new Transform3D(new Basis(side * radius, dir * length, third * radius), start + dir * (length * 0.5f));
        t.Flame.Visible = true;
    }

    /// <summary>The outer cone (orange) with a shorter, brighter core inside; both unshaded and additive, no shadows.</summary>
    MeshInstance3D CreateFlame()
    {
        _flameMaterial ??= FlameMaterial(new Color(1f, 0.45f, 0.12f, 0.85f));
        _coreMaterial ??= FlameMaterial(new Color(1f, 0.9f, 0.6f, 0.9f));
        _flameMesh ??= new CylinderMesh { TopRadius = 0, BottomRadius = 1, Height = 1, RadialSegments = 12, Rings = 1, CapTop = false, CapBottom = false };
        var flame = new MeshInstance3D
        {
            Name = "Flame", Mesh = _flameMesh, MaterialOverride = _flameMaterial, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        flame.AddChild(new MeshInstance3D
        {
            Name = "Core", Mesh = _flameMesh, MaterialOverride = _coreMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // In the cone's unit space: half as wide, 60% as long, starting at the nozzle like the outer cone.
            Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(0.5f, 0.6f, 0.5f)), new Vector3(0, -0.2f, 0)),
        });
        return flame;
    }

    static StandardMaterial3D FlameMaterial(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        AlbedoColor = color,
    };
}
