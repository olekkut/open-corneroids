using Godot;
using OpenCorneroids.Simulation.Ships;
using ShipLaunch = OpenCorneroids.Simulation.Ships.Launch;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.World;

/// <summary>Where the player aims: the solid cell a ray stopped at and the empty cell it came from, in one grid's local
/// cells. A new block goes into <see cref="Before"/>, in that grid.</summary>
public readonly record struct Aim(BlockGrid Grid, Vector3I Hit, Vector3I Before);

/// <summary>
/// Every block grid of one play session: the static world and the ships. Answers the questions that span grids (what
/// does a ray hit first) and owns ship creation, launch and removal; everything inside one grid stays with
/// <see cref="BlockGrid"/>.
/// </summary>
public sealed class GridSet
{
    readonly Node3D _shipParent;
    readonly List<ShipGrid> _ships = [];
    int _nextShip = 1;

    /// <param name="shipParent">The node ships are added to; it should sit at the world origin, unturned.</param>
    public GridSet(VoxelWorld world, Node3D shipParent)
    {
        World = world;
        _shipParent = shipParent;
    }

    public VoxelWorld World { get; }

    public IReadOnlyList<ShipGrid> Ships => _ships;

    /// <summary>The world first, then the ships in creation order.</summary>
    public IEnumerable<BlockGrid> All
    {
        get
        {
            yield return World;
            foreach (var ship in _ships) yield return ship;
        }
    }

    /// <summary>One palette for every grid: ships are built from the world's itemset.</summary>
    public BlockPalette Palette => World.Palette;

    /// <summary>Whether the grid is still part of the session (a ship mined down to nothing is removed).</summary>
    public bool Contains(BlockGrid grid) => grid == World || (grid is ShipGrid ship && _ships.Contains(ship));

    /// <summary>An empty ship grid at the world origin, unturned, sharing the world's palette, textures and settings.</summary>
    /// <param name="size">Edge of its local grid; a multiple of <see cref="BlockGrid.ChunkSize"/>.</param>
    public ShipGrid AddShip(int size = ShipLaunch.ShipGridSize)
    {
        var ship = new ShipGrid(size) { Name = $"Ship_{_nextShip++}", AutoCloseDoors = World.AutoCloseDoors };
        ship.Initialize(World.Palette, World.Textures, World.Mapping);
        _shipParent.AddChild(ship);
        _ships.Add(ship);
        return ship;
    }

    public void RemoveShip(ShipGrid ship)
    {
        if (!_ships.Remove(ship)) return;
        ship.QueueFree();
    }

    /// <summary>Breaks a target in whichever grid it is; a ship left with no blocks at all goes away.</summary>
    public void Break(BlockTarget target)
    {
        target.Grid.Break(target);
        if (target.Grid is ShipGrid ship && ship.IsEmpty) RemoveShip(ship);
    }

    /// <summary>
    /// The first solid cell along a world-space ray in any grid: the ray is taken into each grid's local space, cast
    /// there (<see cref="VoxelRaycast"/>), and the nearest hit wins. Ships whose bounding sphere the ray can't reach
    /// are skipped.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out Aim aim) =>
        Raycast(origin, direction, maxDistance, out aim, out _);

    /// <summary>As above, also giving how far along the ray the hit cell starts (0 when the ray starts inside it).</summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out Aim aim, out float distance)
    {
        aim = default;
        float best = float.PositiveInfinity;
        var dir = direction.Normalized();
        // Indexed rather than through All: this runs every frame and should not allocate.
        CastInto(World, origin, dir, maxDistance, ref best, ref aim);
        for (int i = 0; i < _ships.Count; i++)
            if (Reaches(_ships[i], origin, dir, maxDistance)) CastInto(_ships[i], origin, dir, maxDistance, ref best, ref aim);
        distance = best;
        return best < float.PositiveInfinity;
    }

    static void CastInto(BlockGrid grid, Vector3 origin, Vector3 dir, float maxDistance, ref float best, ref Aim aim)
    {
        var localOrigin = grid.ToLocal(origin);
        var localDir = grid.DirectionToLocal(dir);
        if (!VoxelRaycast.Cast(grid, localOrigin, localDir, Math.Min(maxDistance, best), out var hit, out var before, out var distance) ||
            distance >= best)
            return;
        best = distance;
        aim = new Aim(grid, hit, before);
    }

    /// <summary>
    /// Why a block can't go into <paramref name="cells"/> (local cells of <paramref name="grid"/>) because ANOTHER grid
    /// already has something there: a grid block, or any part of an object's footprint (so a ship's thruster can't be
    /// built into rock, nor a world block into a ship's exhaust space). Null when nothing is in the way. Each cell's cube
    /// is taken into the other grid's local space and every cell its axis-aligned bounds there touch is checked: exact
    /// for grids that are only shifted by whole cells (a launched ship), a little conservative for a turned ship (our
    /// approximation, like the player's body in <see cref="Player.PlayerMotor"/>).
    /// </summary>
    public string? OverlapProblem(BlockGrid grid, IEnumerable<Vector3I> cells)
    {
        var (center, radius) = grid.Bounds;
        if (grid != World && OverlapIn(World, grid, center, radius, cells) is { } world) return world;
        for (int i = 0; i < _ships.Count; i++)
            if (_ships[i] != grid && OverlapIn(_ships[i], grid, center, radius, cells) is { } ship) return ship;
        return null;
    }

    string? OverlapIn(BlockGrid other, BlockGrid grid, Vector3 center, float radius, IEnumerable<Vector3I> cells)
    {
        var (otherCenter, otherRadius) = other.Bounds;
        if (center.DistanceTo(otherCenter) > radius + otherRadius + 1) return null;
        // grid-local → other-local; neither grid is ever scaled, so a unit cube's bounds there are |basis| · ½ per axis.
        var toOther = other.GlobalTransform.AffineInverse() * grid.GlobalTransform;
        var b = toOther.Basis;
        const float inset = 1e-3f;
        var half = new Vector3(
            0.5f * (Mathf.Abs(b.X.X) + Mathf.Abs(b.Y.X) + Mathf.Abs(b.Z.X)),
            0.5f * (Mathf.Abs(b.X.Y) + Mathf.Abs(b.Y.Y) + Mathf.Abs(b.Z.Y)),
            0.5f * (Mathf.Abs(b.X.Z) + Mathf.Abs(b.Y.Z) + Mathf.Abs(b.Z.Z))) - Vector3.One * inset;
        foreach (var cell in cells)
        {
            var c = toOther * (cell + new Vector3(0.5f, 0.5f, 0.5f));
            var lo = (Vector3I)(c - half).Floor();
            var hi = (Vector3I)(c + half).Floor();
            for (int z = lo.Z; z <= hi.Z; z++)
            for (int y = lo.Y; y <= hi.Y; y++)
            for (int x = lo.X; x <= hi.X; x++)
            {
                var block = other.OccupantAt(new Vector3I(x, y, z));
                if (block == BlockPalette.Air) continue;
                var owner = other is ShipGrid ship ? ship.Name.ToString() : "the world";
                return $"No room: {Palette.Item(block).Name} of {owner} is in the way";
            }
        }
        return null;
    }

    /// <summary>Whether a ray of this length can touch the grid's bounding sphere.</summary>
    static bool Reaches(BlockGrid grid, Vector3 origin, Vector3 dir, float maxDistance)
    {
        var (center, radius) = grid.Bounds;
        var toCenter = center - origin;
        float along = Mathf.Clamp(toCenter.Dot(dir), 0, maxDistance);
        return (origin + dir * along).DistanceSquaredTo(center) <= radius * radius;
    }

    /// <summary>
    /// E on a console of the static world: the structure attached to it (<see cref="Structure.Collect"/>, stopping at
    /// asteroid rock and minerals, multi-blocks included) leaves the world and becomes a new ship at the same world
    /// position, world-aligned. Refused, with the reason as the message, when the structure is too big
    /// (<see cref="ShipLaunch.Problem"/>) or is only the console.
    /// </summary>
    /// <returns>The new ship (null when refused) and what to tell the player.</returns>
    public (ShipGrid? Ship, string Message) LaunchShip(BlockTarget console)
    {
        if (console.Grid != World) return (null, "That console is already part of a ship");
        long shipCells = 0;
        foreach (var existing in _ships) shipCells += (long)existing.GridSize * existing.GridSize * existing.GridSize;
        if (ShipLaunch.FleetProblem(_ships.Count, shipCells) is { } full) return (null, full);
        var world = World;
        var palette = Palette;
        var start = console.Object is { } consoleObject ? consoleObject.ModelCells.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z).First() : console.Cell;

        int? BlockAt(Simulation.Shapes.Int3 cell)
        {
            var p = cell.ToGodot();
            var block = world.Get(p);
            return block != BlockPalette.Air ? block : world.ObjectAt(p)?.Block;
        }
        var scan = Structure.Collect(start.ToInt3(), BlockAt, id => ShipLaunch.BelongsToShip(palette.Item((ushort)id)), ShipLaunch.MaxCells);

        var cells = new List<(Vector3I Cell, ushort Block)>();
        var objects = new HashSet<WorldObject>();
        foreach (var c in scan.Cells)
        {
            var p = c.ToGodot();
            var block = world.Get(p);
            if (block != BlockPalette.Air) cells.Add((p, block));
            else if (world.ObjectAt(p) is { } obj) objects.Add(obj);
        }
        var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
        foreach (var (p, _) in cells) (min, max) = (min.Min(p), max.Max(p));
        foreach (var obj in objects)
        {
            var origin = obj.Origin.ToGodot();
            (min, max) = (min.Min(origin), max.Max(origin + obj.RotatedSize.ToGodot() - Vector3I.One));
        }
        if (ShipLaunch.Problem(cells.Count + objects.Count, min.ToInt3(), max.ToInt3(), scan.Truncated) is { } problem)
            return (null, problem);

        // The structure's bounds are centred in the ship grid, whose origin then sits so that each block keeps its world
        // position: world cell w is ship cell w + shift.
        var shift = ShipLaunch.LocalMin(min.ToInt3(), max.ToInt3()).ToGodot() - min;
        // The whole door state goes along, not just how far open: a door still opening keeps opening and an open one
        // keeps its time before closing.
        var saved = objects.OrderBy(o => o.Id).Select(o => (o.Block, Origin: o.Origin.ToGodot() + shift, o.Orientation, o.DoorState)).ToList();
        // Turned grid blocks keep their turn on the ship (clearing the world cells forgets it there).
        var turns = cells.Select(c => (Cell: c.Cell + shift, Turn: world.TurnOf(c.Cell))).Where(t => t.Turn != Orientation.Default).ToList();
        var movingCells = new HashSet<Simulation.Shapes.Int3>();
        foreach (var (cell, _) in cells) movingCells.Add(cell.ToInt3());
        foreach (var obj in objects) movingCells.Add(obj.Origin);
        var consoles = world.Consoles.ExtractAndShift(movingCells, -shift.ToInt3());
        world.ClearCells(cells.Select(c => c.Cell).ToList());
        foreach (var obj in objects) world.RemoveObject(obj);

        var ship = AddShip();
        ship.Position = -(Vector3)shift;
        ship.SetCells(cells.Select(c => (c.Cell + shift, c.Block)).ToList());
        ship.SetTurns(turns);
        foreach (var (block, origin, orientation, door) in saved)
        {
            var added = ship.AddObject(block, origin.ToInt3(), orientation, door.Open);
            if (added is null) GD.PushWarning($"Launch: {palette.Item(block).Name} did not fit the new ship at {origin}");
            else if (added.Door is not null) added.DoorState = door;
        }
        ship.ImportConsoles(consoles.ToSnapshot());

        int blocks = cells.Count + objects.Count;
        var message = FormattableString.Invariant($"Ship launched: {blocks} blocks, {ship.Mass.Mass:0.#} mass");
        GD.Print($"{message} ({ship.Name} at {ship.Position}, grid {ship.GridSize}³)");
        return (ship, message);
    }

    /// <summary>
    /// Finds the grid whose closest solid block is nearest to <paramref name="worldPos"/> within
    /// <paramref name="maxDistance"/> (16 units in the original). Returns the grid, the local cell of that nearest
    /// block, and the world-space point the red attachment line connects to (the ship's centre of mass for a ship, or
    /// the nearest block's centre for the static world).
    /// </summary>
    public (BlockGrid Grid, Vector3I NearestCell, Vector3 LineTargetWorld)? NearestAttachment(
        Vector3 worldPos, float maxDistance = ConsoleBinding.MaxAttachmentDistance)
    {
        BlockGrid? bestGrid = null;
        Vector3I bestCell = default;
        float bestDistance = maxDistance;

        void Check(BlockGrid grid)
        {
            var (center, radius) = grid.Bounds;
            if (worldPos.DistanceTo(center) > radius + bestDistance) return;
            var localPos = grid.ToLocal(worldPos);
            if (grid.NearestSolidBlock(localPos, bestDistance) is not { } hit) return;
            bestDistance = hit.Distance;
            bestGrid = grid;
            bestCell = hit.Cell;
        }

        Check(World);
        for (int i = 0; i < _ships.Count; i++) Check(_ships[i]);

        if (bestGrid is null) return null;
        var targetWorld = bestGrid is ShipGrid ship
            ? ship.GlobalTransform * ship.Mass.CenterOfMass.ToGodot()
            : bestGrid.ToGlobal((Vector3)bestCell + new Vector3(0.5f, 0.5f, 0.5f));
        return (bestGrid, bestCell, targetWorld);
    }

    /// <summary>
    /// Places a block in empty space when no existing grid is within <see cref="ConsoleBinding.MaxAttachmentDistance"/>,
    /// starting a new floating ship whose first block sits centred at <paramref name="worldPos"/>.
    /// </summary>
    public (ShipGrid? Ship, string? Problem) CreateFloatingShip(ushort block, Vector3 worldPos, Orientation orientation)
    {
        long shipCells = 0;
        foreach (var existing in _ships) shipCells += (long)existing.GridSize * existing.GridSize * existing.GridSize;
        if (ShipLaunch.FleetProblem(_ships.Count, shipCells) is { } full) return (null, full);

        var origin = new Vector3I(ShipLaunch.ShipGridSize / 2, ShipLaunch.ShipGridSize / 2, ShipLaunch.ShipGridSize / 2);
        var shape = Palette.Shape(block);
        var size = shape is not null ? (Vector3)shape.RotatedSize(orientation).ToGodot() : Vector3.One;
        var ship = AddShip();
        ship.Position = worldPos - ((Vector3)origin + size * 0.5f);
        if (shape is not null)
            ship.AddObject(block, origin.ToInt3(), orientation);
        else
            ship.Set(origin, block, orientation);
        return (ship, null);
    }
}
