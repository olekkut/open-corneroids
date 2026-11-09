using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.World;

/// <summary>What the player aims at: a grid block, or a whole world object, in one grid.</summary>
/// <param name="Grid">The grid it belongs to: the static world or a ship.</param>
/// <param name="Cell">The grid cell (grid-local); for an object its origin, so aiming at different cells of one object
/// is one target (drilling progress survives moving the aim across a door).</param>
/// <param name="Object">The object, or null for a grid block.</param>
public readonly record struct BlockTarget(BlockGrid Grid, Vector3I Cell, WorldObject? Object);

/// <summary>
/// A bounded cubic block grid with its own transform: the static asteroid world (<see cref="VoxelWorld"/>) or a ship
/// (<see cref="ShipGrid"/>). Single-cell blocks live in a chunked array, each chunk meshed into one MeshInstance3D;
/// multi-cell blocks are world objects (<see cref="WorldObject"/>) beside it. Block (x,y,z) occupies the unit cube from
/// (x,y,z) to (x+1,y+1,z+1) in the grid's LOCAL coordinates; meshes, lights and object nodes are children of this node,
/// so moving or turning the node moves everything. Point-light blocks get a real light.
/// </summary>
/// <remarks>
/// Two occupancy maps sit next to the cells: footprint cell → object (what placing must not overlap) and model cell →
/// object (what is solid). <see cref="IsSolid"/> is the single answer to "can I pass through / does the ray stop
/// here", shared by collision, the ray cast and mining; footprint space that is not model (thruster exhaust, door
/// travel) is passable but reserved. Every query and edit takes grid-local cells; <see cref="Node3D.ToLocal"/> and
/// <see cref="Node3D.ToGlobal"/> convert.
/// </remarks>
public abstract partial class BlockGrid : Node3D
{
    public const int ChunkSize = 16;
    const int MaxLights = 64;

    readonly ushort[] _cells;
    readonly MeshInstance3D?[] _chunkNodes;
    readonly int _chunks;
    readonly Dictionary<Vector3I, OmniLight3D> _lights = new();
    readonly Dictionary<int, WorldObject> _objects = new();
    readonly Dictionary<Vector3I, WorldObject> _footprints = new();
    readonly Dictionary<Vector3I, WorldObject> _solid = new();
    readonly HashSet<Vector3I> _fullCells = new();
    /// <summary>Grid cells drawn with their unpowered textures (<see cref="SetInactive"/>).</summary>
    readonly HashSet<Vector3I> _inactive = new();
    /// <summary>Grid cells holding a powered block (<see cref="BlockPalette.IsPowered"/>), kept up to date by every
    /// edit so power networks never scan the whole grid.</summary>
    readonly HashSet<Vector3I> _powered = new();
    int _nextObjectId = 1;
    /// <summary>Lights of light-emitting objects, capped separately from grid lights so neither starves the other.</summary>
    int _objectLights;
    BlockPalette _palette = null!;
    BlockTextures _textures = null!;
    TextureMapping _mapping;

    /// <param name="size">Edge length in cells; a positive multiple of <see cref="ChunkSize"/>.</param>
    protected BlockGrid(int size)
    {
        if (size < ChunkSize || size % ChunkSize != 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, $"A grid edge must be a positive multiple of {ChunkSize}.");
        GridSize = size;
        _chunks = size / ChunkSize;
        _cells = new ushort[size * size * size];
        _chunkNodes = new MeshInstance3D?[_chunks * _chunks * _chunks];
        SolidQuery = (x, y, z) => IsSolid(new Vector3I(x, y, z));
        Systems = new GridSystems(this);
    }

    /// <summary>Power networks and how they look, and for a ship its thrusters. The static world has them too, so a
    /// structure shows the same textures before and after it is launched.</summary>
    public GridSystems Systems { get; }

    /// <summary>Console colour-group bindings, key assignments, and bound cameras on this grid.</summary>
    public ShipConsoleBindings Consoles { get; } = new();

    /// <summary>Edge length of the cubic grid, in cells.</summary>
    public int GridSize { get; }

    public BlockPalette Palette => _palette;

    /// <summary>Bumped by every edit (blocks, objects), so derived data such as a ship's mass knows when to recompute.</summary>
    public int Revision { get; private set; }

    /// <summary>Bumped whenever an object's model cells move (a door sliding), which <see cref="Revision"/> does not
    /// count: what is solid changed, but nothing was built or broken.</summary>
    public int ModelRevision { get; private set; }

    /// <summary>
    /// When false, open doors stay open instead of closing after their time (<c>--open-doors</c>, so a screenshot taken
    /// seconds later still shows them open).
    /// </summary>
    public bool AutoCloseDoors { get; set; } = true;

    public IReadOnlyCollection<WorldObject> Objects => _objects.Values;

    /// <summary>The refusal for a placement that leaves the grid, e.g. "No room: that would reach outside the world".</summary>
    protected abstract string OutsideMessage { get; }

    public void Initialize(BlockPalette palette, BlockTextures textures, TextureMapping mapping)
    {
        _palette = palette;
        _textures = textures;
        _mapping = mapping;
    }

    /// <summary>
    /// The block's textured model as placed objects show it (same faces, regions and <see cref="TextureMapping"/>),
    /// built at origin zero so one mesh serves wherever it is moved to. For the placement ghost, with its own material.
    /// </summary>
    public ArrayMesh? ModelMesh(ushort block, Orientation o, Material material) =>
        _palette.Shape(block) is { } shape ? ObjectMesher.Build(shape, Int3.Zero, o, _mapping, _textures, material) : null;

    /// <summary>The block atlas, e.g. for a material that draws models differently (the placement ghost).</summary>
    public BlockTextures Textures => _textures;

    /// <summary>How multi-block faces lay out their texture regions; a new grid (a ship) takes the world's.</summary>
    public TextureMapping Mapping => _mapping;

    // ---- coordinates ----

    /// <summary>A world direction in local axes (the grid is never scaled, so lengths are kept).</summary>
    public Vector3 DirectionToLocal(Vector3 world) => GlobalTransform.Basis.Inverse() * world;

    /// <summary>The world centre of the grid's cube and its bounding-sphere radius, for cheap "can this matter" tests.</summary>
    public (Vector3 Center, float Radius) Bounds => (ToGlobal(Vector3.One * (GridSize * 0.5f)), GridSize * 0.8661f);

    /// <summary>How many 16³ sectors hold any block: what the original counts to cap the parasites on an entity.</summary>
    public int FilledChunks => _chunkNodes.Count(n => n is not null);

    /// <summary>
    /// Finds the closest solid cell (grid block or object model cell) within <paramref name="maxDistance"/> of
    /// <paramref name="localPos"/> (grid-local coordinates). Empty chunks are skipped in one check.
    /// </summary>
    public (Vector3I Cell, float Distance)? NearestSolidBlock(Vector3 localPos, float maxDistance)
    {
        float bestDistSq = maxDistance * maxDistance;
        Vector3I? bestCell = null;

        int minCx = Math.Max(0, Mathf.FloorToInt((localPos.X - maxDistance) / ChunkSize));
        int maxCx = Math.Min(_chunks - 1, Mathf.FloorToInt((localPos.X + maxDistance) / ChunkSize));
        int minCy = Math.Max(0, Mathf.FloorToInt((localPos.Y - maxDistance) / ChunkSize));
        int maxCy = Math.Min(_chunks - 1, Mathf.FloorToInt((localPos.Y + maxDistance) / ChunkSize));
        int minCz = Math.Max(0, Mathf.FloorToInt((localPos.Z - maxDistance) / ChunkSize));
        int maxCz = Math.Min(_chunks - 1, Mathf.FloorToInt((localPos.Z + maxDistance) / ChunkSize));

        int loX = Math.Max(0, Mathf.FloorToInt(localPos.X - maxDistance));
        int hiX = Math.Min(GridSize - 1, Mathf.FloorToInt(localPos.X + maxDistance));
        int loY = Math.Max(0, Mathf.FloorToInt(localPos.Y - maxDistance));
        int hiY = Math.Min(GridSize - 1, Mathf.FloorToInt(localPos.Y + maxDistance));
        int loZ = Math.Max(0, Mathf.FloorToInt(localPos.Z - maxDistance));
        int hiZ = Math.Min(GridSize - 1, Mathf.FloorToInt(localPos.Z + maxDistance));

        for (int cz = minCz; cz <= maxCz; cz++)
        for (int cy = minCy; cy <= maxCy; cy++)
        for (int cx = minCx; cx <= maxCx; cx++)
        {
            if (_chunkNodes[(cz * _chunks + cy) * _chunks + cx] is null) continue;
            int z0 = Math.Max(loZ, cz * ChunkSize), z1 = Math.Min(hiZ, (cz + 1) * ChunkSize - 1);
            int y0 = Math.Max(loY, cy * ChunkSize), y1 = Math.Min(hiY, (cy + 1) * ChunkSize - 1);
            int x0 = Math.Max(loX, cx * ChunkSize), x1 = Math.Min(hiX, (cx + 1) * ChunkSize - 1);
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (_cells[(z * GridSize + y) * GridSize + x] == BlockPalette.Air) continue;
                float d2 = localPos.DistanceSquaredTo(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
                if (d2 <= bestDistSq)
                {
                    bestDistSq = d2;
                    bestCell = new Vector3I(x, y, z);
                }
            }
        }

        foreach (var obj in _objects.Values)
            foreach (var cell in obj.ModelCells)
            {
                float d2 = localPos.DistanceSquaredTo(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));
                if (d2 <= bestDistSq)
                {
                    bestDistSq = d2;
                    bestCell = cell;
                }
            }

        return bestCell is { } found ? (found, MathF.Sqrt(bestDistSq)) : null;
    }

    protected static readonly Vector3I[] Steps = [Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward];

    int _clusterRevision = -1;
    readonly Dictionary<Vector3I, Vector3> _clusterCenterCache = new();

    /// <summary>
    /// Returns the world-space centre of mass of the connected solid cluster containing <paramref name="cell"/>.
    /// Cached and invalidated when the grid revision changes.
    /// </summary>
    public Vector3 ClusterCenterOfMass(Vector3I cell)
    {
        if (Revision != _clusterRevision)
        {
            _clusterCenterCache.Clear();
            _clusterRevision = Revision;
        }

        if (_clusterCenterCache.TryGetValue(cell, out var cached))
            return cached;

        if (!InGrid(cell) || !IsSolid(cell))
            return ToGlobal((Vector3)cell + new Vector3(0.5f, 0.5f, 0.5f));

        var visited = new List<Vector3I>();
        var queue = new Queue<Vector3I>();
        var seen = new HashSet<Vector3I>();

        queue.Enqueue(cell);
        seen.Add(cell);

        var sum = Vector3.Zero;
        double totalMass = 0;

        while (queue.TryDequeue(out var current))
        {
            visited.Add(current);
            ushort block = Get(current);
            float mass = Palette.Item(block).Block?.Mass ?? 1f;
            if (mass <= 0f) mass = 1f;
            sum += ((Vector3)current + new Vector3(0.5f, 0.5f, 0.5f)) * mass;
            totalMass += mass;

            foreach (var step in Steps)
            {
                var next = current + step;
                if (InGrid(next) && IsSolid(next) && seen.Add(next))
                    queue.Enqueue(next);
            }
        }

        var center = totalMass > 0 ? (sum / (float)totalMass) : ((Vector3)cell + new Vector3(0.5f, 0.5f, 0.5f));
        var worldCenter = ToGlobal(center);

        foreach (var v in visited)
            _clusterCenterCache[v] = worldCenter;

        return worldCenter;
    }

    // ---- cells ----

    public bool InGrid(Vector3I p) =>
        p.X >= 0 && p.Y >= 0 && p.Z >= 0 && p.X < GridSize && p.Y < GridSize && p.Z < GridSize;

    /// <summary>The grid block at <paramref name="p"/>; objects are not in the grid, see <see cref="IsSolid"/>.</summary>
    public ushort Get(Vector3I p) => InGrid(p) ? _cells[Index(p)] : BlockPalette.Air;

    // ---- turned cells ----
    // A single-cell block normally has no orientation: the cell holds only the block. Blocks whose look or use depends
    // on which way they face (BlockPalette.IsTurnable: a console, a crafting station, a cannon) may be turned when
    // placed; their orientation lives here, keyed by cell, and is cleared whenever the cell changes.
    readonly Dictionary<Vector3I, Orientation> _turned = new();

    /// <summary>How the grid block at <paramref name="p"/> is turned; <see cref="Orientation.Default"/> for most cells.</summary>
    public Orientation TurnOf(Vector3I p) => _turned.Count > 0 && _turned.TryGetValue(p, out var o) ? o : Orientation.Default;

    /// <summary>Every turned cell, for saves and for a launch carrying them onto a ship.</summary>
    public IReadOnlyDictionary<Vector3I, Orientation> Turned => _turned;

    /// <summary>Turns the grid block at <paramref name="p"/> (a turnable one) and remeshes it.</summary>
    public void SetTurn(Vector3I p, Orientation o)
    {
        if (!InGrid(p) || !Turn(p, _cells[Index(p)], o)) return;
        Revision++;
        RebuildAround([p]);
    }

    /// <summary>Records <paramref name="o"/> for the cell without remeshing; false when nothing changed.</summary>
    bool Turn(Vector3I p, ushort block, Orientation o)
    {
        if (block == BlockPalette.Air || o == Orientation.Default || !_palette.IsTurnable(block)) return _turned.Remove(p);
        if (_turned.TryGetValue(p, out var old) && old == o) return false;
        _turned[p] = o;
        return true;
    }

    /// <summary>Copies every cell, in <c>(z * GridSize + y) * GridSize + x</c> order, into <paramref name="buffer"/>
    /// (GridSize³ long), for work done off the main thread on a snapshot.</summary>
    public void CopyCellsTo(ushort[] buffer) => _cells.CopyTo(buffer, 0);

    /// <summary>A grid block or an object's model is there: collision, ray casts and mining stop here.</summary>
    public bool IsSolid(Vector3I p) => Get(p) != BlockPalette.Air || _solid.ContainsKey(p);

    /// <summary><see cref="IsSolid"/> in the form <see cref="Simulation.Physics.VoxelCollider"/> takes, made once so the
    /// per-frame collision of the player against every grid allocates nothing.</summary>
    public Func<int, int, int, bool> SolidQuery { get; }

    /// <summary>
    /// What takes up <paramref name="p"/> for another grid's placement: the grid block there, or the object whose
    /// footprint covers it (its model, exhaust or door travel), or air when nothing does (also outside the grid, which is
    /// no concern of another grid's).
    /// </summary>
    public ushort OccupantAt(Vector3I p)
    {
        var block = Get(p);
        return block != BlockPalette.Air ? block : _footprints.TryGetValue(p, out var obj) ? obj.Block : BlockPalette.Air;
    }

    /// <summary>Nothing may be placed here: outside the grid, a grid block, or any object's footprint.</summary>
    public bool IsReserved(Vector3I p) => ReservedProblem(p) is not null;

    /// <summary>Why nothing may be placed at <paramref name="p"/> (see <see cref="IsReserved"/>), or null if something may.</summary>
    public string? ReservedProblem(Vector3I p) =>
        !InGrid(p) ? OutsideMessage
        : Get(p) != BlockPalette.Air ? "No room: blocks are in the way"
        : _footprints.TryGetValue(p, out var obj) ? $"No room: that space belongs to {_palette.Item(obj.Block).Name}"
        : null;

    /// <summary>For the chunk mesher: an opaque object model fills this cell, so grid faces against it are hidden.</summary>
    public bool HidesFaces(Vector3I p) => _fullCells.Contains(p);

    /// <summary>For the chunk mesher: the grid block here is drawn with its unpowered textures.</summary>
    public bool IsInactive(Vector3I p) => _inactive.Count > 0 && _inactive.Contains(p);

    /// <summary>
    /// Which blocks show their unpowered (<c>deactiveTextures</c>) textures: the grid cells in <paramref name="cells"/>
    /// and the objects in <paramref name="objects"/>; everything else shows its active ones. Only what changed is
    /// remeshed: the chunks of grid cells that switched, and the meshes of objects that switched. Blocks without
    /// unpowered textures of their own are skipped, since they look the same either way.
    /// </summary>
    public void SetInactive(IReadOnlySet<Vector3I> cells, IReadOnlySet<WorldObject> objects)
    {
        var wanted = new HashSet<Vector3I>();
        foreach (var p in cells)
            if (_palette.HasInactive(Get(p))) wanted.Add(p);
        var chunks = new HashSet<Vector3I>();
        foreach (var p in _inactive)
            if (!wanted.Contains(p)) chunks.Add(ChunkOf(p));
        foreach (var p in wanted)
            if (!_inactive.Contains(p)) chunks.Add(ChunkOf(p));
        _inactive.Clear();
        _inactive.UnionWith(wanted);
        foreach (var chunk in chunks) RebuildChunk(chunk);

        foreach (var obj in _objects.Values)
        {
            bool active = !objects.Contains(obj) || !_palette.HasInactive(obj.Block);
            if (obj.Active == active) continue;
            obj.Active = active;
            obj.Node.Mesh = BuildObjectMesh(obj.Block, obj.Shape, obj.Origin, obj.Orientation, active);
        }
    }

    /// <summary>The object whose model covers <paramref name="p"/>, if any.</summary>
    public WorldObject? ObjectAt(Vector3I p) => _solid.GetValueOrDefault(p);

    /// <summary>The object whose full footprint (model, exhaust wash or door travel) covers <paramref name="p"/>, if any.</summary>
    public WorldObject? FootprintObjectAt(Vector3I p) => _footprints.GetValueOrDefault(p);

    /// <summary>The object whose placement <see cref="WorldObject.Origin"/> is <paramref name="origin"/>, if any.
    /// Console bindings record an object's origin, which for some orientations is an empty exhaust cell rather than a solid cell.</summary>
    public WorldObject? ObjectAtOrigin(Vector3I origin) =>
        _footprints.TryGetValue(origin, out var obj) && obj.Origin == origin.ToInt3() ? obj : null;

    /// <summary>What a ray stopping at the solid cell <paramref name="hit"/> targets.</summary>
    public BlockTarget TargetAt(Vector3I hit) =>
        ObjectAt(hit) is { } obj ? new BlockTarget(this, obj.Origin.ToGodot(), obj) : new BlockTarget(this, hit, null);

    /// <summary>The palette index of a target (air if it is gone).</summary>
    public ushort BlockOf(BlockTarget target) =>
        target.Object is { } obj ? (_objects.ContainsKey(obj.Id) ? obj.Block : BlockPalette.Air) : Get(target.Cell);

    /// <summary>Removes the target, grid block or whole object.</summary>
    public void Break(BlockTarget target)
    {
        if (target.Object is { } obj) RemoveObject(obj);
        else Set(target.Cell, BlockPalette.Air);
    }

    /// <summary>No grid block and no object left.</summary>
    public bool IsEmpty => _objects.Count == 0 && Array.TrueForAll(_cells, c => c == BlockPalette.Air);

    /// <summary>Every grid block that is not air, with its cell.</summary>
    public IEnumerable<(Vector3I Cell, ushort Block)> Blocks()
    {
        for (int i = 0; i < _cells.Length; i++)
            if (_cells[i] != BlockPalette.Air) yield return (CellOf(i), _cells[i]);
    }

    /// <summary>Grid blocks that take part in power networks, with their cells (objects are in <see cref="Objects"/>).</summary>
    public IEnumerable<(Vector3I Cell, ushort Block)> PoweredBlocks() => _powered.Select(p => (p, Get(p)));

    void TrackPower(Vector3I p, ushort block)
    {
        if (_palette.IsPowered(block)) _powered.Add(p);
        else _powered.Remove(p);
    }

    /// <summary>Sets a block without remeshing; call <see cref="RebuildAll"/> afterwards. For world generation.</summary>
    public void SetRaw(Vector3I p, ushort block)
    {
        if (!InGrid(p)) return;
        if (_cells[Index(p)] != block) Consoles.RemoveBlock(p.ToInt3());
        _cells[Index(p)] = block;
        _turned.Remove(p);
        TrackPower(p, block);
        Revision++;
    }

    /// <summary>Sets a grid block and remeshes the affected chunks. Returns false outside the grid, or for an object
    /// block, which belongs in <see cref="AddObject"/>.</summary>
    public bool Set(Vector3I p, ushort block) => Set(p, block, Orientation.Default);

    /// <summary>As <see cref="Set(Vector3I, ushort)"/>, turned by <paramref name="o"/> if the block is turnable.</summary>
    public bool Set(Vector3I p, ushort block, Orientation o)
    {
        if (!InGrid(p) || (block != BlockPalette.Air && _palette.IsObject(block))) return false;
        if (_cells[Index(p)] != block) Consoles.RemoveBlock(p.ToInt3());
        _cells[Index(p)] = block;
        Turn(p, block, o);
        Revision++;
        UpdateLight(p, block);
        TrackPower(p, block);
        var chunk = new Vector3I(p.X / ChunkSize, p.Y / ChunkSize, p.Z / ChunkSize);
        RebuildChunk(chunk);
        // A block on a chunk border also changes which faces the neighbouring chunk shows.
        for (int axis = 0; axis < 3; axis++)
        {
            int local = p[axis] % ChunkSize;
            var step = Vector3I.Zero;
            step[axis] = 1;
            if (local == 0) RebuildChunk(chunk - step);
            if (local == ChunkSize - 1) RebuildChunk(chunk + step);
        }
        return true;
    }

    /// <summary>Empties many cells with one remesh of the touched chunks (a launch takes a whole structure out).</summary>
    public void ClearCells(IReadOnlyCollection<Vector3I> cells) => SetCells(cells.Select(p => (p, BlockPalette.Air)).ToList());

    /// <summary>
    /// Sets many grid blocks with one remesh of the touched chunks (cells outside the grid and object blocks are
    /// skipped, as <see cref="Set"/> refuses them).
    /// </summary>
    public void SetCells(IReadOnlyCollection<(Vector3I Cell, ushort Block)> cells)
    {
        foreach (var (p, block) in cells)
        {
            if (!InGrid(p) || (block != BlockPalette.Air && _palette.IsObject(block)) || _cells[Index(p)] == block) continue;
            Consoles.RemoveBlock(p.ToInt3());
            _cells[Index(p)] = block;
            _turned.Remove(p);
            UpdateLight(p, block);
            TrackPower(p, block);
        }
        Revision++;
        RebuildAround(cells.Select(c => c.Cell));
    }

    // ---- objects ----

    /// <summary>Why the object block <paramref name="block"/> can't go at <paramref name="origin"/>, or null if it can.
    /// The player's body is the caller's check.</summary>
    public string? ObjectProblem(ushort block, Int3 origin, Orientation o)
    {
        if (_palette.Shape(block) is not { } shape) return $"{_palette.Item(block).Name} is not a multi-block";
        foreach (var cell in shape.Footprint(origin, o))
        {
            var p = cell.ToGodot();
            if (!InGrid(p)) return OutsideMessage;
            if (Get(p) != BlockPalette.Air) return "No room: blocks are in the way";
            if (_footprints.ContainsKey(p)) return $"No room: overlaps {_palette.Item(_footprints[p].Block).Name}";
        }
        return null;
    }

    /// <summary>
    /// Places an object block. Returns null (and changes nothing) when <see cref="ObjectProblem"/> objects, unless
    /// <paramref name="clearGrid"/> is set: then grid blocks in the footprint are removed first (the showcase builds
    /// through rock), though other objects still block it.
    /// </summary>
    /// <param name="open">Door open fraction to resume from (<see cref="DoorState.Resting"/>); ignored for non-doors.</param>
    public WorldObject? AddObject(ushort block, Int3 origin, Orientation o, float open = 0, bool clearGrid = false)
    {
        if (_palette.Shape(block) is not { } shape) return null;
        if (clearGrid)
        {
            var cells = shape.Footprint(origin, o).Select(c => c.ToGodot()).ToList();
            if (cells.Any(p => !InGrid(p) || _footprints.ContainsKey(p))) return null;
            // Cleared without per-cell remeshing (Set rebuilds up to four chunks per cell; a bay door has hundreds of
            // cells), then the touched chunks once.
            ClearCells(cells.Where(p => Get(p) != BlockPalette.Air).ToList());
        }
        else if (ObjectProblem(block, origin, o) is not null) return null;

        var item = _palette.Item(block);
        bool cutout = _palette.IsCutout(block);
        var node = new MeshInstance3D
        {
            Name = $"Object_{_nextObjectId}",
            Mesh = BuildObjectMesh(block, shape, origin, o, active: true),
        };
        var door = item.Get<DoorComponent>();
        var obj = new WorldObject(_nextObjectId++, block, shape, origin, o, door, !cutout, node)
        {
            DoorState = door is null ? DoorState.Closed : DoorState.Resting(open),
        };
        AddChild(node);
        if (item.Get<LightComponent>() is { } light && _objectLights < MaxLights)
        {
            var center = shape.ModelBox(origin, o).Center.ToGodot();
            node.AddChild(new OmniLight3D
            {
                Position = center,
                LightColor = Color.Color8(light.Light.Color.R, light.Light.Color.G, light.Light.Color.B),
                OmniRange = Math.Max(2f, light.Light.Radius * 1.5f),
                LightEnergy = 2f,
            });
            _objectLights++;
        }
        _objects[obj.Id] = obj;
        foreach (var p in obj.Footprint) _footprints[p] = obj;
        MoveModel(obj, obj.DoorState.Open, obj.CellsAt(obj.DoorState.Open));
        Revision++;
        return obj;
    }

    ArrayMesh BuildObjectMesh(ushort block, BlockShape shape, Int3 origin, Orientation o, bool active) =>
        ObjectMesher.Build(shape, origin, o, _mapping, _textures, _palette.IsCutout(block) ? _textures.Cutout : _textures.Opaque, active);

    public void RemoveObject(WorldObject obj)
    {
        if (!_objects.Remove(obj.Id)) return;
        Consoles.RemoveBlock(obj.Origin);
        foreach (var p in obj.Footprint) _footprints.Remove(p);
        foreach (var p in obj.ModelCells) _solid.Remove(p);
        foreach (var p in obj.FullCells) _fullCells.Remove(p);
        RebuildFacesAgainst(obj.FullCells);
        if (obj.Node.GetChildCount() > 0) _objectLights--;
        obj.Node.QueueFree();
        Revision++;
    }

    /// <summary>Puts the model (cells, hidden grid faces, mesh position) where open fraction <paramref name="open"/> has it.
    /// <paramref name="cells"/> is <see cref="WorldObject.CellsAt"/> for that fraction, which the caller has already
    /// computed (a sliding door needs it for the body check too).</summary>
    void MoveModel(WorldObject obj, float open, HashSet<Vector3I> cells)
    {
        foreach (var p in obj.ModelCells) _solid.Remove(p);
        foreach (var p in cells) _solid[p] = obj;
        if (!cells.SetEquals(obj.ModelCells)) ModelRevision++;
        obj.ModelCells = cells;

        var full = obj.FullCellsAt(open, cells);
        if (!full.SetEquals(obj.FullCells))
        {
            var changed = new HashSet<Vector3I>(full);
            changed.SymmetricExceptWith(obj.FullCells);
            foreach (var p in obj.FullCells) _fullCells.Remove(p);
            foreach (var p in full) _fullCells.Add(p);
            obj.FullCells = full;
            RebuildFacesAgainst(changed);
        }

        var offset = obj.Door is { } door ? DoorMotion.Offset(open, door) : default;
        obj.Node.Position = obj.Orientation.Apply(offset).ToGodot();
    }

    /// <summary>F on a door: reverse it.</summary>
    public void ToggleDoor(WorldObject obj)
    {
        if (obj.Door is null) return;
        obj.DoorState = DoorMotion.Toggle(obj.DoorState);
    }

    /// <summary>
    /// Every door fully open at once (<c>--open-doors</c>), except a door whose open model would land in cells
    /// <paramref name="blocked"/> rejects (the player's body): that one stays as it is, the same rule
    /// <see cref="StepDoors"/> keeps. Returns how many doors were left shut that way.
    /// </summary>
    public int OpenAllDoors(Func<Func<Vector3I, bool>, bool> blocked)
    {
        int refused = 0;
        foreach (var obj in _objects.Values.Where(o => o.Door is not null))
        {
            var cells = obj.CellsAt(1);
            var current = obj.ModelCells;
            if (blocked(p => cells.Contains(p) && !current.Contains(p)))
            {
                refused++;
                continue;
            }
            obj.DoorState = new DoorState(1, true, 0);
            MoveModel(obj, 1, cells);
        }
        return refused;
    }

    /// <summary>
    /// Moves every door one frame. A door whose model would move into cells where <paramref name="blocked"/> says the
    /// player is stays put; if it was closing, it opens again instead and <paramref name="refusedClose"/> is told, so a
    /// door can never shut on the player (ours; original behaviour unknown). <paramref name="blocked"/> gets a
    /// membership test for the grid-local cells the model would newly enter, so no set is built just to ask, and this grid,
    /// so one callback made once serves every grid.
    /// </summary>
    public void StepDoors(float delta, Func<BlockGrid, Func<Vector3I, bool>, bool> blocked, Action<WorldObject> refusedClose)
    {
        foreach (var obj in _objects.Values)
        {
            if (obj.Door is not { } door) continue;
            var state = obj.DoorState;
            var next = AutoCloseDoors || !state.Opening
                ? DoorMotion.Advance(state, door, delta)
                : new DoorState(DoorMotion.Step(state.Open, 1, door, delta), true, 0);
            if (next.Open == state.Open)
            {
                obj.DoorState = next;
                continue;
            }
            // Computed once and handed to MoveModel, which would otherwise build the same set again.
            var cells = obj.CellsAt(next.Open);
            var current = obj.ModelCells;
            if (!cells.IsSubsetOf(current) && blocked(this, p => cells.Contains(p) && !current.Contains(p)))
            {
                if (!next.Opening)
                {
                    obj.DoorState = new DoorState(state.Open, true, 0);
                    refusedClose(obj);
                }
                continue;
            }
            obj.DoorState = next;
            MoveModel(obj, next.Open, cells);
        }
    }

    /// <summary>The objects for a save (original blockIds, so palette changes can't scramble them).</summary>
    public List<PlacedObject> ExportObjects() =>
        [.. _objects.Values.OrderBy(o => o.Id).Select(o => new PlacedObject(_palette.BlockIdOf(o.Block), o.Origin, o.Orientation.Index, o.DoorState.Open))];

    /// <summary>
    /// Restores saved turns onto the grid blocks already imported. A turn on a cell that is empty, outside the grid or
    /// holds a block that can't be turned (the itemset changed) is dropped: the block stays, facing the default way.
    /// Only the chunks around turned cells are remeshed again.
    /// </summary>
    /// <returns>How many saved turns were dropped.</returns>
    public int ImportTurned(IReadOnlyList<TurnedCell> saved)
    {
        int dropped = 0;
        var turned = new List<(Vector3I, Orientation)>();
        foreach (var t in saved)
        {
            var p = t.Cell.ToGodot();
            if (!Orientation.IsValidIndex(t.Orientation) || !InGrid(p) || !_palette.IsTurnable(_cells[Index(p)])) dropped++;
            else turned.Add((p, Orientation.FromIndex(t.Orientation)));
        }
        SetTurns(turned);
        return dropped;
    }

    /// <summary>Turns many grid blocks with one remesh of the chunks around them (a launch, a load).</summary>
    public void SetTurns(IReadOnlyCollection<(Vector3I Cell, Orientation Turn)> turns)
    {
        var changed = new List<Vector3I>();
        foreach (var (p, o) in turns)
            if (InGrid(p) && Turn(p, _cells[Index(p)], o)) changed.Add(p);
        if (changed.Count == 0) return;
        Revision++;
        RebuildAround(changed);
    }

    /// <summary>The turned cells for a save.</summary>
    public List<TurnedCell> ExportTurned() =>
        [.. _turned.OrderBy(t => t.Key.Z).ThenBy(t => t.Key.Y).ThenBy(t => t.Key.X).Select(t => new TurnedCell(t.Key.ToInt3(), t.Value.Index))];

    /// <summary>The console bindings for a save.</summary>
    public IReadOnlyList<SavedConsole> ExportConsoles() => Consoles.ToSnapshot();

    /// <summary>Restores saved console bindings after cells and objects have been imported.</summary>
    public void ImportConsoles(IReadOnlyList<SavedConsole> saved)
    {
        Consoles.LoadSnapshot(saved, cell =>
        {
            var p = cell.ToGodot();
            var block = Get(p);
            if (block == BlockPalette.Air && _footprints.TryGetValue(p, out var obj) && obj.Origin == cell)
                block = obj.Block;
            return block != BlockPalette.Air && _palette.Item(block).Get<ConsoleComponent>() is { } c
                ? (c.Buttons, c.GunsOnly) : null;
        });
    }

    /// <summary>
    /// Places saved objects after <see cref="Import"/>. Objects whose block this palette lacks (or now treats as a
    /// single-cell grid block), that would reach outside this grid, or that overlap what is already there are dropped;
    /// the return value counts them, to be reported like lost cells.
    /// </summary>
    public int ImportObjects(IReadOnlyList<PlacedObject> saved)
    {
        int lost = 0;
        foreach (var placed in saved)
        {
            if (_palette.IndexOfBlockId(placed.BlockId) is not { } block || _palette.Shape(block) is not { } shape ||
                placed.FootprintProblem(shape, GridSize) is not null ||
                AddObject(block, placed.Origin, Orientation.FromIndex(placed.Orientation), placed.Open) is null)
            {
                lost++;
                GD.PushWarning($"Saved object block {placed.BlockId} at {placed.Origin} could not be restored");
            }
        }
        return lost;
    }

    // ---- saves of the grid ----

    /// <summary>
    /// The cells for a save: a palette of ORIGINAL blockIds (entry 0 the air sentinel) and the grid re-indexed into it.
    /// Only blocks that actually occur get an entry, so the save's palette says exactly which blocks the grid needs.
    /// The grid order is the same as <see cref="WorldSnapshot.CellIndex"/>.
    /// </summary>
    public (List<int> Palette, ushort[] Cells) Export()
    {
        var palette = new List<int> { WorldSnapshot.AirBlockId };
        var toSaved = new ushort?[_palette.Count + 1];
        toSaved[BlockPalette.Air] = WorldSnapshot.Air;
        var cells = new ushort[_cells.Length];
        for (int i = 0; i < _cells.Length; i++)
        {
            var block = _cells[i];
            if (toSaved[block] is not { } saved)
            {
                saved = (ushort)palette.Count;
                palette.Add(_palette.BlockIdOf(block));
                toSaved[block] = saved;
            }
            cells[i] = saved;
        }
        return (palette, cells);
    }

    /// <summary>
    /// Replaces every cell with a saved grid (palette of original blockIds), then rebuilds meshes and lights. BlockIds
    /// this palette lacks (removed from the itemset, no longer placeable here, or now an object block) become air, as do
    /// cells outside this grid's bounds when the save is larger. Returns how many solid cells were lost that way.
    /// </summary>
    public int Import(IReadOnlyList<int> savedPalette, ushort[] savedCells, int savedSize)
    {
        var toRuntime = new ushort[savedPalette.Count];
        for (int i = 0; i < savedPalette.Count; i++)
            toRuntime[i] = savedPalette[i] == WorldSnapshot.AirBlockId ? BlockPalette.Air
                : _palette.IndexOfBlockId(savedPalette[i]) is { } index && !_palette.IsObject(index) ? index : BlockPalette.Air;

        Array.Clear(_cells);
        _turned.Clear();
        int lost = 0;
        for (int z = 0; z < savedSize; z++)
        for (int y = 0; y < savedSize; y++)
        for (int x = 0; x < savedSize; x++)
        {
            var saved = savedCells[WorldSnapshot.CellIndex(savedSize, x, y, z)];
            if (saved == WorldSnapshot.Air) continue;
            var block = toRuntime[saved];
            var p = new Vector3I(x, y, z);
            if (block == BlockPalette.Air || !InGrid(p)) lost++;
            else _cells[Index(p)] = block;
        }
        Revision++;
        RebuildAll();
        return lost;
    }

    /// <summary>Remeshes every chunk and recreates the lights of light-emitting blocks, which <see cref="SetRaw"/> skips.</summary>
    public void RebuildAll()
    {
        foreach (var light in _lights.Values) light.QueueFree();
        _lights.Clear();
        _powered.Clear();
        for (int i = 0; i < _cells.Length; i++)
        {
            if (_cells[i] == BlockPalette.Air) continue;
            UpdateLight(CellOf(i), _cells[i]);
            TrackPower(CellOf(i), _cells[i]);
        }

        for (int z = 0; z < _chunks; z++)
        for (int y = 0; y < _chunks; y++)
        for (int x = 0; x < _chunks; x++)
            RebuildChunk(new Vector3I(x, y, z));
    }

    /// <summary>Remeshes the chunks holding these cells or any of their face neighbours.</summary>
    void RebuildAround(IEnumerable<Vector3I> cells)
    {
        var chunks = new HashSet<Vector3I>();
        foreach (var p in cells)
        {
            chunks.Add(ChunkOf(p));
            for (int axis = 0; axis < 3; axis++)
            {
                var step = Vector3I.Zero;
                step[axis] = 1;
                chunks.Add(ChunkOf(p + step));
                chunks.Add(ChunkOf(p - step));
            }
        }
        foreach (var chunk in chunks) RebuildChunk(chunk);
    }

    /// <summary>
    /// After the fully covered object cells changed: remeshes only the chunks of grid blocks next to those cells, the
    /// only faces <see cref="HidesFaces"/> affects. A door sliding through open space changes its full cells twice per
    /// cell of travel and would otherwise remesh several chunks each time for no visible change.
    /// </summary>
    void RebuildFacesAgainst(IEnumerable<Vector3I> cells)
    {
        var chunks = new HashSet<Vector3I>();
        foreach (var p in cells)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                var step = Vector3I.Zero;
                step[axis] = 1;
                if (Get(p + step) != BlockPalette.Air) chunks.Add(ChunkOf(p + step));
                if (Get(p - step) != BlockPalette.Air) chunks.Add(ChunkOf(p - step));
            }
        }
        foreach (var chunk in chunks) RebuildChunk(chunk);
    }

    static Vector3I ChunkOf(Vector3I p) =>
        new(Mathf.FloorToInt(p.X / (float)ChunkSize), Mathf.FloorToInt(p.Y / (float)ChunkSize), Mathf.FloorToInt(p.Z / (float)ChunkSize));

    void RebuildChunk(Vector3I chunk)
    {
        if (chunk.X < 0 || chunk.Y < 0 || chunk.Z < 0 || chunk.X >= _chunks || chunk.Y >= _chunks || chunk.Z >= _chunks) return;
        int slot = (chunk.Z * _chunks + chunk.Y) * _chunks + chunk.X;
        var mesh = ChunkMesher.Build(this, chunk * ChunkSize, ChunkSize, _textures.Opaque, _textures.Cutout);
        if (mesh is null)
        {
            _chunkNodes[slot]?.QueueFree();
            _chunkNodes[slot] = null;
            return;
        }
        if (_chunkNodes[slot] is null)
        {
            _chunkNodes[slot] = new MeshInstance3D { Name = $"Chunk_{chunk.X}_{chunk.Y}_{chunk.Z}" };
            AddChild(_chunkNodes[slot]);
        }
        _chunkNodes[slot]!.Mesh = mesh;
    }

    void UpdateLight(Vector3I p, ushort block)
    {
        if (_lights.Remove(p, out var old)) old.QueueFree();
        if (block == BlockPalette.Air || _lights.Count >= MaxLights) return;
        if (_palette.Item(block).Get<LightComponent>() is not { } light) return;
        var omni = new OmniLight3D
        {
            Position = new Vector3(p.X + 0.5f, p.Y + 0.5f, p.Z + 0.5f),
            LightColor = Color.Color8(light.Light.Color.R, light.Light.Color.G, light.Light.Color.B),
            OmniRange = Math.Max(2f, light.Light.Radius * 1.5f),
            LightEnergy = 2f,
        };
        _lights[p] = omni;
        AddChild(omni);
    }

    int Index(Vector3I p) => (p.Z * GridSize + p.Y) * GridSize + p.X;

    Vector3I CellOf(int i) => new(i % GridSize, i / GridSize % GridSize, i / (GridSize * GridSize));
}
