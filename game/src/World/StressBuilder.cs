using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Player;
using OpenCorneroids.Simulation.Creatures;
using OpenCorneroids.Simulation.Debugging;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;
using Num = System.Numerics;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;
using ShipLaunch = OpenCorneroids.Simulation.Ships.Launch;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Ours: builds the scenes of the <c>stress</c> cheat (<see cref="StressTest"/>), big and repeatable, so the F3
/// readout (<see cref="PerfMonitor"/>) can measure what they cost:
/// <list type="bullet">
/// <item><c>stress ship n</c>: a ship of about n blocks ahead of the player, ready to fly: a box whose outside layer
/// is the strongest single-cell power block (so it powers and connects everything on it), a console on a top corner,
/// and on every few surface cells a thruster, light, power block or door in turn. W fires the thrusters that push it
/// forward and S the ones that push it back, so <c>--pilot --hold=move_forward:8</c> flies it.</item>
/// <item><c>stress guns n</c>: a powered plate with n guns pointing out of it (up first), a console, and every gun bound
/// to the console's first colour group under W.</item>
/// <item><c>stress creatures [kind] n</c>: n creatures spread over what the player aims at (ahead, for flyers).</item>
/// <item><c>stress objects n</c>: n multi-blocks of every kind in turn, in a lattice in the static world ahead.</item>
/// </list>
/// </summary>
public sealed class StressBuilder(GridSet grids, FlyCamera player, CreatureSystem creatures)
{
    /// <summary>Key of the console group that moves a stress ship forward, or fires a stress platform's guns.</summary>
    const string ForwardKey = "W";
    const string BackKey = "S";
    /// <summary>Half-angle of the cone creatures are spread over, degrees.</summary>
    const float CreatureCone = 30;

    BlockPalette Palette => grids.Palette;

    public string Build(StressRequest request) => request.Kind switch
    {
        StressKind.Ship => Ship(request.Count),
        StressKind.Guns => Guns(request.Count),
        StressKind.Creatures => Creatures(request.Creature, request.Count),
        _ => Objects(request.Count),
    };

    // ---- blocks ----

    ushort? Pick(Func<ushort, bool> where, Func<ushort, double> order) =>
        Palette.Indices.Where(where).OrderBy(order).ThenBy(b => Palette.Item(b).ItemId).Select(b => (ushort?)b).FirstOrDefault();

    bool Has<T>(ushort b) where T : class, IItemComponent => Palette.Item(b).Get<T>() is not null;

    int? PowerOf(ushort b) => Palette.Item(b).Block!.Power;

    long Volume(ushort b) => Palette.Shape(b)?.Size.Volume ?? 1;

    /// <summary>A plain opaque single-cell block with nothing to it (armour, rock), as the demo ship's hull.</summary>
    ushort? Hull() => Pick(b => !Palette.IsObject(b) && !Palette.IsCutout(b) && Palette.Item(b).Components.Count == 0 && PowerOf(b) is null, _ => 0);

    /// <summary>The outside layer: the strongest single-cell power supply, which also conducts; else the cheapest cable;
    /// else the plain hull block.</summary>
    ushort? Shell() =>
        Pick(b => !Palette.IsObject(b) && !Palette.IsCutout(b) && PowerOf(b) > 0, b => -PowerOf(b)!.Value)
        ?? Pick(b => !Palette.IsObject(b) && !Palette.IsCutout(b) && Palette.Item(b).Components.Count == 0 && PowerOf(b) <= 0, b => Math.Abs(PowerOf(b)!.Value))
        ?? Hull();

    /// <summary>A console to pilot from: a full one before a guns-only one, a single-cell one first.</summary>
    ushort? ConsoleBlock() =>
        Pick(b => Palette.Item(b).Get<ConsoleComponent>() is { GunsOnly: false }, b => Palette.IsObject(b) ? 1 : 0)
        ?? Pick(Has<ConsoleComponent>, b => Palette.IsObject(b) ? 1 : 0);

    // ---- placing ----

    /// <summary>The turn that points a fitting out of a face: a thruster's flame or a gun's barrel along
    /// <paramref name="normal"/>; anything else stands the default way.</summary>
    Orientation Facing(ushort block, Int3 normal)
    {
        var item = Palette.Item(block);
        if (item.Get<ThrusterComponent>() is { } thruster && Palette.Shape(block) is { } shape)
            return Orientation.All.FirstOrDefault(o =>
                Num.Vector3.Distance(ThrusterMount.FromShape(shape, Int3.Zero, o, thruster).LocalFlameDirection, normal.ToVector3()) < 1e-3f);
        if (item.Get<GunComponent>() is not null)
            return Orientation.All.FirstOrDefault(o => o.Apply(Int3.UnitY) == normal);
        return Orientation.Default;
    }

    /// <summary>Puts <paramref name="block"/> against the face of <paramref name="hullCell"/> facing
    /// <paramref name="normal"/>; its console target (an object's origin, a grid block's cell), or null when it doesn't fit.</summary>
    Int3? Fit(BlockGrid grid, ushort block, Int3 hullCell, Int3 normal)
    {
        var place = hullCell + normal;
        var turn = Facing(block, normal);
        if (Palette.Shape(block) is { } shape)
        {
            var origin = shape.AnchorFor(place, normal, turn);
            if (grid.ObjectProblem(block, origin, turn) is not null) return null;
            return grid.AddObject(block, origin, turn) is null ? null : origin;
        }
        var cell = place.ToGodot();
        if (grid.IsSolid(cell) || grid.FootprintObjectAt(cell) is not null) return null;
        return grid.Set(cell, block, turn) ? place : null;
    }

    /// <summary>Fills a box of <paramref name="size"/> at <paramref name="min"/>: its outside layer of
    /// <paramref name="shell"/>, the inside of <paramref name="hull"/>.</summary>
    static void FillBox(BlockGrid grid, Int3 min, Int3 size, ushort shell, ushort hull)
    {
        var cells = new List<(Vector3I, ushort)>((int)size.Volume);
        for (int z = 0; z < size.Z; z++)
        for (int y = 0; y < size.Y; y++)
        for (int x = 0; x < size.X; x++)
        {
            var local = new Int3(x, y, z);
            cells.Add(((min + local).ToGodot(), StressTest.IsShell(local, size) ? shell : hull));
        }
        grid.SetCells(cells);
    }

    /// <summary>The console on the top face's corner cell (no fitting slot is there): a grid console takes the shell
    /// cell's place, a multi-block one stands on it. Its cell as the bindings know it, or null.</summary>
    Int3? PlaceConsole(BlockGrid grid, ushort console, Int3 min, Int3 size)
    {
        var corner = min + new Int3(0, size.Y - 1, 0);
        if (!Palette.IsObject(console)) return grid.Set(corner.ToGodot(), console) ? corner : null;
        return Fit(grid, console, corner, Int3.UnitY);
    }

    /// <summary>A new empty ship, or why the world can't take another.</summary>
    ShipGrid? NewShip(out string? problem)
    {
        long shipCells = 0;
        foreach (var existing in grids.Ships) shipCells += (long)existing.GridSize * existing.GridSize * existing.GridSize;
        problem = ShipLaunch.FleetProblem(grids.Ships.Count, shipCells);
        return problem is null ? grids.AddShip() : null;
    }

    /// <summary>
    /// Moves a freshly built ship (world-aligned, at rest) ahead of the player, further on (then above) while it would
    /// overlap another grid: every block it was built of is checked, the single-cell fittings outside the hull box too.
    /// Returns how far ahead it ended up, and whether that spot is free.
    /// </summary>
    (float Distance, bool Free) PlaceAhead(ShipGrid ship, Int3 min, Int3 size, float reach)
    {
        var center = min.ToVector3() + size.ToVector3() * 0.5f;
        var cells = ship.Blocks().Select(b => b.Cell).Concat(ship.Objects.SelectMany(o => o.Footprint)).ToList();
        var eye = player.GlobalPosition;
        var forward = -player.GlobalTransform.Basis.Z;
        float radius = size.ToVector3().Length() * 0.5f + reach;
        float distance = radius + 6;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float ahead = radius + 6 + (attempt % 6) * radius;
            var lift = attempt < 6 ? Vector3.Zero : Vector3.Up * radius * 2;
            ship.GlobalPosition = eye + forward * ahead + lift - center.ToGodot();
            distance = ahead;
            if (grids.OverlapProblem(ship, cells) is null) return (distance, true);
        }
        return (distance, false);
    }

    // ---- the scenes ----

    string Ship(int blocks)
    {
        if (Hull() is not { } hull || Shell() is not { } shell) return "The itemset has no plain single-cell block to build a hull from.";
        if (ConsoleBlock() is not { } console) return "The itemset has no console block, so the ship could not be piloted.";
        // The strongest thruster, so the heavy hull still gets moving within a benchmark run.
        var thruster = Pick(b => Palette.IsObject(b) && Has<ThrusterComponent>(b), b => -Palette.Item(b).Get<ThrusterComponent>()!.Force);
        var light = Pick(Has<LightComponent>, b => Volume(b));
        var power = Pick(b => PowerOf(b) > 0, b => (Palette.IsObject(b) ? 0 : 1_000_000) - PowerOf(b)!.Value);
        var door = Pick(b => Palette.IsObject(b) && Has<DoorComponent>(b), b => Volume(b));
        var fittings = new[] { thruster, light, power, door }.OfType<ushort>().ToArray();

        if (NewShip(out var full) is not { } ship) return full!;
        var size = StressTest.HullSize(blocks);
        var min = new Int3((ship.GridSize - size.X) / 2, (ship.GridSize - size.Y) / 2, (ship.GridSize - size.Z) / 2);
        FillBox(ship, min, size, shell, hull);
        var consoleCell = PlaceConsole(ship, console, min, size);

        // Wide enough apart that the biggest fitting (a thruster stands out along its face's normal) fits each slot.
        int spacing = 1 + fittings.Where(b => !Has<ThrusterComponent>(b))
            .Select(b => Palette.Shape(b) is { } s ? Math.Max(s.Size.X, Math.Max(s.Size.Y, s.Size.Z)) : 1).DefaultIfEmpty(1).Max();
        spacing = Math.Clamp(spacing, 2, 8);
        var placed = new Dictionary<ushort, int>();
        var forward = new List<Int3>();
        var back = new List<Int3>();
        int next = 0;
        foreach (var (cell, normal) in StressTest.FaceSlots(size, spacing))
        {
            if (fittings.Length == 0) break;
            var block = fittings[next++ % fittings.Length];
            if (Fit(ship, block, min + cell, normal) is not { } target) continue;
            placed[block] = placed.GetValueOrDefault(block) + 1;
            // Flames out of the back push it forward (Godot's forward is -Z), as the demo ship's W group.
            if (block == thruster && normal == Int3.UnitZ) forward.Add(target);
            if (block == thruster && normal == -Int3.UnitZ) back.Add(target);
        }

        string bound = "";
        if (consoleCell is { } cid && Palette.Item(console).Get<ConsoleComponent>() is { GunsOnly: false } component)
        {
            Bind(ship, cid, component, 0, ForwardKey, forward);
            Bind(ship, cid, component, 1, BackKey, back);
            bound = $", {forward.Count} thrusters on {ForwardKey} and {back.Count} on {BackKey}";
        }
        int reach = 1 + fittings.Select(b => Palette.Shape(b) is { } s ? Math.Max(s.Size.X, Math.Max(s.Size.Y, s.Size.Z)) : 1).DefaultIfEmpty(1).Max();
        var (ahead, free) = PlaceAhead(ship, min, size, reach);
        var (supply, demand, _) = ship.Systems.PowerSummary;
        string kinds = string.Join(", ", placed.Select(p => $"{p.Value} {Palette.Item(p.Key).Name}"));
        string message = FormattableString.Invariant(
            $"Built {ship.Name}: {size.X}x{size.Y}x{size.Z} hull ({size.Volume} blocks of {Palette.Item(hull).Name} inside {Palette.Item(shell).Name}), ") +
            $"{ship.Objects.Count} multi-blocks ({kinds}), console {Palette.Item(console).Name}{bound}; " +
            FormattableString.Invariant($"{ship.BlockCount} blocks, mass {ship.Mass.Mass:0}, power {supply - demand:+0;-0}, {ahead:0} m ahead") +
            (free ? "." : ", overlapping something (no free spot found).");
        GD.Print($"stress: {message}");
        return message;
    }

    string Guns(int count)
    {
        if (Hull() is not { } hull || Shell() is not { } shell) return "The itemset has no plain single-cell block to build a platform from.";
        if (Pick(Has<GunComponent>, b => Volume(b) * 100_000 + Palette.Item(b).Get<GunComponent>()!.ReloadMs) is not { } gun)
            return "The itemset has no ship gun.";
        var console = ConsoleBlock();

        if (NewShip(out var full) is not { } ship) return full!;
        int spacing = 1 + (Palette.Shape(gun) is { } shape ? Math.Max(shape.Size.X, shape.Size.Z) : 1);
        var size = StressTest.PlateSize(count, spacing);
        var min = new Int3((ship.GridSize - size.X) / 2, (ship.GridSize - size.Y) / 2, (ship.GridSize - size.Z) / 2);
        FillBox(ship, min, size, shell, hull);
        var consoleCell = console is { } c ? PlaceConsole(ship, c, min, size) : null;

        var guns = new List<Int3>();
        foreach (var (cell, normal) in StressTest.FaceSlots(size, spacing))
        {
            if (guns.Count == count) break;
            if (Fit(ship, gun, min + cell, normal) is { } target) guns.Add(target);
        }

        string bound = "unbound (the itemset has no console)";
        if (consoleCell is { } cid && Palette.Item(console!.Value).Get<ConsoleComponent>() is { } component)
        {
            Bind(ship, cid, component, 0, ForwardKey, guns);
            bound = $"bound to {Palette.Item(console.Value).Name}'s first group, key {ForwardKey}";
        }
        int reach = 1 + (Palette.Shape(gun) is { } g ? Math.Max(g.Size.X, Math.Max(g.Size.Y, g.Size.Z)) : 1);
        var (ahead, free) = PlaceAhead(ship, min, size, reach);
        string message = FormattableString.Invariant(
            $"Built {ship.Name}: {guns.Count} of {count} {Palette.Item(gun).Name} on a {size.X}x{size.Y}x{size.Z} plate, {bound}; ") +
            FormattableString.Invariant($"{ship.BlockCount} blocks, {ahead:0} m ahead") +
            (free ? "." : ", overlapping something (no free spot found).");
        GD.Print($"stress: {message}");
        return message;
    }

    /// <summary>Binds <paramref name="targets"/> to colour group <paramref name="group"/> of the console at
    /// <paramref name="consoleCell"/>, under <paramref name="key"/>.</summary>
    static void Bind(ShipGrid ship, Int3 consoleCell, ConsoleComponent component, int group, string key, IEnumerable<Int3> targets)
    {
        if (group >= ConsoleBinding.ClampGroupCount(component.Buttons)) return;
        ship.Consoles.AssignKey(consoleCell, component.Buttons, component.GunsOnly, group, key);
        var state = ship.Consoles.GetOrCreate(consoleCell, component.Buttons, component.GunsOnly);
        foreach (var target in targets)
        {
            // Each cycle moves a target one group on, from unbound to the first.
            for (int i = 0; i <= group && state.GroupOf(target) != group; i++)
                ship.Consoles.CycleTarget(consoleCell, component.Buttons, component.GunsOnly, target, isThruster: true, isGun: true);
        }
    }

    string Creatures(CreatureKind kind, int count)
    {
        var eye = player.GlobalPosition;
        var forward = (-player.GlobalTransform.Basis.Z).ToNumerics();
        int before = creatures.Count;
        bool flyer = kind is CreatureKind.Scavenger or CreatureKind.Needle;
        // Flyers appear where they are spread to; parasites and eggs need rock under each ray, so more rays are tried
        // (the spiral widens as it goes, so the first rays stay on what the player aims at).
        int rays = flyer ? count : count * 4;
        string? last = null;
        for (int i = 0; i < rays && creatures.Count - before < count; i++)
            last = creatures.SpawnNear(kind, eye, StressTest.SpreadDirection(forward, i, rays, CreatureCone).ToGodot());
        int spawned = creatures.Count - before;
        string message = $"Spawned {spawned} of {count} {kind}; {creatures.Count} creatures in the world." +
                         (spawned < count && last is not null ? $" ({last})" : "");
        GD.Print($"stress: {message}");
        return message;
    }

    string Objects(int count)
    {
        var world = grids.World;
        var kinds = Palette.ObjectIndices.OrderBy(b => Palette.Item(b).ItemId).ToList();
        if (kinds.Count == 0) return "The itemset has no multi-blocks.";
        int spacing = 1 + Math.Min(8, kinds.Max(b => Palette.Shape(b) is { } s ? Math.Max(s.Size.X, Math.Max(s.Size.Y, s.Size.Z)) : 1));
        // Room for more spots than objects: rock and the player take some.
        int side = (int)Math.Ceiling(Math.Cbrt(count)) + 2;
        int extent = side * spacing;
        var eye = player.GlobalPosition;
        var ahead = eye + (-player.GlobalTransform.Basis.Z) * (extent * 0.5f + 4);
        var start = (Vector3I)(ahead - Vector3.One * (extent * 0.5f)).Round();
        int limit = Math.Max(0, world.GridSize - extent);
        start = new Vector3I(Math.Clamp(start.X, 0, limit), Math.Clamp(start.Y, 0, limit), Math.Clamp(start.Z, 0, limit));

        int placed = 0;
        for (int i = 0; i < side * side * side && placed < count; i++)
        {
            var block = kinds[placed % kinds.Count];
            var origin = start.ToInt3() + StressTest.LatticePoint(i, side) * spacing;
            var turn = Orientation.Default;
            if (world.ObjectProblem(block, origin, turn) is not null) continue;
            var footprint = Palette.Shape(block)!.Footprint(origin, turn).Select(c => c.ToGodot()).ToList();
            if (footprint.Any(p => PlayerMotor.BodyOccupies(eye, p)) || grids.OverlapProblem(world, footprint) is not null) continue;
            if (world.AddObject(block, origin, turn) is not null) placed++;
        }
        string message = $"Placed {placed} of {count} multi-blocks ({Math.Min(placed, kinds.Count)} kinds) in the static world, " +
                         $"{world.Objects.Count} there now.";
        GD.Print($"stress: {message}");
        return message;
    }
}
