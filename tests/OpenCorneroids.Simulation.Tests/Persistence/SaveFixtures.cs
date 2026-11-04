using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>Synthetic worlds and itemsets. No original game data.</summary>
static class SaveFixtures
{
    public static readonly ItemsetBinding Binding = new("MiniItemset", "Mini", "test-pack", "ab12cd34");

    public static readonly DateTimeOffset SavedAt = new DateTimeOffset(2026, 11, 3, 12, 34, 56, TimeSpan.FromHours(2)).AddTicks(1_234_567);

    /// <summary>A 5³ world with short random runs over a palette that includes blockId 0.</summary>
    public static WorldSnapshot SmallWorld(int size = 5, int seed = 1234)
    {
        int[] palette = [WorldSnapshot.AirBlockId, 0, 7, 3];
        var random = new Random(seed);
        var cells = new ushort[size * size * size];
        for (int i = 0; i < cells.Length;)
        {
            var value = (ushort)random.Next(palette.Length);
            for (int run = random.Next(1, 6); run > 0 && i < cells.Length; run--) cells[i++] = value;
        }
        return World(size, palette, cells) with
        {
            Name = "Kosmos Ω \"quoted\" \\ name",
            Seed = -987654,
            Player = new PlayerState(new Vector3(0.1f, -1e-7f, 3.4e38f), new Vector3(-2.5f, 0, float.Epsilon), -1.2345678f, 6.2831855f),
            Inventory = [new ItemStack(0, 5), null, new ItemStack(21, 1), null],
            Creative = true,
            TexturePacks = ["SordsTextures", "Pack \"Ω\""],
            Objects = [new PlacedObject(5, new Int3(1, 2, 3), 13, 0.25f), new PlacedObject(0, Int3.Zero, 0, 0), new PlacedObject(7, new Int3(4, 4, 4), 23, 1)],
        };
    }

    public static WorldSnapshot World(int size, int[] palette, ushort[] cells) =>
        new("Test world", SavedAt, Binding, 42, size, palette, cells,
            new PlayerState(new Vector3(1, 2, 3), Vector3.Zero, 0, 0), []);

    public static WorldSnapshot AllAir(int size) => World(size, [WorldSnapshot.AirBlockId], new ushort[size * size * size]);

    public static byte[] ToBytes(WorldSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        WorldSaveFormat.Write(stream, snapshot);
        return stream.ToArray();
    }

    public static WorldSnapshot FromBytes(byte[] bytes) => WorldSaveFormat.Read(new MemoryStream(bytes));

    public static void AssertSameWorld(WorldSnapshot expected, WorldSnapshot actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.SavedAt, actual.SavedAt);
        Assert.Equal(expected.SavedAt.Offset, actual.SavedAt.Offset);
        Assert.Equal(expected.Itemset, actual.Itemset);
        Assert.Equal(expected.Seed, actual.Seed);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.Palette, actual.Palette);
        Assert.True(expected.Cells.AsSpan().SequenceEqual(actual.Cells), "cells differ");
        Assert.Equal(expected.Player, actual.Player);
        Assert.Equal(expected.Inventory, actual.Inventory);
        Assert.Equal(expected.Creative, actual.Creative);
        Assert.Equal(expected.TexturePacks, actual.TexturePacks);
        Assert.Equal(expected.Objects, actual.Objects);
    }

    // ---- raw container pieces, for building damaged files by hand ----

    public static (ushort Version, byte[] Json, byte[] CellBlock) Split(byte[] save)
    {
        var version = BinaryPrimitives.ReadUInt16LittleEndian(save.AsSpan(4));
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(save.AsSpan(6));
        var json = save.AsSpan(10, jsonLength).ToArray();
        var cells = save.AsSpan(10 + jsonLength).ToArray();
        return (version, json, cells);
    }

    public static byte[] Container(byte[] json, byte[] cellBlock, ushort version = WorldSaveFormat.CurrentVersion,
        int? jsonLength = null, string magic = "OCWS")
    {
        using var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(magic));
        writer.Write(version);
        writer.Write(jsonLength ?? json.Length);
        writer.Write(json);
        writer.Write(cellBlock);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] Zlib(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw);
        return output.ToArray();
    }

    /// <summary>
    /// A zlib stream that was flushed but never finished: header and deflate data with no final block and no Adler-32,
    /// which is what a writer killed mid-save leaves behind.
    /// </summary>
    public static byte[] UnfinishedZlib(byte[] raw)
    {
        using var output = new MemoryStream();
        output.Write([0x78, 0x9C]);
        var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true);
        deflate.Write(raw);
        deflate.Flush();
        return output.ToArray();
    }

    /// <summary>Uncompressed run stream: u16 value, then the run length as unsigned LEB128.</summary>
    public static byte[] Runs(params (int Value, uint Run)[] runs)
    {
        var bytes = new List<byte>();
        foreach (var (value, run) in runs)
        {
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
            var r = run;
            for (; r >= 0x80; r >>= 7) bytes.Add((byte)(r | 0x80));
            bytes.Add((byte)r);
        }
        return [.. bytes];
    }

    // ---- itemsets for binding checks ----

    public static ItemsetDef Itemset(string key, string fingerprint, params int[] blockIds)
    {
        var rect = new CellRect(0, 0, 1, 1);
        var faces = new FaceSet(rect, rect, rect);
        var items = blockIds.Select((blockId, i) => new ItemDef(
            100 + i, "asteroid", "asteroids", $"Block {blockId}", "", new Rgb8(10, 20, 30), 64, 1, 0, rect,
            new BlockDef(blockId, 1, 5, 1, Vector3.One, Vector3.Zero, Vector3.Zero, faces, null, null),
            [], new Dictionary<string, string>(), null)).ToList();
        return new ItemsetDef
        {
            Path = "Blocksets/test.xml",
            Header = new ItemsetHeader("Mini", "Tests", key, 1, null, null),
            Items = items,
            ItemsById = items.ToDictionary(i => i.ItemId),
            ItemsByBlockId = items.ToDictionary(i => i.Block!.BlockId),
            Projectiles = new Dictionary<int, ProjectileDef>(),
            Blueprints = [],
            DefaultItems = [],
            Fingerprint = fingerprint,
        };
    }
}

/// <summary>Hides seekability so the reader can't use the file length as a shortcut for truncation checks.</summary>
sealed class ForwardOnlyStream(byte[] data) : Stream
{
    readonly MemoryStream _inner = new(data);

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 7));
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
