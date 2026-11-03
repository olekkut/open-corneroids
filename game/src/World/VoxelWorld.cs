using Godot;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Game.World;

/// <summary>
/// A bounded block grid split into chunks, each meshed into one MeshInstance3D. Block (x,y,z) occupies the unit
/// cube from (x,y,z) to (x+1,y+1,z+1). Point-light blocks get a real light.
/// </summary>
public partial class VoxelWorld : Node3D
{
    public const int Size = 128;
    public const int ChunkSize = 16;
    const int Chunks = Size / ChunkSize;
    const int MaxLights = 64;

    readonly ushort[] _cells = new ushort[Size * Size * Size];
    readonly MeshInstance3D?[] _chunkNodes = new MeshInstance3D?[Chunks * Chunks * Chunks];
    readonly Dictionary<Vector3I, OmniLight3D> _lights = new();
    BlockPalette _palette = null!;
    Material _opaque = null!;
    Material _cutout = null!;

    public BlockPalette Palette => _palette;

    public void Initialize(BlockPalette palette, Material opaque, Material cutout)
    {
        _palette = palette;
        _opaque = opaque;
        _cutout = cutout;
    }

    public static bool InBounds(Vector3I p) =>
        p.X >= 0 && p.Y >= 0 && p.Z >= 0 && p.X < Size && p.Y < Size && p.Z < Size;

    public ushort Get(Vector3I p) => InBounds(p) ? _cells[Index(p)] : BlockPalette.Air;

    /// <summary>Sets a block without remeshing; call <see cref="RebuildAll"/> afterwards. For world generation.</summary>
    public void SetRaw(Vector3I p, ushort block)
    {
        if (InBounds(p)) _cells[Index(p)] = block;
    }

    /// <summary>Sets a block and remeshes the affected chunks. Returns false outside the world.</summary>
    public bool Set(Vector3I p, ushort block)
    {
        if (!InBounds(p)) return false;
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

    public void RebuildAll()
    {
        for (int z = 0; z < Chunks; z++)
        for (int y = 0; y < Chunks; y++)
        for (int x = 0; x < Chunks; x++)
            RebuildChunk(new Vector3I(x, y, z));
    }

    void RebuildChunk(Vector3I chunk)
    {
        if (chunk.X < 0 || chunk.Y < 0 || chunk.Z < 0 || chunk.X >= Chunks || chunk.Y >= Chunks || chunk.Z >= Chunks) return;
        int slot = (chunk.Z * Chunks + chunk.Y) * Chunks + chunk.X;
        var mesh = ChunkMesher.Build(this, chunk * ChunkSize, ChunkSize, _opaque, _cutout);
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
