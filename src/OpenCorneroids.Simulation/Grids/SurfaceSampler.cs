using System.Numerics;

namespace OpenCorneroids.Simulation.Grids;

/// <summary>
/// Points that stand for a grid's solid volume in collisions, grid-local: for every solid cell (grid block or object
/// model cell) with an open face, a point just inside each open face (<see cref="FaceInset"/> of a cell out from the
/// centre) and the centre itself. Buried cells add nothing: a body can't reach them without first touching the
/// surface. Ours.
/// </summary>
/// <remarks>
/// The points are kept per chunk. A chunk's points depend only on what is solid in it and in its six face neighbours,
/// so after an edit only the chunks whose own or neighbouring <see cref="VoxelCells.ChunkStamp"/> moved are sampled
/// again; the rest keep theirs.
/// </remarks>
public sealed class SurfaceSampler
{
    /// <summary>How far from a cell's centre its face points sit, in cells.</summary>
    public const float FaceInset = 0.45f;

    /// <summary>The six face directions, in the order points are added: +X, −X, +Y, −Y, +Z, −Z.</summary>
    static readonly (int X, int Y, int Z)[] Steps = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    readonly VoxelCells _cells;
    readonly List<Vector3>?[] _chunkPoints;
    readonly int[] _builtFrom;
    int _seenStamp = -1;
    Vector3[] _points = [];

    public SurfaceSampler(VoxelCells cells)
    {
        _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        _chunkPoints = new List<Vector3>?[cells.ChunkSlots];
        _builtFrom = new int[cells.ChunkSlots];
        Array.Fill(_builtFrom, -1);
    }

    /// <summary>Chunks sampled again by the last <see cref="Points"/> that found a change (for the debug readout and tests).</summary>
    public int LastRebuilt { get; private set; }

    /// <summary>Every point, chunk by chunk; the same array until something solid changes.</summary>
    public IReadOnlyList<Vector3> Points
    {
        get
        {
            if (_seenStamp == _cells.SolidStamp) return _points;
            _seenStamp = _cells.SolidStamp;
            int rebuilt = 0;
            for (int slot = 0; slot < _chunkPoints.Length; slot++)
            {
                int stamp = NeighbourhoodStamp(slot);
                if (stamp == _builtFrom[slot]) continue;
                _builtFrom[slot] = stamp;
                rebuilt++;
                if (_cells.IsChunkEmpty(slot))
                {
                    _chunkPoints[slot]?.Clear();
                    continue;
                }
                var list = _chunkPoints[slot] ??= new List<Vector3>();
                list.Clear();
                AddChunk(_cells, slot, list);
            }
            LastRebuilt = rebuilt;
            if (rebuilt == 0) return _points;
            int total = 0;
            foreach (var list in _chunkPoints) total += list?.Count ?? 0;
            var points = new Vector3[total];
            int at = 0;
            foreach (var list in _chunkPoints)
            {
                if (list is null) continue;
                list.CopyTo(points, at);
                at += list.Count;
            }
            return _points = points;
        }
    }

    /// <summary>The latest stamp of the chunk and its six face neighbours: its points can only change when this does.</summary>
    int NeighbourhoodStamp(int slot)
    {
        int n = _cells.Chunks;
        var c = _cells.ChunkAt(slot);
        int stamp = _cells.ChunkStamp(slot);
        if (c.X > 0) stamp = Math.Max(stamp, _cells.ChunkStamp(slot - 1));
        if (c.X < n - 1) stamp = Math.Max(stamp, _cells.ChunkStamp(slot + 1));
        if (c.Y > 0) stamp = Math.Max(stamp, _cells.ChunkStamp(slot - n));
        if (c.Y < n - 1) stamp = Math.Max(stamp, _cells.ChunkStamp(slot + n));
        if (c.Z > 0) stamp = Math.Max(stamp, _cells.ChunkStamp(slot - n * n));
        if (c.Z < n - 1) stamp = Math.Max(stamp, _cells.ChunkStamp(slot + n * n));
        return stamp;
    }

    /// <summary>Adds the points of every solid cell in one chunk, cells in storage order.</summary>
    public static void AddChunk(VoxelCells cells, int slot, List<Vector3> into)
    {
        var origin = cells.ChunkAt(slot) * VoxelCells.ChunkSize;
        for (int z = origin.Z; z < origin.Z + VoxelCells.ChunkSize; z++)
        for (int y = origin.Y; y < origin.Y + VoxelCells.ChunkSize; y++)
        for (int x = origin.X; x < origin.X + VoxelCells.ChunkSize; x++)
            if (cells.IsSolid(x, y, z)) AddCell(cells, x, y, z, into);
    }

    /// <summary>Adds the points of one solid cell: one per open face, then the centre if any face was open.</summary>
    public static void AddCell(VoxelCells cells, int x, int y, int z, List<Vector3> into)
    {
        var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
        bool open = false;
        foreach (var (sx, sy, sz) in Steps)
        {
            if (cells.IsSolid(x + sx, y + sy, z + sz)) continue;
            into.Add(center + new Vector3(sx, sy, sz) * FaceInset);
            open = true;
        }
        if (open) into.Add(center);
    }
}
