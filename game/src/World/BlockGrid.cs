using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Grids;
using OpenCorneroids.Simulation.Items;
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

    /// <summary>The grid blocks, with per-chunk counts and the object model cells, so emptiness, block counts and
    /// <see cref="IsSolid"/> never scan or hash.</summary>
    readonly VoxelCells _voxels;
    readonly MeshInstance3D?[] _chunkNodes;
    /// <summary>Chunks whose meshes wait for the end of the frame (<see cref="FlushMeshes"/>).</summary>
    readonly DirtyChunks _dirty;
    readonly List<int> _toBuild = new();
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
    /// <summary>Grid cells holding a gun block (a turned single-cell cannon), kept up to date like <see cref="_powered"/>
    /// so firing never scans the grid for guns.</summary>
    readonly HashSet<Vector3I> _guns = new();
    int _nextObjectId = 1;
    /// <summary>Lights of light-emitting objects, by object id, capped separately from grid lights so neither starves the
    /// other. Children of the grid, not of the object's mesh, so they stay when objects are drawn merged.</summary>
    readonly Dictionary<int, OmniLight3D> _objectLights = new();
    /// <summary>Whether objects other than doors are drawn merged per chunk (<c>cl_batch_objects</c>).</summary>
    bool _batching;
    /// <summary>The merged-drawn objects of each chunk, by the chunk of their origin.</summary>
    readonly List<WorldObject>?[] _batched;
    /// <summary>Each chunk's merged object mesh, beside its block mesh.</summary>
    readonly MeshInstance3D?[] _objectChunkNodes;
    /// <summary>Chunks whose merged object meshes wait for the end of the frame.</summary>
    readonly DirtyChunks _objectsDirty;
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
        _voxels = new VoxelCells(size);
        _chunkNodes = new MeshInstance3D?[_chunks * _chunks * _chunks];
        _dirty = new DirtyChunks(_chunkNodes.Length);
        _batched = new List<WorldObject>?[_chunkNodes.Length];
        _objectChunkNodes = new MeshInstance3D?[_chunkNodes.Length];
        _objectsDirty = new DirtyChunks(_chunkNodes.Length);
        _batching = BatchingWanted;
        // Processed after every other node, so the edits of a frame are meshed in that same frame.
        ProcessPriority = RemeshPriority;
        SolidQuery = (x, y, z) => IsSolid(new Vector3I(x, y, z));
        Systems = new GridSystems(this);
    }

    /// <summary>Power networks and how they look, and for a ship its thrusters. The static world has them too, so a
    /// structure shows the same textures before and after it is launched.</summary>
    public GridSystems Systems { get; }

    /// <summary>Console colour-group bindings, key assignments, and bound cameras on this grid.</summary>
    public ShipConsoleBindings Consoles { get; } = new();

    /// <summary>
    /// What the chests (container blocks) on this grid hold, keyed by cell. Forgotten whenever a cell changes, so a new
    /// block never finds old contents; <see cref="GridSet.Break"/> takes them out first to spill them.
    /// </summary>
    public GridContainers Containers { get; } = new();

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
        CellsReplaced();
    }

    /// <summary>The cells with their bookkeeping, for data a grid kind derives from them (a ship's collision points).</summary>
    protected VoxelCells CellStore => _voxels;

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

    /// <summary>How many 16³ sectors hold any block: what the original counts to cap the parasites on an entity. Kept
    /// up to date by every edit.</summary>
    public int FilledChunks => _voxels.FilledChunks;

    /// <summary>
    /// Finds the closest solid cell (grid block or object model cell) within <paramref name="maxDistance"/> of
    /// <paramref name="localPos"/> (grid-local coordinates). Empty chunks are skipped in one check, and objects whose
    /// cells all lie further away than the best cell so far are skipped by their bounds.
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
            int slot = (cz * _chunks + cy) * _chunks + cx;
            if (_voxels.BlocksInChunk(slot) == 0) continue;
            int z0 = Math.Max(loZ, cz * ChunkSize), z1 = Math.Min(hiZ, (cz + 1) * ChunkSize - 1);
            int y0 = Math.Max(loY, cy * ChunkSize), y1 = Math.Min(hiY, (cy + 1) * ChunkSize - 1);
            int x0 = Math.Max(loX, cx * ChunkSize), x1 = Math.Min(hiX, (cx + 1) * ChunkSize - 1);
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (_voxels[(z * GridSize + y) * GridSize + x] == BlockPalette.Air) continue;
                float d2 = localPos.DistanceSquaredTo(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
                if (d2 <= bestDistSq)
                {
                    bestDistSq = d2;
                    bestCell = new Vector3I(x, y, z);
                }
            }
        }

        foreach (var obj in _objects.Values)
        {
            if (DistanceSquaredToCells(localPos, obj.CellSpan) > bestDistSq) continue;
            foreach (var cell in obj.ModelCells)
            {
                float d2 = localPos.DistanceSquaredTo(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));
                if (d2 <= bestDistSq)
                {
                    bestDistSq = d2;
                    bestCell = cell;
                }
            }
        }

        return bestCell is { } found ? (found, MathF.Sqrt(bestDistSq)) : null;
    }

    /// <summary>Squared distance from <paramref name="p"/> to the box of the cells an object's model can cover: no
    /// cell centre of it is nearer.</summary>
    static float DistanceSquaredToCells(Vector3 p, ModelCellSpan span)
    {
        float dx = Math.Max(0, Math.Max(span.Lo.X - p.X, p.X - (span.Hi.X + 1)));
        float dy = Math.Max(0, Math.Max(span.Lo.Y - p.Y, p.Y - (span.Hi.Y + 1)));
        float dz = Math.Max(0, Math.Max(span.Lo.Z - p.Z, p.Z - (span.Hi.Z + 1)));
        return dx * dx + dy * dy + dz * dz;
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
    public ushort Get(Vector3I p) => InGrid(p) ? _voxels[Index(p)] : BlockPalette.Air;

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
        if (!InGrid(p) || !Turn(p, _voxels[Index(p)], o)) return;
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
    public void CopyCellsTo(ushort[] buffer) => _voxels.CopyTo(buffer);

    /// <summary>A grid block or an object's model is there: collision, ray casts and mining stop here. Two array reads.</summary>
    public bool IsSolid(Vector3I p) => _voxels.IsSolid(p.X, p.Y, p.Z);

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
        foreach (var chunk in chunks) MarkChunk(chunk);

        foreach (var obj in _objects.Values)
        {
            bool active = !objects.Contains(obj) || !_palette.HasInactive(obj.Block);
            if (obj.Active == active) continue;
            obj.Active = active;
            if (obj.Node is { } node) node.Mesh = BuildObjectMesh(obj.Block, obj.Shape, obj.Origin, obj.Orientation, active);
            else _objectsDirty.Mark(SlotOf(obj.Origin));
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
    public bool IsEmpty => _objects.Count == 0 && _voxels.Count == 0;

    /// <summary>Grid blocks (objects not counted), kept up to date by every edit.</summary>
    public int GridBlockCount => _voxels.Count;

    /// <summary>Every grid block that is not air, with its cell, in cell order; empty chunks are skipped.</summary>
    public IEnumerable<(Vector3I Cell, ushort Block)> Blocks()
    {
        foreach (var (cell, block) in _voxels.Blocks()) yield return (cell.ToGodot(), block);
    }

    /// <summary>The smallest and largest cell holding a grid block, or null when there is none; only the outer chunks
    /// are scanned.</summary>
    public (Vector3I Min, Vector3I Max)? BlockBounds() =>
        _voxels.TryBlockBounds(out var min, out var max) ? (min.ToGodot(), max.ToGodot()) : null;

    /// <summary>Grid cells holding a gun block (objects are in <see cref="Objects"/>).</summary>
    public IReadOnlyCollection<Vector3I> GunCells => _guns;

    /// <summary>Grid blocks that take part in power networks, with their cells (objects are in <see cref="Objects"/>).</summary>
    public IEnumerable<(Vector3I Cell, ushort Block)> PoweredBlocks() => _powered.Select(p => (p, Get(p)));

    /// <summary>Keeps the powered-cell and gun-cell sets in step with the block now at <paramref name="p"/>.</summary>
    void TrackPower(Vector3I p, ushort block)
    {
        if (_palette.IsPowered(block)) _powered.Add(p);
        else _powered.Remove(p);
        if (_palette.IsGun(block)) _guns.Add(p);
        else _guns.Remove(p);
    }

    /// <summary>A block went away or was replaced: its console bindings and chest contents go with it.</summary>
    void ForgetBlockState(Int3 cell)
    {
        Consoles.RemoveBlock(cell);
        Containers.Forget(cell);
    }

    /// <summary>The one place a grid block is written: the cell store and what derives from it (a ship's mass).</summary>
    void WriteCell(Vector3I p, ushort block)
    {
        var old = _voxels.SetAt(Index(p), block);
        if (old != block) BlockChanged(p, old, block);
    }

    /// <summary>A grid block changed from <paramref name="old"/> to <paramref name="now"/> (either may be air).</summary>
    protected virtual void BlockChanged(Vector3I p, ushort old, ushort now) { }

    /// <summary>An object was placed (<paramref name="added"/>) or removed.</summary>
    protected virtual void ObjectChanged(WorldObject obj, bool added) { }

    /// <summary>Every grid block may have changed at once (a load, a new palette): derived data starts over.</summary>
    protected virtual void CellsReplaced() { }

    /// <summary>Sets a block without remeshing; call <see cref="RebuildAll"/> afterwards. For world generation.</summary>
    public void SetRaw(Vector3I p, ushort block)
    {
        if (!InGrid(p)) return;
        if (_voxels[Index(p)] != block) ForgetBlockState(p.ToInt3());
        WriteCell(p, block);
        _turned.Remove(p);
        TrackPower(p, block);
        Revision++;
    }

    /// <summary>Sets a grid block and marks the affected chunks for remeshing. Returns false outside the grid, or for an object
    /// block, which belongs in <see cref="AddObject"/>.</summary>
    public bool Set(Vector3I p, ushort block) => Set(p, block, Orientation.Default);

    /// <summary>As <see cref="Set(Vector3I, ushort)"/>, turned by <paramref name="o"/> if the block is turnable.</summary>
    public bool Set(Vector3I p, ushort block, Orientation o)
    {
        if (!InGrid(p) || (block != BlockPalette.Air && _palette.IsObject(block))) return false;
        if (_voxels[Index(p)] != block) ForgetBlockState(p.ToInt3());
        WriteCell(p, block);
        Turn(p, block, o);
        Revision++;
        UpdateLight(p, block);
        TrackPower(p, block);
        var chunk = new Vector3I(p.X / ChunkSize, p.Y / ChunkSize, p.Z / ChunkSize);
        MarkChunk(chunk);
        // A block on a chunk border also changes which faces the neighbouring chunk shows.
        for (int axis = 0; axis < 3; axis++)
        {
            int local = p[axis] % ChunkSize;
            var step = Vector3I.Zero;
            step[axis] = 1;
            if (local == 0) MarkChunk(chunk - step);
            if (local == ChunkSize - 1) MarkChunk(chunk + step);
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
            if (!InGrid(p) || (block != BlockPalette.Air && _palette.IsObject(block)) || _voxels[Index(p)] == block) continue;
            ForgetBlockState(p.ToInt3());
            WriteCell(p, block);
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
        var door = item.Get<DoorComponent>();
        int id = _nextObjectId++;
        // Doors move, so they keep a node of their own; everything else is drawn merged with its chunk when batching.
        var node = door is null && _batching ? null : ObjectNode(id, block, shape, origin, o, active: true);
        var obj = new WorldObject(id, block, shape, origin, o, door, Simulation.Grids.FaceVisibility.ObjectHidesFaces(cutout, _palette.IsSeeThrough(block)), node)
        {
            DoorState = door is null ? DoorState.Closed : DoorState.Resting(open),
        };
        if (node is not null) AddChild(node);
        else Batch(obj);
        if (item.Get<LightComponent>() is { } light && _objectLights.Count < MaxLights)
        {
            var omni = PointLights.Make(light.Light.Color, light.Light.Radius);
            omni.Name = $"ObjectLight_{id}";
            _objectLights[id] = omni;
            AddChild(omni);
        }
        _objects[obj.Id] = obj;
        foreach (var p in obj.Footprint) _footprints[p] = obj;
        MoveModel(obj, obj.DoorState.Open, obj.CellsAt(obj.DoorState.Open), obj.SpanAt(obj.DoorState.Open));
        ObjectChanged(obj, added: true);
        Revision++;
        return obj;
    }

    ArrayMesh BuildObjectMesh(ushort block, BlockShape shape, Int3 origin, Orientation o, bool active) =>
        ObjectMesher.Build(shape, origin, o, _mapping, _textures, _palette.IsCutout(block) ? _textures.Cutout : _textures.Opaque, active);

    /// <summary>An object's own mesh node, drawn only within the view distance; the caller adds it to the grid.</summary>
    MeshInstance3D ObjectNode(int id, ushort block, BlockShape shape, Int3 origin, Orientation o, bool active) =>
        Settings.Visuals.Chunk(new MeshInstance3D
        {
            Name = $"Object_{id}",
            Mesh = BuildObjectMesh(block, shape, origin, o, active),
        });

    public void RemoveObject(WorldObject obj)
    {
        if (!_objects.Remove(obj.Id)) return;
        ForgetBlockState(obj.Origin);
        foreach (var p in obj.Footprint) _footprints.Remove(p);
        foreach (var p in obj.ModelCells) RemoveSolid(p);
        foreach (var p in obj.FullCells) _fullCells.Remove(p);
        RebuildFacesAgainst(obj.FullCells);
        if (_objectLights.Remove(obj.Id, out var light)) light.QueueFree();
        if (obj.Node is { } node) node.QueueFree();
        else Unbatch(obj);
        ObjectChanged(obj, added: false);
        Revision++;
    }

    void RemoveSolid(Vector3I p)
    {
        _solid.Remove(p);
        _voxels.SetObjectCell(p.X, p.Y, p.Z, false);
    }

    /// <summary>Puts the model (cells, hidden grid faces, mesh position) where open fraction <paramref name="open"/> has it.
    /// <paramref name="cells"/> and <paramref name="span"/> are <see cref="WorldObject.CellsAt"/> and
    /// <see cref="WorldObject.SpanAt"/> for that fraction, which the caller has already computed (a sliding door needs
    /// them for the body check too).</summary>
    void MoveModel(WorldObject obj, float open, HashSet<Vector3I> cells, ModelCellSpan span)
    {
        foreach (var p in obj.ModelCells) RemoveSolid(p);
        foreach (var p in cells)
        {
            _solid[p] = obj;
            _voxels.SetObjectCell(p.X, p.Y, p.Z, true);
        }
        if (!cells.SetEquals(obj.ModelCells)) ModelRevision++;
        obj.ModelCells = cells;
        obj.CellSpan = span;

        var full = obj.FullCellsAt(cells, span);
        if (!full.SetEquals(obj.FullCells))
        {
            var changed = new HashSet<Vector3I>(full);
            changed.SymmetricExceptWith(obj.FullCells);
            foreach (var p in obj.FullCells) _fullCells.Remove(p);
            foreach (var p in full) _fullCells.Add(p);
            obj.FullCells = full;
            RebuildFacesAgainst(changed);
        }

        PlaceNode(obj, open);
    }

    /// <summary>Moves the model's mesh and its light to where open fraction <paramref name="open"/> has them. A merged
    /// object never moves (only doors do, and they keep their own node).</summary>
    void PlaceNode(WorldObject obj, float open)
    {
        var offset = obj.Door is { } door ? obj.Orientation.Apply(DoorMotion.Offset(open, door)).ToGodot() : Vector3.Zero;
        if (obj.Node is { } node) node.Position = offset;
        if (_objectLights.TryGetValue(obj.Id, out var light))
            light.Position = obj.Shape.ModelBox(obj.Origin, obj.Orientation).Center.ToGodot() + offset;
    }

    // ---- merged object meshes (cl_batch_objects) ----

    /// <summary>The <c>cl_batch_objects</c> setting.</summary>
    static bool BatchingWanted => Settings.GameSettings.Cvars.Bool(Simulation.Settings.Cvars.BatchObjects);

    /// <summary>Each object model's arrays, built once per block, orientation and power state and shared by every grid.</summary>
    static readonly ObjectPartCache Parts = new();

    /// <summary>Reused for every merged object mesh: opaque and see-through models go into separate surfaces.</summary>
    static readonly MeshBatch OpaqueBatch = new(), CutoutBatch = new();

    /// <summary>The chunk slot of the cell <paramref name="p"/> (inside the grid): where an object with that origin is drawn.</summary>
    int SlotOf(Int3 p) => (p.Z / ChunkSize * _chunks + p.Y / ChunkSize) * _chunks + p.X / ChunkSize;

    /// <summary>Draws the object as part of its origin chunk's merged mesh from the next flush.</summary>
    void Batch(WorldObject obj)
    {
        int slot = SlotOf(obj.Origin);
        (_batched[slot] ??= new()).Add(obj);
        _objectsDirty.Mark(slot);
    }

    /// <summary>Takes the object out of its chunk's merged mesh from the next flush.</summary>
    void Unbatch(WorldObject obj)
    {
        int slot = SlotOf(obj.Origin);
        if (_batched[slot]?.Remove(obj) == true) _objectsDirty.Mark(slot);
    }

    /// <summary>
    /// Switches every live grid between merged object meshes and one node per object, when <c>cl_batch_objects</c>
    /// changes. A grid outside the tree catches up when it enters it.
    /// </summary>
    public static void BatchingChanged()
    {
        foreach (var grid in Live) grid.SetBatching(BatchingWanted);
    }

    /// <summary>Moves every object but the doors into the merged meshes, or out of them into nodes of their own.</summary>
    void SetBatching(bool on)
    {
        if (_batching == on) return;
        _batching = on;
        foreach (var obj in _objects.Values)
        {
            if (obj.Door is not null) continue;
            if (on && obj.Node is { } node)
            {
                node.QueueFree();
                obj.Node = null;
                Batch(obj);
            }
            else if (!on && obj.Node is null)
            {
                Unbatch(obj);
                obj.Node = ObjectNode(obj.Id, obj.Block, obj.Shape, obj.Origin, obj.Orientation, obj.Active);
                AddChild(obj.Node);
                PlaceNode(obj, obj.DoorState.Open);
            }
        }
    }

    /// <summary>Builds the merged object meshes of the chunks that changed since the last flush: every waiting one, as
    /// they are only copies of cached arrays.</summary>
    void FlushObjects()
    {
        if (_objectsDirty.Count == 0) return;
        _objectsDirty.Take(int.MaxValue, _ => 0, _toBuild);
        foreach (int slot in _toBuild) RebuildObjectSlot(slot);
    }

    /// <summary>One chunk's merged object mesh: each object's cached part moved to its origin, opaque models in the first
    /// surface and see-through ones in the second, as in the block meshes.</summary>
    void RebuildObjectSlot(int slot)
    {
        using var timing = PerfMonitor.Measure(PerfMonitor.Remesh);
        OpaqueBatch.Clear();
        CutoutBatch.Clear();
        if (_batched[slot] is { } objects)
            foreach (var obj in objects)
            {
                var part = Parts.Get(_palette, _textures, _mapping, obj.Block, obj.Shape, obj.Orientation, obj.Active);
                (_palette.IsCutout(obj.Block) ? CutoutBatch : OpaqueBatch).Add(part, obj.Origin.ToVector3());
            }
        if (OpaqueBatch.IsEmpty && CutoutBatch.IsEmpty)
        {
            _objectChunkNodes[slot]?.QueueFree();
            _objectChunkNodes[slot] = null;
            return;
        }
        var mesh = new ArrayMesh();
        if (!OpaqueBatch.IsEmpty)
            ObjectMesher.AddSurface(mesh, OpaqueBatch.Positions, OpaqueBatch.Normals, OpaqueBatch.Uvs, OpaqueBatch.Indices, _textures.Opaque);
        if (!CutoutBatch.IsEmpty)
            ObjectMesher.AddSurface(mesh, CutoutBatch.Positions, CutoutBatch.Normals, CutoutBatch.Uvs, CutoutBatch.Indices, _textures.Cutout);
        if (_objectChunkNodes[slot] is null)
        {
            var chunk = ChunkAt(slot);
            _objectChunkNodes[slot] = Settings.Visuals.Chunk(new MeshInstance3D { Name = $"Objects_{chunk.X}_{chunk.Y}_{chunk.Z}" });
            AddChild(_objectChunkNodes[slot]);
        }
        _objectChunkNodes[slot]!.Mesh = mesh;
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
            MoveModel(obj, 1, cells, obj.SpanAt(1));
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
            // While the door's cell span stays the same its cells do too: only the mesh moves.
            var span = obj.SpanAt(next.Open);
            if (span == obj.CellSpan)
            {
                obj.DoorState = next;
                PlaceNode(obj, next.Open);
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
            MoveModel(obj, next.Open, cells, span);
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
            if (!Orientation.IsValidIndex(t.Orientation) || !InGrid(p) || !_palette.IsTurnable(_voxels[Index(p)])) dropped++;
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
            if (InGrid(p) && Turn(p, _voxels[Index(p)], o)) changed.Add(p);
        if (changed.Count == 0) return;
        Revision++;
        RebuildAround(changed);
    }

    /// <summary>The turned cells for a save.</summary>
    public List<TurnedCell> ExportTurned() =>
        [.. _turned.OrderBy(t => t.Key.Z).ThenBy(t => t.Key.Y).ThenBy(t => t.Key.X).Select(t => new TurnedCell(t.Key.ToInt3(), t.Value.Index))];

    /// <summary>The console bindings for a save.</summary>
    public IReadOnlyList<SavedConsole> ExportConsoles() => Consoles.ToSnapshot();

    /// <summary>
    /// Restores saved console bindings after cells and objects have been imported. Bindings that no longer fit the
    /// blocks there (see <see cref="ShipConsoleBindings.LoadSnapshot"/>) are dropped.
    /// </summary>
    /// <returns>How many bindings were dropped.</returns>
    public int ImportConsoles(IReadOnlyList<SavedConsole> saved) =>
        Consoles.LoadSnapshot(saved,
            cell => BlockWithOriginAt(cell) is { } block && _palette.Item(block).Get<ConsoleComponent>() is { } c
                ? (c.Buttons, c.GunsOnly) : null,
            cell => BlockWithOriginAt(cell) is { } block ? TargetKind(_palette.Item(block)) : ConsoleTargetKind.None);

    /// <summary>The chests holding something, for a save.</summary>
    public IReadOnlyList<SavedContainer> ExportContainers() => Containers.ToSnapshot();

    /// <summary>
    /// Restores saved chests after cells and objects have been imported. A chest whose cell no longer holds a container
    /// block, or whose block became smaller, gives back what no longer fits (see <see cref="GridContainers.LoadSnapshot"/>).
    /// </summary>
    public ContainerLoad ImportContainers(IReadOnlyList<SavedContainer> saved) =>
        Containers.LoadSnapshot(saved, cell => ContainerAt(cell) is { } c ? ContainerRules.SlotCount(c) : null, _palette.MaxStackOf);

    /// <summary>The container block at a cell (the origin of a multi-cell one), or null when there is none.</summary>
    public ContainerComponent? ContainerAt(Int3 cell) =>
        BlockWithOriginAt(cell) is { } block ? _palette.Item(block).Get<ContainerComponent>() : null;

    /// <summary>The chest at <paramref name="cell"/>, made empty the first time it is opened.</summary>
    public Inventory OpenContainer(Int3 cell, ContainerComponent container) =>
        Containers.GetOrCreate(cell, ContainerRules.SlotCount(container), _palette.MaxStackOf);

    /// <summary>The grid block at a cell, or the object whose origin it is; null for air and out-of-grid cells.</summary>
    ushort? BlockWithOriginAt(Int3 cell)
    {
        var p = cell.ToGodot();
        if (!InGrid(p)) return null;
        var block = Get(p);
        if (block == BlockPalette.Air && _footprints.TryGetValue(p, out var obj) && obj.Origin == cell)
            block = obj.Block;
        return block == BlockPalette.Air ? null : block;
    }

    /// <summary>What an item can be bound to a console as, by the same rules as the bind tool.</summary>
    static ConsoleTargetKind TargetKind(ItemDef item) =>
        (item.Get<ThrusterComponent>() is not null ? ConsoleTargetKind.Thruster : ConsoleTargetKind.None) |
        (item.Get<GunComponent>() is not null ? ConsoleTargetKind.Gun : ConsoleTargetKind.None) |
        (item.Kind == "camera" ? ConsoleTargetKind.Camera : ConsoleTargetKind.None);

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
        var cells = new ushort[_voxels.Cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            var block = _voxels[i];
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
    /// Replaces every cell with a saved grid (palette of original blockIds), rebuilds the lights and marks every chunk
    /// for remeshing. The caller imports the objects, turns and consoles next and then calls <see cref="FlushMeshes"/>,
    /// so each chunk is built once, with its turned blocks and the faces hidden by objects already right. BlockIds
    /// this palette lacks (removed from the itemset, no longer placeable here, or now an object block) become air, as do
    /// cells outside this grid's bounds when the save is larger. Returns how many solid cells were lost that way.
    /// </summary>
    public int Import(IReadOnlyList<int> savedPalette, ushort[] savedCells, int savedSize)
    {
        var toRuntime = new ushort[savedPalette.Count];
        for (int i = 0; i < savedPalette.Count; i++)
            toRuntime[i] = savedPalette[i] == WorldSnapshot.AirBlockId ? BlockPalette.Air
                : _palette.IndexOfBlockId(savedPalette[i]) is { } index && !_palette.IsObject(index) ? index : BlockPalette.Air;

        _voxels.ClearBlocks();
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
            else _voxels.SetAt(Index(p), block);
        }
        Revision++;
        CellsReplaced();
        RebuildLights();
        MarkAllChunks();
        return lost;
    }

    /// <summary>Remeshes every chunk at once (a load or a new world, which must not wait for the per-frame budget) and
    /// recreates the lights of light-emitting blocks, which <see cref="SetRaw"/> skips.</summary>
    public void RebuildAll()
    {
        RebuildLights();
        MarkAllChunks();
        FlushMeshes();
    }

    /// <summary>Recreates the lights of light-emitting blocks and the powered-cell set from the cells.</summary>
    void RebuildLights()
    {
        foreach (var light in _lights.Values) light.QueueFree();
        _lights.Clear();
        _powered.Clear();
        _guns.Clear();
        foreach (var (cell, block) in Blocks())
        {
            UpdateLight(cell, block);
            TrackPower(cell, block);
        }
    }

    /// <summary>Marks every chunk for remeshing, in slot order.</summary>
    void MarkAllChunks()
    {
        for (int slot = 0; slot < _chunkNodes.Length; slot++) _dirty.Mark(slot);
    }

    /// <summary>Marks for remeshing the chunks holding these cells or any of their face neighbours.</summary>
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
        foreach (var chunk in chunks) MarkChunk(chunk);
    }

    /// <summary>
    /// After the fully covered object cells changed: marks for remeshing only the chunks of grid blocks next to those cells, the
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
        foreach (var chunk in chunks) MarkChunk(chunk);
    }

    static Vector3I ChunkOf(Vector3I p) =>
        new(Mathf.FloorToInt(p.X / (float)ChunkSize), Mathf.FloorToInt(p.Y / (float)ChunkSize), Mathf.FloorToInt(p.Z / (float)ChunkSize));

    // ---- remeshing ----

    /// <summary>After every other node's <c>_Process</c> (Godot runs lower priorities first).</summary>
    const int RemeshPriority = 1000;

    /// <summary>Decides once per frame which waiting chunks of all grids are built (<c>cl_remesh_per_frame</c>).</summary>
    static readonly RemeshScheduler Scheduler = new();

    /// <summary>Every grid in the scene tree, and its queue at the same index, for <see cref="FlushAllMeshes"/>.</summary>
    static readonly List<BlockGrid> Live = new();
    static readonly List<DirtyChunks> LiveQueues = new();
    static readonly List<(int Queue, int Slot)> ToBuild = new();

    /// <summary>The camera in each live grid's local space, filled only when the per-frame limit has to choose.</summary>
    static Vector3[] _eyes = [];

    /// <summary>Squared distance from the camera to a chunk centre. Grids are never scaled, so this is the world
    /// distance and chunks of different grids compare fairly.</summary>
    static readonly Func<int, int, float> DistanceToEye = (queue, slot) => Live[queue].ChunkCenter(slot).DistanceSquaredTo(_eyes[queue]);

    /// <summary>Chunks waiting for their mesh.</summary>
    public int PendingChunks => _dirty.Count;

    public override void _EnterTree()
    {
        Live.Add(this);
        LiveQueues.Add(_dirty);
        SetBatching(BatchingWanted);
    }

    public override void _ExitTree()
    {
        int index = Live.IndexOf(this);
        if (index < 0) return;
        Live.RemoveAt(index);
        LiveQueues.RemoveAt(index);
    }

    /// <summary>The first grid processed in a frame flushes every grid; the others find nothing left to do.</summary>
    public override void _Process(double delta)
    {
        if (Scheduler.StartFrame((long)Engine.GetProcessFrames())) FlushAllMeshes(GetViewport()?.GetCamera3D());
    }

    /// <summary>
    /// Builds the meshes of the chunks that edits marked since the last flush, in every grid in the tree: each chunk once
    /// however many edits touched it, as the original rebuilds a changed sector once, in the next frame. With
    /// <c>cl_remesh_per_frame</c> above 0 only that many are built per frame, the nearest to the camera first whichever
    /// grid they are in, and the rest wait (the original builds one per frame); at 0 (ours, the default) every waiting
    /// chunk is built now. Runs after every other node each frame, so a frame's edits show in that frame, including
    /// those of a grid added to the tree during the frame.
    /// </summary>
    static void FlushAllMeshes(Camera3D? camera)
    {
        foreach (var grid in Live) grid.FlushObjects();
        int waiting = 0;
        foreach (var queue in LiveQueues) waiting += queue.Count;
        if (waiting == 0) return;
        int perFrame = Settings.GameSettings.Cvars.Int(Simulation.Settings.Cvars.RemeshPerFrame);
        if (perFrame > 0 && perFrame < waiting)
        {
            if (_eyes.Length < Live.Count) _eyes = new Vector3[Math.Max(Live.Count, _eyes.Length * 2)];
            for (int i = 0; i < Live.Count; i++)
                _eyes[i] = camera is null ? Vector3.One * (Live[i].GridSize * 0.5f) : Live[i].ToLocal(camera.GlobalPosition);
        }
        Scheduler.Take(LiveQueues, perFrame, DistanceToEye, ToBuild);
        foreach (var (queue, slot) in ToBuild) Live[queue].RebuildSlot(slot);
    }

    /// <summary>
    /// Builds every waiting chunk of this grid now, ignoring <c>cl_remesh_per_frame</c>: for a load, a new world or an
    /// imported structure, which must not appear piece by piece.
    /// </summary>
    public void FlushMeshes()
    {
        _dirty.Take(int.MaxValue, _ => 0, _toBuild);
        foreach (int slot in _toBuild) RebuildSlot(slot);
        FlushObjects();
    }

    /// <summary>Queues a chunk's mesh for <see cref="FlushMeshes"/>; chunks outside the grid are ignored.</summary>
    void MarkChunk(Vector3I chunk)
    {
        if (chunk.X < 0 || chunk.Y < 0 || chunk.Z < 0 || chunk.X >= _chunks || chunk.Y >= _chunks || chunk.Z >= _chunks) return;
        _dirty.Mark((chunk.Z * _chunks + chunk.Y) * _chunks + chunk.X);
    }

    Vector3I ChunkAt(int slot) => new(slot % _chunks, slot / _chunks % _chunks, slot / (_chunks * _chunks));

    Vector3 ChunkCenter(int slot) => ((Vector3)ChunkAt(slot) + new Vector3(0.5f, 0.5f, 0.5f)) * ChunkSize;

    void RebuildSlot(int slot)
    {
        var chunk = ChunkAt(slot);
        using var timing = PerfMonitor.Measure(PerfMonitor.Remesh);
        var mesh = ChunkMesher.Build(this, chunk * ChunkSize, ChunkSize, _textures.Opaque, _textures.Cutout);
        if (mesh is null)
        {
            _chunkNodes[slot]?.QueueFree();
            _chunkNodes[slot] = null;
            return;
        }
        if (_chunkNodes[slot] is null)
        {
            _chunkNodes[slot] = Settings.Visuals.Chunk(new MeshInstance3D { Name = $"Chunk_{chunk.X}_{chunk.Y}_{chunk.Z}" });
            AddChild(_chunkNodes[slot]);
        }
        _chunkNodes[slot]!.Mesh = mesh;
    }

    void UpdateLight(Vector3I p, ushort block)
    {
        if (_lights.Remove(p, out var old)) old.QueueFree();
        if (block == BlockPalette.Air || _lights.Count >= MaxLights) return;
        if (_palette.Item(block).Get<LightComponent>() is not { } light) return;
        var omni = PointLights.Make(light.Light.Color, light.Light.Radius);
        omni.Position = new Vector3(p.X + 0.5f, p.Y + 0.5f, p.Z + 0.5f);
        _lights[p] = omni;
        AddChild(omni);
    }

    int Index(Vector3I p) => (p.Z * GridSize + p.Y) * GridSize + p.X;
}
