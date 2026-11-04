using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.World;

/// <summary>What the player aims at: a grid block, or a whole world object.</summary>
/// <param name="Cell">The grid cell; for an object its origin, so aiming at different cells of one object is one target
/// (drilling progress survives moving the aim across a door).</param>
/// <param name="Object">The object, or null for a grid block.</param>
public readonly record struct BlockTarget(Vector3I Cell, WorldObject? Object);

/// <summary>
/// A bounded block grid split into chunks, each meshed into one MeshInstance3D, plus the world objects (multi-cell
/// blocks) that live outside the grid. Block (x,y,z) occupies the unit cube from (x,y,z) to (x+1,y+1,z+1).
/// Point-light blocks get a real light.
/// </summary>
/// <remarks>
/// Two occupancy maps sit next to the grid: footprint cell → object (what placing must not overlap) and model cell →
/// object (what is solid). <see cref="IsSolid"/> is the single answer to "can I pass through / does the ray stop
/// here", shared by collision, the ray cast and mining; footprint space that is not model (thruster exhaust, door
/// travel) is passable but reserved.
/// </remarks>
public partial class VoxelWorld : Node3D
{
    public const int Size = 128;
    public const int ChunkSize = 16;
    const int Chunks = Size / ChunkSize;
    const int MaxLights = 64;

    readonly ushort[] _cells = new ushort[Size * Size * Size];
    readonly MeshInstance3D?[] _chunkNodes = new MeshInstance3D?[Chunks * Chunks * Chunks];
    readonly Dictionary<Vector3I, OmniLight3D> _lights = new();
    readonly Dictionary<int, WorldObject> _objects = new();
    readonly Dictionary<Vector3I, WorldObject> _footprints = new();
    readonly Dictionary<Vector3I, WorldObject> _solid = new();
    readonly HashSet<Vector3I> _fullCells = new();
    int _nextObjectId = 1;
    /// <summary>Lights of light-emitting objects, capped separately from grid lights so neither starves the other.</summary>
    int _objectLights;
    BlockPalette _palette = null!;
    BlockTextures _textures = null!;
    TextureMapping _mapping;

    public BlockPalette Palette => _palette;

    /// <summary>
    /// When false, open doors stay open instead of closing after their time (<c>--open-doors</c>, so a screenshot taken
    /// seconds later still shows them open).
    /// </summary>
    public bool AutoCloseDoors { get; set; } = true;

    public IReadOnlyCollection<WorldObject> Objects => _objects.Values;

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

    public static bool InBounds(Vector3I p) =>
        p.X >= 0 && p.Y >= 0 && p.Z >= 0 && p.X < Size && p.Y < Size && p.Z < Size;

    /// <summary>The grid block at <paramref name="p"/>; objects are not in the grid, see <see cref="IsSolid"/>.</summary>
    public ushort Get(Vector3I p) => InBounds(p) ? _cells[Index(p)] : BlockPalette.Air;

    /// <summary>A grid block or an object's model is there: collision, ray casts and mining stop here.</summary>
    public bool IsSolid(Vector3I p) => Get(p) != BlockPalette.Air || _solid.ContainsKey(p);

    /// <summary>Nothing may be placed here: outside the world, a grid block, or any object's footprint.</summary>
    public bool IsReserved(Vector3I p) => ReservedProblem(p) is not null;

    /// <summary>Why nothing may be placed at <paramref name="p"/> (see <see cref="IsReserved"/>), or null if something may.</summary>
    public string? ReservedProblem(Vector3I p) =>
        !InBounds(p) ? "No room: that would reach outside the world"
        : Get(p) != BlockPalette.Air ? "No room: blocks are in the way"
        : _footprints.TryGetValue(p, out var obj) ? $"No room: that space belongs to {_palette.Item(obj.Block).Name}"
        : null;

    /// <summary>For the chunk mesher: an opaque object model fills this cell, so grid faces against it are hidden.</summary>
    public bool HidesFaces(Vector3I p) => _fullCells.Contains(p);

    /// <summary>The object whose model covers <paramref name="p"/>, if any.</summary>
    public WorldObject? ObjectAt(Vector3I p) => _solid.GetValueOrDefault(p);

    /// <summary>What a ray stopping at the solid cell <paramref name="hit"/> targets.</summary>
    public BlockTarget TargetAt(Vector3I hit) =>
        ObjectAt(hit) is { } obj ? new BlockTarget(obj.Origin.ToGodot(), obj) : new BlockTarget(hit, null);

    /// <summary>The palette index of a target (air if it is gone).</summary>
    public ushort BlockOf(BlockTarget target) =>
        target.Object is { } obj ? (_objects.ContainsKey(obj.Id) ? obj.Block : BlockPalette.Air) : Get(target.Cell);

    /// <summary>Removes the target, grid block or whole object.</summary>
    public void Break(BlockTarget target)
    {
        if (target.Object is { } obj) RemoveObject(obj);
        else Set(target.Cell, BlockPalette.Air);
    }

    /// <summary>Sets a block without remeshing; call <see cref="RebuildAll"/> afterwards. For world generation.</summary>
    public void SetRaw(Vector3I p, ushort block)
    {
        if (InBounds(p)) _cells[Index(p)] = block;
    }

    /// <summary>Sets a grid block and remeshes the affected chunks. Returns false outside the world, or for an object
    /// block, which belongs in <see cref="AddObject"/>.</summary>
    public bool Set(Vector3I p, ushort block)
    {
        if (!InBounds(p) || (block != BlockPalette.Air && _palette.IsObject(block))) return false;
        _cells[Index(p)] = block;
        UpdateLight(p, block);
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

    // ---- objects ----

    /// <summary>Why the object block <paramref name="block"/> can't go at <paramref name="origin"/>, or null if it can.
    /// The player's body is the caller's check.</summary>
    public string? ObjectProblem(ushort block, Int3 origin, Orientation o)
    {
        if (_palette.Shape(block) is not { } shape) return $"{_palette.Item(block).Name} is not a multi-block";
        foreach (var cell in shape.Footprint(origin, o))
        {
            var p = cell.ToGodot();
            if (!InBounds(p)) return "No room: that would reach outside the world";
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
            if (cells.Any(p => !InBounds(p) || _footprints.ContainsKey(p))) return null;
            // Cleared without per-cell remeshing (Set rebuilds up to four chunks per cell; a bay door has hundreds of
            // cells), then the touched chunks once.
            var cleared = cells.Where(p => Get(p) != BlockPalette.Air).ToList();
            foreach (var p in cleared)
            {
                _cells[Index(p)] = BlockPalette.Air;
                UpdateLight(p, BlockPalette.Air);
            }
            RebuildAround(cleared);
        }
        else if (ObjectProblem(block, origin, o) is not null) return null;

        var item = _palette.Item(block);
        bool cutout = _palette.IsCutout(block);
        var node = new MeshInstance3D
        {
            Name = $"Object_{_nextObjectId}",
            Mesh = ObjectMesher.Build(shape, origin, o, _mapping, _textures, cutout ? _textures.Cutout : _textures.Opaque),
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
        return obj;
    }

    public void RemoveObject(WorldObject obj)
    {
        if (!_objects.Remove(obj.Id)) return;
        foreach (var p in obj.Footprint) _footprints.Remove(p);
        foreach (var p in obj.ModelCells) _solid.Remove(p);
        foreach (var p in obj.FullCells) _fullCells.Remove(p);
        RebuildFacesAgainst(obj.FullCells);
        if (obj.Node.GetChildCount() > 0) _objectLights--;
        obj.Node.QueueFree();
    }

    /// <summary>Puts the model (cells, hidden grid faces, mesh position) where open fraction <paramref name="open"/> has it.
    /// <paramref name="cells"/> is <see cref="WorldObject.CellsAt"/> for that fraction, which the caller has already
    /// computed (a sliding door needs it for the body check too).</summary>
    void MoveModel(WorldObject obj, float open, HashSet<Vector3I> cells)
    {
        foreach (var p in obj.ModelCells) _solid.Remove(p);
        foreach (var p in cells) _solid[p] = obj;
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
    /// membership test for the cells the model would newly enter, so no set is built just to ask.
    /// </summary>
    public void StepDoors(float delta, Func<Func<Vector3I, bool>, bool> blocked, Action<WorldObject> refusedClose)
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
            if (!cells.IsSubsetOf(current) && blocked(p => cells.Contains(p) && !current.Contains(p)))
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
    /// Places saved objects after <see cref="Import"/>. Objects whose block this palette lacks (or now treats as a
    /// single-cell grid block), that would reach outside this world, or that overlap what is already there are dropped;
    /// the return value counts them, to be reported like lost cells.
    /// </summary>
    public int ImportObjects(IReadOnlyList<PlacedObject> saved)
    {
        int lost = 0;
        foreach (var placed in saved)
        {
            if (_palette.IndexOfBlockId(placed.BlockId) is not { } block || _palette.Shape(block) is not { } shape ||
                placed.FootprintProblem(shape, Size) is not null ||
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
    /// Only blocks that actually occur get an entry, so the save's palette says exactly which blocks the world needs.
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
    /// cells outside this world's bounds when the save is larger. Returns how many solid cells were lost that way.
    /// </summary>
    public int Import(IReadOnlyList<int> savedPalette, ushort[] savedCells, int savedSize)
    {
        var toRuntime = new ushort[savedPalette.Count];
        for (int i = 0; i < savedPalette.Count; i++)
            toRuntime[i] = savedPalette[i] == WorldSnapshot.AirBlockId ? BlockPalette.Air
                : _palette.IndexOfBlockId(savedPalette[i]) is { } index && !_palette.IsObject(index) ? index : BlockPalette.Air;

        Array.Clear(_cells);
        int lost = 0;
        for (int z = 0; z < savedSize; z++)
        for (int y = 0; y < savedSize; y++)
        for (int x = 0; x < savedSize; x++)
        {
            var saved = savedCells[WorldSnapshot.CellIndex(savedSize, x, y, z)];
            if (saved == WorldSnapshot.Air) continue;
            var block = toRuntime[saved];
            var p = new Vector3I(x, y, z);
            if (block == BlockPalette.Air || !InBounds(p)) lost++;
            else _cells[Index(p)] = block;
        }
        RebuildAll();
        return lost;
    }

    /// <summary>Remeshes every chunk and recreates the lights of light-emitting blocks, which <see cref="SetRaw"/> skips.</summary>
    public void RebuildAll()
    {
        foreach (var light in _lights.Values) light.QueueFree();
        _lights.Clear();
        for (int i = 0; i < _cells.Length; i++)
        {
            if (_cells[i] == BlockPalette.Air) continue;
            int x = i % Size, y = i / Size % Size, z = i / (Size * Size);
            UpdateLight(new Vector3I(x, y, z), _cells[i]);
        }

        for (int z = 0; z < Chunks; z++)
        for (int y = 0; y < Chunks; y++)
        for (int x = 0; x < Chunks; x++)
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
        if (chunk.X < 0 || chunk.Y < 0 || chunk.Z < 0 || chunk.X >= Chunks || chunk.Y >= Chunks || chunk.Z >= Chunks) return;
        int slot = (chunk.Z * Chunks + chunk.Y) * Chunks + chunk.X;
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

    static int Index(Vector3I p) => (p.Z * Size + p.Y) * Size + p.X;
}
