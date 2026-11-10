using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Grids;

/// <summary>
/// The cells of one cubic block grid (the static world or a ship) with the bookkeeping that keeps questions about them
/// cheap after an edit: how many blocks each 16³ chunk holds (so empty chunks are skipped in one check, and
/// <see cref="Count"/>, <see cref="FilledChunks"/> and "is it empty" are O(1)), which cells a multi-block's model makes
/// solid (so <see cref="IsSolid"/> is two array reads), and a stamp per chunk that moves whenever what is solid there
/// changes (so data derived from the solid shape, such as <see cref="SurfaceSampler"/>, rebuilds only the chunks that
/// changed). Ours: how the grid is stored is not a game rule.
/// </summary>
/// <remarks>Cells are stored at <c>(z * Size + y) * Size + x</c>; 0 is air.</remarks>
public sealed class VoxelCells
{
    public const int ChunkSize = 16;
    public const ushort Air = 0;

    readonly ushort[] _cells;
    readonly bool[] _objectCells;
    readonly int[] _blocksInChunk;
    readonly int[] _objectCellsInChunk;
    readonly int[] _chunkStamps;

    /// <param name="size">Edge length in cells; a positive multiple of <see cref="ChunkSize"/>.</param>
    public VoxelCells(int size)
    {
        if (size < ChunkSize || size % ChunkSize != 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, $"A grid edge must be a positive multiple of {ChunkSize}.");
        Size = size;
        Chunks = size / ChunkSize;
        _cells = new ushort[size * size * size];
        _objectCells = new bool[_cells.Length];
        int slots = Chunks * Chunks * Chunks;
        _blocksInChunk = new int[slots];
        _objectCellsInChunk = new int[slots];
        _chunkStamps = new int[slots];
    }

    /// <summary>Edge length in cells.</summary>
    public int Size { get; }

    /// <summary>Chunks along one edge.</summary>
    public int Chunks { get; }

    /// <summary>Chunk slots in all, <c>Chunks³</c>; slot = <c>(cz * Chunks + cy) * Chunks + cx</c>.</summary>
    public int ChunkSlots => _blocksInChunk.Length;

    /// <summary>Grid blocks (non-air cells).</summary>
    public int Count { get; private set; }

    /// <summary>Chunks holding at least one grid block.</summary>
    public int FilledChunks { get; private set; }

    /// <summary>Cells made solid by an object's model.</summary>
    public int ObjectCellCount { get; private set; }

    /// <summary>Moves on every change of what is solid anywhere; equal values mean nothing solid changed.</summary>
    public int SolidStamp { get; private set; }

    /// <summary>The cells, read-only, in storage order.</summary>
    public ReadOnlySpan<ushort> Cells => _cells;

    public bool InGrid(int x, int y, int z) =>
        (uint)x < (uint)Size && (uint)y < (uint)Size && (uint)z < (uint)Size;

    public int Index(int x, int y, int z) => (z * Size + y) * Size + x;

    public Int3 CellOf(int index) => new(index % Size, index / Size % Size, index / (Size * Size));

    /// <summary>The chunk slot of an in-grid cell.</summary>
    public int SlotOf(int x, int y, int z) => (z / ChunkSize * Chunks + y / ChunkSize) * Chunks + x / ChunkSize;

    /// <summary>The chunk coordinates of a slot.</summary>
    public Int3 ChunkAt(int slot) => new(slot % Chunks, slot / Chunks % Chunks, slot / (Chunks * Chunks));

    /// <summary>The block at a cell; air outside the grid.</summary>
    public ushort Get(int x, int y, int z) => InGrid(x, y, z) ? _cells[Index(x, y, z)] : Air;

    /// <summary>The block at a storage index.</summary>
    public ushort this[int index] => _cells[index];

    /// <summary>A grid block or an object's model is there; false outside the grid.</summary>
    public bool IsSolid(int x, int y, int z)
    {
        if (!InGrid(x, y, z)) return false;
        int i = Index(x, y, z);
        return _cells[i] != Air || _objectCells[i];
    }

    /// <summary>An object's model covers the cell; false outside the grid.</summary>
    public bool IsObjectCell(int x, int y, int z) => InGrid(x, y, z) && _objectCells[Index(x, y, z)];

    /// <summary>Grid blocks in the chunk at <paramref name="slot"/>.</summary>
    public int BlocksInChunk(int slot) => _blocksInChunk[slot];

    /// <summary>Nothing solid at all in the chunk: no grid block and no object model cell.</summary>
    public bool IsChunkEmpty(int slot) => _blocksInChunk[slot] == 0 && _objectCellsInChunk[slot] == 0;

    /// <summary>The stamp of the last change of what is solid in the chunk (0 if it never changed).</summary>
    public int ChunkStamp(int slot) => _chunkStamps[slot];

    /// <summary>Sets the block at an in-grid cell and returns the one that was there.</summary>
    public ushort Set(int x, int y, int z, ushort block)
    {
        if (!InGrid(x, y, z)) throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}, {z}) is outside the grid.");
        return SetAt(Index(x, y, z), block);
    }

    /// <summary>Sets the block at a storage index and returns the one that was there.</summary>
    public ushort SetAt(int index, ushort block)
    {
        var old = _cells[index];
        if (old == block) return old;
        _cells[index] = block;
        if ((old == Air) == (block == Air)) return old;
        var cell = CellOf(index);
        int slot = SlotOf(cell.X, cell.Y, cell.Z);
        int change = block == Air ? -1 : 1;
        if (change > 0 && _blocksInChunk[slot] == 0) FilledChunks++;
        _blocksInChunk[slot] += change;
        if (change < 0 && _blocksInChunk[slot] == 0) FilledChunks--;
        Count += change;
        if (!_objectCells[index]) Touch(slot);
        return old;
    }

    /// <summary>Marks or clears an in-grid cell as covered by an object's model; false when nothing changed.</summary>
    public bool SetObjectCell(int x, int y, int z, bool covered)
    {
        if (!InGrid(x, y, z)) return false;
        int i = Index(x, y, z);
        if (_objectCells[i] == covered) return false;
        _objectCells[i] = covered;
        int slot = SlotOf(x, y, z);
        int change = covered ? 1 : -1;
        _objectCellsInChunk[slot] += change;
        ObjectCellCount += change;
        if (_cells[i] == Air) Touch(slot);
        return true;
    }

    /// <summary>Empties every grid block (object cells stay).</summary>
    public void ClearBlocks()
    {
        if (Count == 0) return;
        for (int slot = 0; slot < _blocksInChunk.Length; slot++)
        {
            if (_blocksInChunk[slot] == 0) continue;
            _blocksInChunk[slot] = 0;
            Touch(slot);
        }
        Array.Clear(_cells);
        Count = 0;
        FilledChunks = 0;
    }

    /// <summary>Copies every cell, in storage order, into <paramref name="buffer"/> (at least Size³ long).</summary>
    public void CopyTo(ushort[] buffer) => _cells.CopyTo(buffer, 0);

    void Touch(int slot) => _chunkStamps[slot] = ++SolidStamp;

    /// <summary>
    /// Every grid block with its cell, in storage order (the order a plain scan of the cells gives), skipping each run
    /// of 16 cells whose chunk holds no block in one check.
    /// </summary>
    public IEnumerable<(Int3 Cell, ushort Block)> Blocks()
    {
        if (Count == 0) yield break;
        for (int z = 0; z < Size; z++)
        for (int y = 0; y < Size; y++)
        {
            int rowSlot = (z / ChunkSize * Chunks + y / ChunkSize) * Chunks;
            int row = (z * Size + y) * Size;
            for (int cx = 0; cx < Chunks; cx++)
            {
                if (_blocksInChunk[rowSlot + cx] == 0) continue;
                int start = row + cx * ChunkSize;
                for (int i = start; i < start + ChunkSize; i++)
                    if (_cells[i] != Air) yield return (new Int3(i - row, y, z), _cells[i]);
            }
        }
    }

    /// <summary>
    /// The smallest and largest cell over every grid block; false when there is none. Only the chunks on the outer
    /// layers of the filled chunks are scanned: the block furthest along an axis must lie in a chunk furthest along it.
    /// </summary>
    public bool TryBlockBounds(out Int3 min, out Int3 max)
    {
        min = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        max = new Int3(int.MinValue, int.MinValue, int.MinValue);
        if (Count == 0) return false;
        var lo = new Int3(int.MaxValue, int.MaxValue, int.MaxValue);
        var hi = new Int3(int.MinValue, int.MinValue, int.MinValue);
        for (int slot = 0; slot < _blocksInChunk.Length; slot++)
        {
            if (_blocksInChunk[slot] == 0) continue;
            var c = ChunkAt(slot);
            lo = Int3.Min(lo, c);
            hi = Int3.Max(hi, c);
        }
        for (int slot = 0; slot < _blocksInChunk.Length; slot++)
        {
            if (_blocksInChunk[slot] == 0) continue;
            var c = ChunkAt(slot);
            if (c.X != lo.X && c.Y != lo.Y && c.Z != lo.Z && c.X != hi.X && c.Y != hi.Y && c.Z != hi.Z) continue;
            var origin = c * ChunkSize;
            for (int z = origin.Z; z < origin.Z + ChunkSize; z++)
            for (int y = origin.Y; y < origin.Y + ChunkSize; y++)
            {
                int row = Index(origin.X, y, z);
                for (int x = 0; x < ChunkSize; x++)
                {
                    if (_cells[row + x] == Air) continue;
                    var p = new Int3(origin.X + x, y, z);
                    min = Int3.Min(min, p);
                    max = Int3.Max(max, p);
                }
            }
        }
        return true;
    }
}
