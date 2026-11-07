using System.IO.Compression;
using System.Numerics;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Persistence.Nbt;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>
/// Adversarial security, corruption, fuzzing, and boundary test suite for
/// Spacebox structure bridge (.sbs) and pure C# BCL NBT parser.
/// Tests ensure robust failure modes, DoS/OOM resistance, and schema conformance.
/// Uses exclusively programmatic in-memory fixtures.
/// </summary>
public class SpaceboxStructureBridgeSecurityTests
{
    // =========================================================================
    // Category 1: Stream Corruption & Truncation
    // =========================================================================

    [Fact]
    public void Import_ZeroByteStream_ThrowsWorldSaveException()
    {
        using var stream = new MemoryStream([]);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
        Assert.Contains("Corrupt or invalid Spacebox structure NBT stream", ex.Message);
    }

    [Fact]
    public void Import_SingleByteStream_ThrowsWorldSaveException()
    {
        using var stream = new MemoryStream([0x1F]);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(1337)]
    [InlineData(99999)]
    public void Import_RandomJunkBytes_ThrowsWorldSaveException(int seed)
    {
        var random = new Random(seed);
        byte[] junk = new byte[128];
        random.NextBytes(junk);

        using var stream = new MemoryStream(junk);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Fact]
    public void Import_TruncatedGzipHeader_TwoBytesOnly_ThrowsWorldSaveException()
    {
        // Only GZIP magic bytes [0x1F, 0x8B], missing compression method, flags, etc.
        using var stream = new MemoryStream([0x1F, 0x8B]);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Fact]
    public void Import_TruncatedGzipHeader_ThreeBytesOnly_ThrowsWorldSaveException()
    {
        // GZIP magic + DEFLATE method (0x08), missing remaining 7 header bytes
        using var stream = new MemoryStream([0x1F, 0x8B, 0x08]);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Fact]
    public void Import_CorruptedGzipPayload_ThrowsWorldSaveException()
    {
        byte[] validSbs = CreateMinimalValidSbsGzip(1, 1, 1);
        Assert.True(validSbs.Length > 20);

        // Corrupt bytes in the middle of the compressed DEFLATE block
        byte[] corrupted = (byte[])validSbs.Clone();
        for (int i = 10; i < Math.Min(corrupted.Length - 8, 25); i++)
        {
            corrupted[i] ^= 0xFF;
        }

        using var stream = new MemoryStream(corrupted);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Fact]
    public void Import_CorruptedGzipCrc32Footer_ThrowsWorldSaveException()
    {
        byte[] validSbs = CreateMinimalValidSbsGzip(1, 1, 1);
        Assert.True(validSbs.Length >= 18);

        // The last 8 bytes of a GZIP stream are CRC32 (4 bytes) and ISIZE (4 bytes)
        byte[] corruptedFooter = (byte[])validSbs.Clone();
        corruptedFooter[^4] ^= 0xAA;
        corruptedFooter[^3] ^= 0x55;
        corruptedFooter[^2] ^= 0xCC;
        corruptedFooter[^1] ^= 0x33;

        using var stream = new MemoryStream(corruptedFooter);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Fact]
    public void Import_TruncatedNbtPayload_RootNameCutOff_ThrowsWorldSaveException()
    {
        // Uncompressed root compound header (TAG_Compound = 10), string length = 10, but payload truncated
        byte[] truncated = [10, 0, 10, (byte)'H', (byte)'e', (byte)'l']; // only 3 of 10 chars
        using var stream = new MemoryStream(truncated);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Fact]
    public void Import_TruncatedNbtPayload_TagNameCutOff_ThrowsWorldSaveException()
    {
        // Root compound header with tag type TAG_Int (3), name length = 8, but only 3 name bytes provided
        byte[] truncated =
        [
            10, 0, 0, // Root TAG_Compound("")
            3,        // TAG_Int
            0, 8,     // Name length = 8
            (byte)'V', (byte)'e', (byte)'r' // Only 3 of 8 bytes
        ];
        using var stream = new MemoryStream(truncated);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Fact]
    public void Import_TruncatedNbtPayload_PayloadLengthCutOff_ThrowsWorldSaveException()
    {
        // Root compound with TAG_List (9), name "List" (4 bytes), item type TAG_Int (3), but count integer cut off after 2 bytes
        byte[] truncated =
        [
            10, 0, 0, // Root TAG_Compound("")
            9,        // TAG_List
            0, 4, (byte)'L', (byte)'i', (byte)'s', (byte)'t',
            3,        // ItemType = TAG_Int
            0, 0      // Only 2 of 4 bytes for count integer
        ];
        using var stream = new MemoryStream(truncated);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Fact]
    public void Import_TruncatedNbtPayload_ArrayElementsCutOff_ThrowsWorldSaveException()
    {
        // Root compound with TAG_Int_Array (11), name "Arr" (3 bytes), length = 5 (20 payload bytes expected), but stream ends after 6 bytes
        byte[] truncated =
        [
            10, 0, 0, // Root TAG_Compound("")
            11,       // TAG_Int_Array
            0, 3, (byte)'A', (byte)'r', (byte)'r',
            0, 0, 0, 5, // Length = 5 ints (20 bytes)
            0, 0, 0, 1, // 1st int
            0, 0        // 2nd int cut off after 2 bytes
        ];
        using var stream = new MemoryStream(truncated);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(stream));
    }

    [Fact]
    public void Import_TruncatedNbtPayload_BlocksArrayCutOff_ThrowsWorldSaveException()
    {
        // Build valid root uncompressed, then truncate the stream in the middle of Blocks array
        using var ms = new MemoryStream();
        var root = CreateMinimalValidRootCompound(2, 2, 2);
        NbtWriter.WriteRoot(ms, root);
        byte[] fullBytes = ms.ToArray();

        // Truncate stream by 10 bytes from end
        byte[] cutBytes = fullBytes[..^10];
        using var truncatedStream = new MemoryStream(cutBytes);
        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(truncatedStream));
    }

    // =========================================================================
    // Category 2: Malformed NBT Schemas
    // =========================================================================

    [Fact]
    public void Import_MissingSizeTag_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Size", ex.Message);
    }

    [Fact]
    public void Import_MissingPaletteTag_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Palette", ex.Message);
    }

    [Fact]
    public void Import_EmptyPaletteTag_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });
        root.Add(new NbtList("Palette", NbtTagType.Compound)); // Empty palette list
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Palette", ex.Message);
    }

    [Fact]
    public void Import_MissingBlocksTag_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });
        root.Add(CreateValidPaletteList());

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Blocks", ex.Message);
    }

    [Fact]
    public void Import_MissingVersionTag_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        // Omit "Version" tag entirely
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Version", ex.Message);
    }

    [Fact]
    public void Import_VersionTagWrongType_String_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetString("Version", "1.0"); // TAG_String instead of TAG_Int
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Version", ex.Message);
    }

    [Fact]
    public void Import_UnsupportedVersionNumber_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 999); // Unsupported future version
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Version", ex.Message);
    }

    [Fact]
    public void Import_SizeTagWrongType_String_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Size", "1x1x1"); // Wrong tag type: String instead of List
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Size", ex.Message);
    }

    [Fact]
    public void Import_SizeTagWrongType_ListOfStrings_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        var sizeList = new NbtList("Size", NbtTagType.String) { "1", "1", "1" };
        root.Add(sizeList);
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Expected integer tag", ex.Message);
    }

    [Fact]
    public void Import_SizeTagWrongCount_TwoElements_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 10, 20 }); // 2 elements instead of 3
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Size", ex.Message);
    }

    [Fact]
    public void Import_SizeTagWrongCount_FourElements_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 10, 20, 30, 40 }); // 4 elements instead of 3
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Size", ex.Message);
    }

    [Fact]
    public void Import_PaletteTagWrongType_IntArray_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });
        root.SetIntArray("Palette", [0, 11, 23]); // IntArray instead of List of Compounds
        root.SetIntArray("Blocks", [0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Palette", ex.Message);
    }

    [Fact]
    public void Import_BlocksTagWrongType_String_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });
        root.Add(CreateValidPaletteList());
        root.SetString("Blocks", "0,0,0"); // String instead of IntArray/ByteArray

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Blocks", ex.Message);
    }

    [Fact]
    public void Import_BlocksArrayShorterThanDimensions_ThrowsWorldSaveException()
    {
        // 2x2x2 = 8 cells, but Blocks array has only 4 elements
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 2, 2, 2 });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", [0, 0, 0, 0]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Blocks array length", ex.Message);
    }

    [Fact]
    public void Import_BlocksArrayLongerThanDimensions_ThrowsWorldSaveException()
    {
        // 1x1x1 = 1 cell, but Blocks array has 10 elements
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", new int[10]);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Blocks array length", ex.Message);
    }

    // =========================================================================
    // Category 3: Extreme Dimension Boundaries & DoS Prevention
    // =========================================================================

    [Theory]
    [InlineData(-1, 10, 10)]
    [InlineData(10, -5, 10)]
    [InlineData(10, 10, -100)]
    public void Import_NegativeDimensions_ThrowsWorldSaveException(int sx, int sy, int sz)
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", []);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Invalid structure dimensions", ex.Message);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 10, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 10, 0)]
    public void Import_ZeroDimensions_ThrowsWorldSaveException(int sx, int sy, int sz)
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", []);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("Invalid structure dimensions", ex.Message);
    }

    [Fact]
    public void Import_GiantDimensions_10000Cube_RejectsWithoutOom()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 10000, 10000, 10000 });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", []);

        using var ms = CompressRoot(root);
        long memBefore = GC.GetTotalMemory(true);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        long memAfter = GC.GetTotalMemory(false);

        Assert.Contains("exceed maximum supported size", ex.Message);
        // Verify no massive allocation took place (must be < 10 MB delta)
        Assert.True(Math.Abs(memAfter - memBefore) < 10 * 1024 * 1024);
    }

    [Fact]
    public void Import_GiantDimensions_Exceeding512Boundary_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 513, 1, 1 });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", []);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("exceed maximum supported size", ex.Message);
    }

    [Fact]
    public void Import_ExtremeDimensions_IntMaxValue_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { int.MaxValue, 1, 1 });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", []);

        using var ms = CompressRoot(root);
        var ex = Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
        Assert.Contains("exceed maximum supported size", ex.Message);
    }

    // =========================================================================
    // Category 4: Uncompressed vs GZIP Streams
    // =========================================================================

    [Fact]
    public void Import_UncompressedNbtStream_SuccessfullyImportsStructure()
    {
        var root = CreateMinimalValidRootCompound(2, 2, 2);
        // Mark voxel (1, 1, 1) as solid steel hull (palette index 1)
        var blocks = new int[8];
        blocks[(1 * 2 + 1) * 2 + 1] = 1;
        root.SetIntArray("Blocks", blocks);

        using var rawMs = new MemoryStream();
        NbtWriter.WriteRoot(rawMs, root);
        rawMs.Position = 0;

        var ship = SpaceboxStructureBridge.Import(rawMs, "UncompressedProbe");

        Assert.NotNull(ship);
        Assert.Equal(16, ship.Size);
        ushort solidCell = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 1, 1, 1)];
        Assert.True(solidCell > 0);
    }

    [Fact]
    public void Import_UncompressedNbtStream_WithTurnedOrientationsAndConsoles_Preserved()
    {
        var root = CreateMinimalValidRootCompound(2, 2, 2);
        var blocks = new int[8];
        blocks[0] = 1; // cockpit
        blocks[1] = 2; // thruster
        root.SetIntArray("Blocks", blocks);

        var orientations = new byte[8];
        orientations[0] = 5; // Turned orientation 5
        root.SetByteArray("Orientations", orientations);

        var consolesList = new NbtList("Consoles", NbtTagType.Compound);
        var consoleCompound = new NbtCompound();
        consoleCompound.Add(new NbtList("Cell", NbtTagType.Int) { 0, 0, 0 });
        consolesList.Add(consoleCompound);
        root.Add(consolesList);

        using var rawMs = new MemoryStream();
        NbtWriter.WriteRoot(rawMs, root);
        rawMs.Position = 0;

        var ship = SpaceboxStructureBridge.Import(rawMs);

        Assert.NotNull(ship);
        Assert.Single(ship.Turned);
        Assert.Equal(5, ship.Turned[0].Orientation);
        Assert.Single(ship.Consoles);
        Assert.Equal(new Int3(0, 0, 0), ship.Consoles[0].Cell);
    }

    // =========================================================================
    // Category 5: Fuzzing & Mutation Havoc Test
    // =========================================================================

    [Fact]
    public void Import_FuzzedBitFlips_NeverThrowsUnhandledException()
    {
        byte[] validSbs = CreateMinimalValidSbsGzip(2, 2, 2);
        var rng = new Random(1337);

        for (int iteration = 0; iteration < 50; iteration++)
        {
            byte[] mutated = (byte[])validSbs.Clone();
            int mutations = rng.Next(1, 5);

            for (int m = 0; m < mutations; m++)
            {
                int offset = rng.Next(mutated.Length);
                mutated[offset] ^= (byte)(1 << rng.Next(8));
            }

            using var stream = new MemoryStream(mutated);
            try
            {
                var ship = SpaceboxStructureBridge.Import(stream);
                // If it miraculously survived mutation, ship snapshot must be non-null and valid
                Assert.NotNull(ship);
                Assert.True(ship.Size >= 16);
            }
            catch (WorldSaveException)
            {
                // Expected, clean rejection of corrupt stream
            }
            catch (Exception ex)
            {
                Assert.Fail($"Unhandled exception of type {ex.GetType().FullName} during fuzz iteration {iteration}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void Bridge_NullStreamAndNullShip_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SpaceboxStructureBridge.Import(null!));

        var validShip = Persistence.SaveFixtures.Ship(size: 16);
        using var validStream = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() => SpaceboxStructureBridge.Export(null!, validStream));
        Assert.Throws<ArgumentNullException>(() => SpaceboxStructureBridge.Export(validShip, null!));
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private static NbtCompound CreateMinimalValidRootCompound(int sx, int sy, int sz)
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "MinimalStructure");
        root.SetString("Author", "TestArchitect");
        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });
        root.Add(CreateValidPaletteList());
        root.SetIntArray("Blocks", new int[sx * sy * sz]);
        root.SetByteArray("Orientations", new byte[sx * sy * sz]);
        return root;
    }

    private static NbtList CreateValidPaletteList()
    {
        var palette = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound();
        air.SetString("Name", "default:air");
        palette.Add(air);

        var hull = new NbtCompound();
        hull.SetString("Name", "default:steel_hull");
        palette.Add(hull);

        var thruster = new NbtCompound();
        thruster.SetString("Name", "default:thruster");
        palette.Add(thruster);

        return palette;
    }

    private static MemoryStream CompressRoot(NbtCompound root)
    {
        var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;
        return ms;
    }

    private static byte[] CreateMinimalValidSbsGzip(int sx, int sy, int sz)
    {
        var root = CreateMinimalValidRootCompound(sx, sy, sz);
        using var ms = CompressRoot(root);
        return ms.ToArray();
    }
}
