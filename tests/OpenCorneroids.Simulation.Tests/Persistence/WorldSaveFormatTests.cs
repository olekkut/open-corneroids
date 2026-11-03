using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using OpenCorneroids.Simulation.Persistence;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

public class WorldSaveFormatTests
{
    [Fact]
    public void Round_trip_preserves_every_field()
    {
        var original = SmallWorld();
        AssertSameWorld(original, FromBytes(ToBytes(original)));
    }

    [Fact]
    public void BlockId_zero_stays_distinct_from_air()
    {
        int[] palette = [WorldSnapshot.AirBlockId, 0];
        var cells = new ushort[3 * 3 * 3];
        for (int i = 0; i < cells.Length; i += 2) cells[i] = 1;
        var world = World(3, palette, cells);

        var copy = FromBytes(ToBytes(world));

        Assert.Equal([-1, 0], copy.Palette);
        Assert.Equal(1, copy.Cells[0]);
        Assert.Equal(WorldSnapshot.Air, copy.Cells[1]);
        AssertSameWorld(world, copy);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(17)]
    public void Round_trips_edge_sizes_and_run_shapes(int size)
    {
        // Checkerboard: every run has length 1. Followed by an all-air world: one run of size³.
        var cells = new ushort[size * size * size];
        for (int i = 0; i < cells.Length; i++) cells[i] = (ushort)(i % 2);
        var checker = World(size, [WorldSnapshot.AirBlockId, 5], cells);
        AssertSameWorld(checker, FromBytes(ToBytes(checker)));
        AssertSameWorld(AllAir(size), FromBytes(ToBytes(AllAir(size))));
    }

    [Fact]
    public void Round_trips_a_palette_using_the_full_u16_range()
    {
        var palette = Enumerable.Range(-1, WorldSaveFormat.MaxPaletteEntries).ToArray();
        palette[0] = WorldSnapshot.AirBlockId;
        var cells = new ushort[41 * 41 * 41];
        for (int i = 0; i < cells.Length; i++) cells[i] = (ushort)(i * 7919 % palette.Length);
        cells[^1] = ushort.MaxValue;
        var world = World(41, palette, cells);
        AssertSameWorld(world, FromBytes(ToBytes(world)));
    }

    [Fact]
    public void Mostly_air_128_world_compresses_well_under_100_KB()
    {
        const int size = 128;
        int[] palette = [WorldSnapshot.AirBlockId, 0, 1, 4];
        var cells = new ushort[size * size * size];
        var random = new Random(7);
        var center = new Vector3(64, 60, 70);
        for (int z = 0; z < size; z++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var distance = Vector3.Distance(new Vector3(x, y, z), center);
                    if (distance < 14) cells[WorldSnapshot.CellIndex(size, x, y, z)] = (ushort)(random.Next(10) == 0 ? 2 : 1);
                }
        for (int i = 0; i < 500; i++) cells[random.Next(cells.Length)] = 3;
        var world = World(size, palette, cells);

        var bytes = ToBytes(world);

        Assert.True(bytes.Length < 100_000, $"save is {bytes.Length} bytes");
        AssertSameWorld(world, FromBytes(bytes));
    }

    [Fact]
    public void All_air_128_world_is_tiny()
    {
        // 128³ = 2^21 cells in one run, which needs a 4-byte varint.
        var bytes = ToBytes(AllAir(128));
        Assert.True(bytes.Length < 1_000, $"save is {bytes.Length} bytes");
        AssertSameWorld(AllAir(128), FromBytes(bytes));
    }

    [Fact]
    public void Container_starts_with_magic_and_little_endian_version()
    {
        var bytes = ToBytes(SmallWorld());
        Assert.Equal("OCWS"u8.ToArray(), bytes[..4]);
        Assert.Equal(WorldSaveFormat.CurrentVersion, BitConverter.ToUInt16(bytes, 4));
        var json = JsonNode.Parse(Split(bytes).Json)!.AsObject();
        Assert.Equal("Kosmos Ω \"quoted\" \\ name", (string?)json["name"]);
        Assert.False(json.ContainsKey("cells"));
    }

    [Fact]
    public void Summary_reads_metadata_without_touching_cells()
    {
        var world = SmallWorld();
        var (_, json, cells) = Split(ToBytes(world));
        var garbageCells = Container(json, [.. cells.Select(b => (byte)~b)]);

        var summary = WorldSaveFormat.ReadSummary(new MemoryStream(garbageCells));

        Assert.Equal(new WorldSummary(world.Name, world.SavedAt, world.Itemset, ""), summary);
        Assert.Throws<WorldSaveException>(() => FromBytes(garbageCells));
    }

    [Fact]
    public void Reads_from_a_forward_only_stream()
    {
        var world = SmallWorld();
        AssertSameWorld(world, WorldSaveFormat.Read(new ForwardOnlyStream(ToBytes(world))));
    }

    [Fact]
    public void Write_rejects_inconsistent_snapshots()
    {
        var good = SmallWorld();
        WorldSnapshot[] bad =
        [
            good with { Cells = new ushort[good.Cells.Length - 1] },
            good with { Size = 0, Cells = [] },
            good with { Size = WorldSaveFormat.MaxSize + 1 },
            good with { Palette = [0, 7, 3, 2] },
            good with { Palette = [] },
            good with { Palette = [-1, -5, 3, 2] },
            good with { Palette = [-1, 0] },
            good with { Player = good.Player with { Yaw = float.NaN } },
            good with { Player = good.Player with { Position = new Vector3(float.PositiveInfinity, 0, 0) } },
        ];
        foreach (var snapshot in bad)
            Assert.Throws<ArgumentException>(() => WorldSaveFormat.Write(new MemoryStream(), snapshot));
    }

    // ---- untrusted input ----

    /// <summary>A 2³ world over palette [-1, 0, 5]; damaged variants are built from its pieces.</summary>
    static readonly byte[] Valid = ToBytes(World(2, [WorldSnapshot.AirBlockId, 0, 5], [0, 1, 1, 2, 0, 0, 0, 2]));

    static byte[] ValidJson => Split(Valid).Json;
    static byte[] ValidCells => Split(Valid).CellBlock;

    static byte[] EditJson(Action<JsonObject> edit)
    {
        var json = JsonNode.Parse(ValidJson)!.AsObject();
        edit(json);
        return Container(Encoding.UTF8.GetBytes(json.ToJsonString()), ValidCells);
    }

    static byte[] WithRuns(params (int Value, uint Run)[] runs) => Container(ValidJson, Zlib(Runs(runs)));

    static readonly Dictionary<string, Func<byte[]>> Damaged = new()
    {
        ["empty file"] = () => [],
        ["wrong magic"] = () => Container(ValidJson, ValidCells, magic: "OCWX"),
        ["text file"] = () => Encoding.UTF8.GetBytes("hello, this is not a world"),
        ["newer version"] = () => Container(ValidJson, ValidCells, version: WorldSaveFormat.CurrentVersion + 1),
        ["max version"] = () => Container(ValidJson, ValidCells, version: ushort.MaxValue),
        ["version zero"] = () => Container(ValidJson, ValidCells, version: 0),
        ["negative json length"] = () => Container(ValidJson, ValidCells, jsonLength: -1),
        ["json length over cap"] = () => Container(ValidJson, ValidCells, jsonLength: WorldSaveFormat.MaxMetadataBytes + 1),
        ["json length int max"] = () => Container(ValidJson, ValidCells, jsonLength: int.MaxValue),
        ["json length too long"] = () => Container(ValidJson, ValidCells, jsonLength: ValidJson.Length + 3),
        ["json length too short"] = () => Container(ValidJson, ValidCells, jsonLength: ValidJson.Length - 3),
        ["not json"] = () => Container("{not json"u8.ToArray(), ValidCells),
        ["empty json"] = () => Container([], ValidCells),
        ["json null"] = () => Container("null"u8.ToArray(), ValidCells),
        ["json array"] = () => Container("[1,2]"u8.ToArray(), ValidCells),
        ["json invalid utf8"] = () => Container([.. "{\"name\":\""u8, 0xFF, 0xFE, .. "\"}"u8], ValidCells),
        ["json deeply nested"] = () => Container(Encoding.UTF8.GetBytes("{\"name\":" + new string('[', 500) + new string(']', 500) + "}"), ValidCells),
        ["missing name"] = () => EditJson(j => j.Remove("name")),
        ["null name"] = () => EditJson(j => j["name"] = null),
        ["missing savedAt"] = () => EditJson(j => j.Remove("savedAt")),
        ["bad savedAt"] = () => EditJson(j => j["savedAt"] = "yesterday"),
        ["missing itemset"] = () => EditJson(j => j.Remove("itemset")),
        ["missing fingerprint"] = () => EditJson(j => j["itemset"]!.AsObject().Remove("fingerprint")),
        ["missing seed"] = () => EditJson(j => j.Remove("seed")),
        ["missing size"] = () => EditJson(j => j.Remove("size")),
        ["size as string"] = () => EditJson(j => j["size"] = "big"),
        ["size fraction"] = () => EditJson(j => j["size"] = 2.5),
        ["size overflow"] = () => EditJson(j => j["size"] = 1e12),
        ["size zero"] = () => EditJson(j => j["size"] = 0),
        ["size negative"] = () => EditJson(j => j["size"] = -2),
        ["size over cap"] = () => EditJson(j => j["size"] = WorldSaveFormat.MaxSize + 1),
        ["size larger than cells"] = () => EditJson(j => j["size"] = 3),
        ["size smaller than cells"] = () => EditJson(j => j["size"] = 1),
        ["missing palette"] = () => EditJson(j => j.Remove("palette")),
        ["empty palette"] = () => EditJson(j => j["palette"] = new JsonArray()),
        ["palette without air sentinel"] = () => EditJson(j => j["palette"] = new JsonArray(0, 1, 5)),
        ["negative palette blockId"] = () => EditJson(j => j["palette"] = new JsonArray(-1, -7, 5)),
        ["palette too long"] = () => EditJson(j => j["palette"] = new JsonArray([.. Enumerable.Range(-1, WorldSaveFormat.MaxPaletteEntries + 1).Select(i => JsonValue.Create(i))])),
        ["palette shorter than cells"] = () => EditJson(j => j["palette"] = new JsonArray(-1, 0)),
        ["missing player"] = () => EditJson(j => j.Remove("player")),
        ["missing position"] = () => EditJson(j => j["player"]!.AsObject().Remove("position")),
        ["position with two numbers"] = () => EditJson(j => j["player"]!["position"] = new JsonArray(1, 2)),
        ["missing yaw"] = () => EditJson(j => j["player"]!.AsObject().Remove("yaw")),
        ["NaN as string"] = () => EditJson(j => j["player"]!["pitch"] = "NaN"),
        ["missing inventory"] = () => EditJson(j => j.Remove("inventory")),
        ["inventory slot missing quantity"] = () => EditJson(j => j["inventory"] = new JsonArray(new JsonObject { ["itemId"] = 1 })),
        ["player position overflows float"] = () => EditJson(j => j["player"]!["position"] = new JsonArray(1e39, 0, 0)),
        ["missing cell block"] = () => Valid[..^ValidCells.Length],
        ["one byte of cell block"] = () => Container(ValidJson, ValidCells[..1]),
        ["byte after zlib stream"] = () => [.. Valid, 0],
        ["bytes after zlib stream"] = () => Container(ValidJson, [.. ValidCells, 0, 0, 0, 0]),
        ["many bytes after zlib stream"] = () => Container(ValidJson, [.. ValidCells, .. new byte[5000]]),
        ["second zlib stream"] = () => Container(ValidJson, [.. ValidCells, .. ValidCells]),
        ["not zlib"] = () => Container(ValidJson, [1, 2, 3, 4, 5, 6, 7, 8]),
        ["bad zlib checksum"] = () => Container(ValidJson, [.. ValidCells[..^1], (byte)~ValidCells[^1]]),
        ["zlib checksum missing"] = () => Container(ValidJson, ValidCells[..^4]),
        ["zlib checksum cut to 1 byte"] = () => Container(ValidJson, ValidCells[..^3]),
        ["zlib checksum cut to 3 bytes"] = () => Container(ValidJson, ValidCells[..^1]),
        ["zlib checksum missing, junk in its place"] = () => Container(ValidJson, [.. ValidCells[..^4], 0, 0, 0, 0]),
        ["zlib stream never finished"] = () => Container(ValidJson, UnfinishedZlib(Runs((0, 1), (1, 2), (2, 1), (0, 3), (2, 1)))),
        // 0x7820 passes the zlib header check but sets FDICT; .NET reports that as ZLibException, not InvalidDataException.
        ["zlib preset dictionary"] = () => Container(ValidJson, [0x78, 0x20, 0, 0, 0, 1, .. ValidCells[2..]]),
        // CM 9 with a valid FCHECK, so only the method check can reject it.
        ["zlib wrong method"] = () => Container(ValidJson, [0x79, 0x18, .. ValidCells[2..]]),
        ["no runs"] = () => WithRuns(),
        ["too few cells"] = () => WithRuns((0, 7)),
        ["too many cells"] = () => WithRuns((0, 9)),
        ["second run overflows"] = () => WithRuns((0, 5), (1, 4)),
        ["huge run"] = () => WithRuns((0, uint.MaxValue)),
        ["zero-length run"] = () => WithRuns((0, 0), (0, 8)),
        ["palette index out of range"] = () => WithRuns((0, 4), (3, 4)),
        ["palette index u16 max"] = () => WithRuns((ushort.MaxValue, 8)),
        ["trailing run after full world"] = () => WithRuns((0, 8), (0, 1)),
        ["trailing odd byte"] = () => Container(ValidJson, Zlib([.. Runs((0, 8)), 0])),
        ["value cut in half"] = () => Container(ValidJson, Zlib([0])),
        ["run length cut off"] = () => Container(ValidJson, Zlib([0, 0, 0x80])),
        ["varint over 5 bytes"] = () => Container(ValidJson, Zlib([0, 0, 0x88, 0x80, 0x80, 0x80, 0x80, 0x00])),
        ["varint over 32 bits"] = () => Container(ValidJson, Zlib([0, 0, 0x88, 0x80, 0x80, 0x80, 0x10])),
    };

    public static TheoryData<string> DamagedCases => [.. Damaged.Keys];

    [Theory]
    [MemberData(nameof(DamagedCases))]
    public void Damaged_input_throws_WorldSaveException(string name)
    {
        var bytes = Damaged[name]();
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(bytes));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Throws<WorldSaveException>(() => WorldSaveFormat.Read(new ForwardOnlyStream(bytes)));
    }

    [Fact]
    public void Newer_version_message_says_to_update()
    {
        var bytes = Container(ValidJson, ValidCells, version: 7);
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(bytes));
        Assert.Contains("version 7", error.Message);
    }

    // The caps must reject from the declared numbers alone; the damaged-input cases above would also pass if a too-large
    // value were only caught later, after allocating for it.

    [Theory]
    [InlineData(WorldSaveFormat.MaxSize + 1)]
    [InlineData(2000)] // 2000³ overflows int
    [InlineData(int.MaxValue)]
    public void Size_over_the_cap_is_rejected_by_name(int size)
    {
        var bytes = EditJson(j => j["size"] = size);
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(bytes));
        Assert.Contains($"size {size} is outside 1..{WorldSaveFormat.MaxSize}", error.Message);
        Assert.Throws<WorldSaveException>(() => WorldSaveFormat.ReadSummary(new MemoryStream(bytes)));
    }

    [Fact]
    public void Metadata_length_over_the_cap_is_rejected_before_reading_it()
    {
        // Followed by enough real bytes that only the cap, not running out of data, can reject it.
        var json = new byte[WorldSaveFormat.MaxMetadataBytes + 1];
        var bytes = Container(json, ValidCells);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(bytes));
        Assert.Contains($"metadata length {WorldSaveFormat.MaxMetadataBytes + 1} is outside", error.Message);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000, "the metadata was allocated anyway");
    }

    [Fact]
    public void Damaged_cells_of_a_huge_world_are_rejected_before_the_grid_is_allocated()
    {
        var hugeJson = Split(EditJson(j => j["size"] = WorldSaveFormat.MaxSize)).Json;
        // Garbage, a valid stream covering half the world, and one covering all of it that was never finished.
        byte[][] blocks = [[1, 2, 3], Zlib(Runs((0, 1 << 29))), UnfinishedZlib(Runs((0, 1 << 30)))];
        foreach (var block in blocks)
        {
            var bytes = Container(hugeJson, block);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Throws<WorldSaveException>(() => FromBytes(bytes));
            // The size 1024 grid would be 2 GiB.
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 64 << 20, $"the cell grid was allocated for a {block.Length} byte block");
        }
    }

    /// <summary>Several of these would also fail the checksum by accident; the message shows which check caught them.</summary>
    [Theory]
    [InlineData("zlib checksum missing", "incomplete")]
    [InlineData("zlib checksum cut to 1 byte", "incomplete")]
    [InlineData("zlib stream never finished", "incomplete")]
    [InlineData("byte after zlib stream", "unexpected bytes after")]
    [InlineData("second zlib stream", "unexpected bytes after")]
    [InlineData("bad zlib checksum", "checksum")]
    [InlineData("zlib checksum missing, junk in its place", "checksum")]
    [InlineData("zlib preset dictionary", "preset dictionary")]
    [InlineData("zlib wrong method", "not a zlib stream")]
    public void Zlib_framing_problems_are_named(string name, string expected)
    {
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(Damaged[name]()));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Non_finite_player_numbers_are_rejected_on_read()
    {
        // 1e39 parses to float infinity; Write can never produce it, so only the read-side check sees it.
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(Damaged["player position overflows float"]()));
        Assert.Contains("non-finite", error.Message);
    }

    [Fact]
    public void Cells_follow_the_metadata_directly_as_one_zlib_stream()
    {
        // Spec layout: magic, version, i32 length + JSON, then the zlib data with no framing of its own.
        int cellsStart = 10 + BitConverter.ToInt32(Valid, 6);
        using var zlib = new ZLibStream(new MemoryStream(Valid[cellsStart..]), CompressionMode.Decompress);
        var runs = new MemoryStream();
        zlib.CopyTo(runs);
        Assert.Equal(Runs((0, 1), (1, 2), (2, 1), (0, 3), (2, 1)), runs.ToArray());
    }

    [Fact]
    public void Cell_index_is_z_major_then_y_then_x()
    {
        Assert.Equal((3 * 4 + 2) * 4 + 1, WorldSnapshot.CellIndex(4, 1, 2, 3));
        Assert.Equal(1, WorldSnapshot.CellIndex(4, 1, 0, 0));
        Assert.Equal(4, WorldSnapshot.CellIndex(4, 0, 1, 0));
        Assert.Equal(16, WorldSnapshot.CellIndex(4, 0, 0, 1));
    }

    [Fact]
    public void Summary_rejects_damaged_metadata()
    {
        foreach (var name in new[] { "empty file", "wrong magic", "newer version", "json length over cap", "not json", "missing name", "size zero", "palette without air sentinel" })
            Assert.Throws<WorldSaveException>(() => WorldSaveFormat.ReadSummary(new MemoryStream(Damaged[name]())));
    }

    [Fact]
    public void Unknown_metadata_fields_are_ignored()
    {
        var bytes = EditJson(j => j["addedByALaterMinorRelease"] = new JsonObject { ["x"] = 1 });
        Assert.Equal(2, FromBytes(bytes).Size);
    }

    [Fact]
    public void Truncation_at_every_offset_throws_WorldSaveException()
    {
        var bytes = ToBytes(SmallWorld());
        for (int length = 0; length < bytes.Length; length++)
        {
            var prefix = bytes[..length];
            var error = Record.Exception(() => FromBytes(prefix));
            Assert.True(error is WorldSaveException, $"length {length}: {error?.GetType().Name ?? "no exception"}");
            error = Record.Exception(() => WorldSaveFormat.Read(new ForwardOnlyStream(prefix)));
            Assert.True(error is WorldSaveException, $"forward-only, length {length}: {error?.GetType().Name ?? "no exception"}");
        }
    }

    [Fact]
    public void Summary_needs_only_header_and_metadata()
    {
        var bytes = ToBytes(SmallWorld());
        int metadataEnd = 10 + Split(bytes).Json.Length;
        for (int length = 0; length < metadataEnd; length++)
            Assert.Throws<WorldSaveException>(() => WorldSaveFormat.ReadSummary(new MemoryStream(bytes[..length])));
        Assert.Equal("Kosmos Ω \"quoted\" \\ name", WorldSaveFormat.ReadSummary(new MemoryStream(bytes[..metadataEnd])).Name);
    }

    [Fact]
    public void Flipping_any_byte_never_escapes_as_another_exception()
    {
        var bytes = ToBytes(SmallWorld());
        foreach (byte mask in new byte[] { 0xFF, 0x01, 0x80 })
            for (int i = 0; i < bytes.Length; i++)
            {
                var damaged = (byte[])bytes.Clone();
                damaged[i] ^= mask;
                var error = Record.Exception(() => FromBytes(damaged));
                Assert.True(error is null or WorldSaveException, $"byte {i} ^ {mask:X2}: {error}");
            }
    }

    [Fact]
    public void Random_garbage_after_valid_header_throws_WorldSaveException()
    {
        var random = new Random(99);
        for (int i = 0; i < 300; i++)
        {
            var garbage = new byte[random.Next(0, 200)];
            random.NextBytes(garbage);
            byte[] bytes = random.Next(2) == 0
                ? [.. "OCWS"u8, 1, 0, .. garbage]
                : Container(ValidJson, garbage);
            var error = Record.Exception(() => FromBytes(bytes));
            Assert.True(error is WorldSaveException, $"case {i}: {error?.GetType().Name ?? "no exception"}");
        }
    }
}
