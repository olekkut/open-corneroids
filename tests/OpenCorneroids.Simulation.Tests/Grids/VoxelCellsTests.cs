using OpenCorneroids.Simulation.Grids;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Grids;

public class VoxelCellsTests
{
    /// <summary>The answers the bookkeeping must give, worked out by scanning every cell.</summary>
    static void AssertMatchesFullScan(VoxelCells cells, ushort[] blocks, bool[] objects)
    {
        int size = cells.Size;
        var filled = new HashSet<int>();
        var expectedBlocks = new List<(Int3, ushort)>();
        Int3? min = null, max = null;
        for (int i = 0; i < blocks.Length; i++)
        {
            var c = new Int3(i % size, i / size % size, i / (size * size));
            Assert.Equal(blocks[i], cells[i]);
            Assert.Equal(blocks[i] != 0 || objects[i], cells.IsSolid(c.X, c.Y, c.Z));
            if (blocks[i] == 0) continue;
            filled.Add(cells.SlotOf(c.X, c.Y, c.Z));
            expectedBlocks.Add((c, blocks[i]));
            min = min is { } a ? Int3.Min(a, c) : c;
            max = max is { } b ? Int3.Max(b, c) : c;
        }
        Assert.Equal(expectedBlocks.Count, cells.Count);
        Assert.Equal(filled.Count, cells.FilledChunks);
        Assert.Equal(objects.Count(o => o), cells.ObjectCellCount);
        Assert.Equal(expectedBlocks, cells.Blocks().ToList());
        for (int slot = 0; slot < cells.ChunkSlots; slot++)
            Assert.Equal(filled.Contains(slot), cells.BlocksInChunk(slot) > 0);
        Assert.Equal(min is not null, cells.TryBlockBounds(out var lo, out var hi));
        if (min is not null)
        {
            Assert.Equal(min, lo);
            Assert.Equal(max, hi);
        }
    }

    [Fact]
    public void A_new_grid_is_empty_and_nothing_is_solid()
    {
        var cells = new VoxelCells(32);

        Assert.Equal(0, cells.Count);
        Assert.Equal(0, cells.FilledChunks);
        Assert.Equal(8, cells.ChunkSlots);
        Assert.Empty(cells.Blocks());
        Assert.False(cells.TryBlockBounds(out _, out _));
        Assert.False(cells.IsSolid(0, 0, 0));
        Assert.False(cells.IsSolid(-1, 0, 0));
        Assert.False(cells.IsSolid(32, 0, 0));
        Assert.Equal(0, cells.Get(40, 0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(17)]
    public void A_grid_edge_must_be_a_multiple_of_the_chunk_size(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelCells(size));

    [Fact]
    public void Counts_and_filled_chunks_follow_edits()
    {
        var cells = new VoxelCells(32);

        Assert.Equal(0, cells.Set(1, 2, 3, 5));
        Assert.Equal(0, cells.Set(20, 2, 3, 5));
        Assert.Equal(5, cells.Set(20, 2, 3, 7));
        Assert.Equal((2, 2), (cells.Count, cells.FilledChunks));

        cells.Set(1, 2, 3, 0);
        Assert.Equal((1, 1), (cells.Count, cells.FilledChunks));
        cells.Set(20, 2, 3, 0);
        Assert.Equal((0, 0), (cells.Count, cells.FilledChunks));
    }

    [Fact]
    public void Object_cells_are_solid_without_being_blocks()
    {
        var cells = new VoxelCells(16);

        Assert.True(cells.SetObjectCell(4, 4, 4, true));
        Assert.False(cells.SetObjectCell(4, 4, 4, true));
        Assert.False(cells.SetObjectCell(-1, 4, 4, true));

        Assert.True(cells.IsSolid(4, 4, 4));
        Assert.True(cells.IsObjectCell(4, 4, 4));
        Assert.Equal(0, cells.Count);
        Assert.Equal(0, cells.FilledChunks);
        Assert.False(cells.IsChunkEmpty(0));
        Assert.Empty(cells.Blocks());

        cells.SetObjectCell(4, 4, 4, false);
        Assert.False(cells.IsSolid(4, 4, 4));
        Assert.True(cells.IsChunkEmpty(0));
    }

    [Fact]
    public void The_stamp_moves_only_when_what_is_solid_changes()
    {
        var cells = new VoxelCells(32);
        int start = cells.SolidStamp;

        cells.Set(1, 1, 1, 3);
        int afterPlace = cells.SolidStamp;
        Assert.NotEqual(start, afterPlace);
        Assert.Equal(afterPlace, cells.ChunkStamp(0));

        cells.Set(1, 1, 1, 4);                 // another block in a solid cell: still solid
        cells.SetObjectCell(1, 1, 1, true);    // an object model over a block: still solid
        Assert.Equal(afterPlace, cells.SolidStamp);

        cells.Set(1, 1, 1, 0);                 // the object keeps it solid
        Assert.Equal(afterPlace, cells.SolidStamp);

        cells.SetObjectCell(1, 1, 1, false);
        Assert.NotEqual(afterPlace, cells.SolidStamp);
        Assert.Equal(0, cells.ChunkStamp(1));  // a chunk nothing happened in keeps its stamp
    }

    [Fact]
    public void Clearing_the_blocks_keeps_the_object_cells()
    {
        var cells = new VoxelCells(32);
        cells.Set(1, 1, 1, 3);
        cells.Set(30, 30, 30, 3);
        cells.SetObjectCell(5, 5, 5, true);

        cells.ClearBlocks();

        Assert.Equal((0, 0), (cells.Count, cells.FilledChunks));
        Assert.Empty(cells.Blocks());
        Assert.True(cells.IsSolid(5, 5, 5));
        Assert.False(cells.IsSolid(1, 1, 1));
    }

    [Fact]
    public void Blocks_come_in_cell_order_across_chunks()
    {
        var cells = new VoxelCells(32);
        cells.Set(17, 0, 0, 2);
        cells.Set(3, 0, 0, 1);
        cells.Set(0, 1, 0, 3);
        cells.Set(31, 31, 31, 4);

        Assert.Equal(
            [(new Int3(3, 0, 0), (ushort)1), (new Int3(17, 0, 0), (ushort)2), (new Int3(0, 1, 0), (ushort)3), (new Int3(31, 31, 31), (ushort)4)],
            cells.Blocks().ToList());
    }

    [Fact]
    public void Random_edits_match_a_full_scan()
    {
        var rng = new Random(8128);
        const int size = 48;
        var cells = new VoxelCells(size);
        var blocks = new ushort[size * size * size];
        var objects = new bool[blocks.Length];
        for (int round = 0; round < 20; round++)
        {
            for (int edit = 0; edit < 200; edit++)
            {
                // Edits cluster in a few chunks so chunks fill up and empty out again.
                int x = rng.Next(round % 3 * 16, round % 3 * 16 + 16), y = rng.Next(size), z = rng.Next(0, 20);
                int i = cells.Index(x, y, z);
                if (rng.Next(4) == 0)
                {
                    bool covered = rng.Next(2) == 0;
                    cells.SetObjectCell(x, y, z, covered);
                    objects[i] = covered;
                }
                else
                {
                    var block = rng.Next(3) == 0 ? (ushort)0 : (ushort)rng.Next(1, 6);
                    Assert.Equal(blocks[i], cells.Set(x, y, z, block));
                    blocks[i] = block;
                }
            }
            AssertMatchesFullScan(cells, blocks, objects);
        }
    }
}
