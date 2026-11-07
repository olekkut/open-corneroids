using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Persistence.Nbt;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.E2E;

/// <summary>
/// Tier 5: White-Box Adversarial Coverage Hardening Suite for Milestone 3.
/// Audits and stress-tests:
/// 1. Pure BCL NBT binary parser and writer (NbtReader, NbtWriter, NbtCompound, NbtList, all tag types).
/// 2. Spacebox structure bridge (SpaceboxStructureBridge) import and export.
/// Uses exclusively in-memory streams (MemoryStream, GZipStream) to ensure zero disk side effects.
/// </summary>
public class Tier5_SbsAdversarialTests
{
    // =========================================================================
    // Category 1: Pure BCL NBT Tag Stress & Boundary Conditions
    // =========================================================================

    [Fact]
    public void NbtTag_LongArray_Empty_Extremes_And_Roundtrip()
    {
        var root = new NbtCompound("LongArrayRoot");

        // 1. Empty long array
        root.SetLongArray("EmptyLongs", []);

        // 2. Extremes: MinValue, -1, 0, 1, MaxValue, arbitrary bitmasks
        long[] extremeValues =
        [
            long.MinValue,
            -1L,
            0L,
            1L,
            long.MaxValue,
            0x0123456789ABCDEFL,
            unchecked((long)0xFEDCBA9876543210UL)
        ];
        root.SetLongArray("ExtremeLongs", extremeValues);

        // 3. Moderately large array
        long[] largeArray = new long[2048];
        for (int i = 0; i < largeArray.Length; i++)
        {
            largeArray[i] = (long)i * 1_000_000_007L;
        }
        root.SetLongArray("LargeLongs", largeArray);

        // Roundtrip via uncompressed stream
        using var msUncompressed = new MemoryStream();
        NbtWriter.WriteRoot(msUncompressed, root, "LongArrayRoot");
        msUncompressed.Position = 0;

        var readUncompressed = NbtReader.ReadRootUncompressed(msUncompressed);
        Assert.Equal(new long[0], readUncompressed.GetLongArray("EmptyLongs"));
        Assert.Equal(extremeValues, readUncompressed.GetLongArray("ExtremeLongs"));
        Assert.Equal(largeArray, readUncompressed.GetLongArray("LargeLongs"));

        // Roundtrip via GZIP stream
        using var msGzip = new MemoryStream();
        NbtWriter.WriteRootToGzip(msGzip, root, "LongArrayRoot");
        msGzip.Position = 0;

        var readGzip = NbtReader.ReadRoot(msGzip);
        Assert.Equal(extremeValues, readGzip.GetLongArray("ExtremeLongs"));
        Assert.Equal(largeArray, readGzip.GetLongArray("LargeLongs"));
    }

    [Fact]
    public void NbtTag_LongArray_NegativeLength_ThrowsInvalidDataException()
    {
        // Construct binary payload with TAG_Long_Array and length -1 (0xFFFFFFFF)
        using var ms = new MemoryStream();
        ms.WriteByte((byte)NbtTagType.Compound);
        WriteNbtString(ms, "Root");
        ms.WriteByte((byte)NbtTagType.LongArray);
        WriteNbtString(ms, "CorruptArray");

        // Length = -1 in Big-Endian
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, -1);
        ms.Write(len);

        ms.WriteByte((byte)NbtTagType.End);
        ms.Position = 0;

        Assert.Throws<InvalidDataException>(() => NbtReader.ReadRootUncompressed(ms));
    }

    [Fact]
    public void NbtTag_IntArray_NegativeLength_ThrowsInvalidDataException()
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)NbtTagType.Compound);
        WriteNbtString(ms, "Root");
        ms.WriteByte((byte)NbtTagType.IntArray);
        WriteNbtString(ms, "CorruptArray");

        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, -5);
        ms.Write(len);

        ms.WriteByte((byte)NbtTagType.End);
        ms.Position = 0;

        Assert.Throws<InvalidDataException>(() => NbtReader.ReadRootUncompressed(ms));
    }

    [Fact]
    public void NbtTag_ByteArray_NegativeLength_ThrowsInvalidDataException()
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)NbtTagType.Compound);
        WriteNbtString(ms, "Root");
        ms.WriteByte((byte)NbtTagType.ByteArray);
        WriteNbtString(ms, "CorruptArray");

        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, -100);
        ms.Write(len);

        ms.WriteByte((byte)NbtTagType.End);
        ms.Position = 0;

        Assert.Throws<InvalidDataException>(() => NbtReader.ReadRootUncompressed(ms));
    }

    [Fact]
    public void NbtList_Empty_EndType_And_TypedEmpty_Roundtrip()
    {
        var root = new NbtCompound("ListRoot");

        // 1. Untyped empty list (ItemType = End)
        var untypedEmpty = new NbtList("UntypedEmpty", NbtTagType.End);
        root.Add(untypedEmpty);

        // 2. Typed empty lists
        var emptyIntList = new NbtList("EmptyInts", NbtTagType.Int);
        root.Add(emptyIntList);

        var emptyCompoundList = new NbtList("EmptyCompounds", NbtTagType.Compound);
        root.Add(emptyCompoundList);

        var emptyStringList = new NbtList("EmptyStrings", NbtTagType.String);
        root.Add(emptyStringList);

        using var ms = new MemoryStream();
        NbtWriter.WriteRoot(ms, root);
        ms.Position = 0;

        var read = NbtReader.ReadRootUncompressed(ms);

        var rUntyped = read.GetList("UntypedEmpty");
        Assert.NotNull(rUntyped);
        Assert.Empty(rUntyped);
        Assert.Equal(NbtTagType.End, rUntyped.ItemType);

        var rInts = read.GetList("EmptyInts");
        Assert.NotNull(rInts);
        Assert.Empty(rInts);
        Assert.Equal(NbtTagType.Int, rInts.ItemType);

        var rCompounds = read.GetList("EmptyCompounds");
        Assert.NotNull(rCompounds);
        Assert.Empty(rCompounds);
        Assert.Equal(NbtTagType.Compound, rCompounds.ItemType);
    }

    [Fact]
    public void NbtList_Nested_ListOfLists_MultiLevel()
    {
        var root = new NbtCompound("MatrixRoot");

        // Outer list containing inner lists of integers
        var matrix = new NbtList("Matrix", NbtTagType.List);

        var row0 = new NbtList(NbtTagType.Int) { new NbtInt(10), new NbtInt(20), new NbtInt(30) };
        var row1 = new NbtList(NbtTagType.Int) { new NbtInt(40), new NbtInt(50), new NbtInt(60) };
        var row2 = new NbtList(NbtTagType.Int) { new NbtInt(70), new NbtInt(80), new NbtInt(90) };

        matrix.Add(row0);
        matrix.Add(row1);
        matrix.Add(row2);
        root.Add(matrix);

        // Deeply nested list (3 levels)
        var outer3D = new NbtList("Tensor3D", NbtTagType.List);
        var plane0 = new NbtList(NbtTagType.List);
        plane0.Add(new NbtList(NbtTagType.String) { new NbtString("cell_0_0_0"), new NbtString("cell_0_0_1") });
        outer3D.Add(plane0);
        root.Add(outer3D);

        using var ms = new MemoryStream();
        NbtWriter.WriteRoot(ms, root);
        ms.Position = 0;

        var read = NbtReader.ReadRootUncompressed(ms);
        var rMatrix = read.GetList("Matrix");
        Assert.NotNull(rMatrix);
        Assert.Equal(3, rMatrix.Count);
        Assert.Equal(NbtTagType.List, rMatrix.ItemType);

        var rRow1 = (NbtList)rMatrix[1];
        Assert.Equal(3, rRow1.Count);
        Assert.Equal(50, ((NbtInt)rRow1[1]).Value);

        var rTensor = read.GetList("Tensor3D");
        Assert.NotNull(rTensor);
        var rPlane0 = (NbtList)rTensor[0];
        var rLine0 = (NbtList)rPlane0[0];
        Assert.Equal("cell_0_0_1", ((NbtString)rLine0[1]).Value);
    }

    [Fact]
    public void NbtList_TypeEnforcement_MismatchedTagType_ThrowsArgumentException()
    {
        var list = new NbtList("StrictIntList", NbtTagType.Int)
        {
            new NbtInt(100)
        };

        // Cannot add a string to an int list
        Assert.Throws<ArgumentException>(() => list.Add(new NbtString("invalid")));

        // Cannot set index with wrong type
        Assert.Throws<ArgumentException>(() => list[0] = new NbtFloat(1.5f));

        // Cannot insert with wrong type
        Assert.Throws<ArgumentException>(() => list.Insert(0, new NbtByte(2)));

        // Unset list initializes type from first addition
        var autoList = new NbtList { Name = "AutoTyped" };
        Assert.Equal(NbtTagType.End, autoList.ItemType);
        autoList.Add(new NbtDouble(3.14));
        Assert.Equal(NbtTagType.Double, autoList.ItemType);
        Assert.Throws<ArgumentException>(() => autoList.Add(new NbtInt(5)));
    }

    [Fact]
    public void NbtString_ExactMaxBoundary_65535Bytes_Roundtrip()
    {
        // 65535 bytes is the exact maximum length representable by ushort length prefix in NBT
        string largeAsciiString = new string('A', ushort.MaxValue);
        Assert.Equal(65535, Encoding.UTF8.GetByteCount(largeAsciiString));

        var root = new NbtCompound("StringBoundaryRoot");
        root.SetString("MaxString", largeAsciiString);

        using var ms = new MemoryStream();
        NbtWriter.WriteRoot(ms, root);
        ms.Position = 0;

        var read = NbtReader.ReadRootUncompressed(ms);
        string retrieved = read.GetString("MaxString");
        Assert.Equal(ushort.MaxValue, retrieved.Length);
        Assert.Equal(largeAsciiString, retrieved);
    }

    [Fact]
    public void NbtString_ExceedingMaxBoundary_65536Bytes_ThrowsInvalidOperationException()
    {
        string oversizedString = new string('B', 65536);
        Assert.True(Encoding.UTF8.GetByteCount(oversizedString) > ushort.MaxValue);

        var root = new NbtCompound("OversizedRoot");
        root.SetString("HugeString", oversizedString);

        using var ms = new MemoryStream();
        var ex = Assert.Throws<InvalidOperationException>(() => NbtWriter.WriteRoot(ms, root));
        Assert.Contains("exceeds maximum NBT string length", ex.Message);
    }

    [Fact]
    public void NbtString_Utf8_MultiByte_Cyrillic_CJK_Emojis_Surrogates()
    {
        var root = new NbtCompound("MultilingualRoot");

        // Cyrillic (2 bytes per char)
        string cyrillic = "ПЕРЕЯСЛАВ 2026-фінал — Симуляція Зорельоту #1";
        root.SetString("УкрНазва", cyrillic);

        // CJK (3 bytes per char)
        string cjk = "宇宙ステーション戦闘艦 日本語テスト";
        root.SetString("艦種", cjk);

        // Emojis / 4-byte UTF-8 surrogate pairs
        string emojis = "🚀🌌🛸🛰️🪐✨";
        root.SetString("EmojiPayload", emojis);

        // Mixed with punctuation and whitespace
        string mixed = "  Tab\tNewline\r\nQuotes \"' Slashing \\//  ";
        root.SetString("MixedEscapes", mixed);

        using var ms = new MemoryStream();
        NbtWriter.WriteRoot(ms, root);
        ms.Position = 0;

        var read = NbtReader.ReadRootUncompressed(ms);
        Assert.Equal(cyrillic, read.GetString("УкрНазва"));
        Assert.Equal(cjk, read.GetString("艦種"));
        Assert.Equal(emojis, read.GetString("EmojiPayload"));
        Assert.Equal(mixed, read.GetString("MixedEscapes"));
    }

    [Fact]
    public void NbtCompound_CaseInsensitivity_And_DictionaryOps()
    {
        var compound = new NbtCompound("CaseComp");
        compound.SetInt("ShieldPower", 100);

        // Case-insensitive lookup
        Assert.True(compound.Contains("shieldpower"));
        Assert.True(compound.ContainsKey("SHIELDPOWER"));
        Assert.Equal(100, compound.GetInt("shieldpower"));
        Assert.Equal(100, compound.GetInt("SHIELDPOWER"));

        // Indexer updates tag name
        compound["ThrusterPower"] = new NbtInt("OldTag", 250);
        Assert.Equal("ThrusterPower", compound["ThrusterPower"].Name);
        Assert.Equal(250, compound.GetInt("ThrusterPower"));

        // Type-safe Get<T> with mismatch returns null, primitive Get returns default
        Assert.Null(compound.Get<NbtString>("ShieldPower"));
        Assert.Equal("fallback", compound.GetString("ShieldPower", "fallback"));
        Assert.Equal(999, compound.GetInt("NonExistent", 999));

        // Case-insensitive removal
        Assert.True(compound.Remove("SHIELDPOWER"));
        Assert.False(compound.Contains("shieldpower"));
    }

    [Fact]
    public void NbtTag_ToString_AllTypes_DoNotThrowNullReference()
    {
        NbtTag[] tags =
        [
            new NbtByte("b", 1),
            new NbtShort("s", 2),
            new NbtInt("i", 3),
            new NbtLong("l", 4),
            new NbtFloat("f", 5.0f),
            new NbtDouble("d", 6.0),
            new NbtByteArray("ba", [1, 2]),
            new NbtString("str", "test"),
            new NbtIntArray("ia", [10, 20]),
            new NbtLongArray("la", [100L, 200L]),
            new NbtList("lst", NbtTagType.Int),
            new NbtCompound("cmp")
        ];

        foreach (var tag in tags)
        {
            string? s = tag.ToString();
            Assert.NotNull(s);
            Assert.False(string.IsNullOrWhiteSpace(s));
            Assert.Contains(tag.Name, s);
        }
    }

    // =========================================================================
    // Category 2: Spacebox SBS Import Edge Cases & Fault Tolerance
    // =========================================================================

    [Fact]
    public void SbsImport_EmptyStructure_AllAir_Valid1x1x1_ProducesValidEmptyShip()
    {
        // 1x1x1 SBS file with only air
        byte[] sbsBytes = SbsFixtures.CreateSbsArchive(
            name: "EmptyProbe",
            author: "Tester",
            sizeX: 1,
            sizeY: 1,
            sizeZ: 1,
            palette: ["default:air"],
            blocks: [0],
            orientations: [0]);

        using var ms = new MemoryStream(sbsBytes);
        var ship = SpaceboxStructureBridge.Import(ms, "FallbackName");

        Assert.NotNull(ship);
        Assert.Equal(16, ship.Size); // Smallest enclosing power of 2 >= 1 is 16
        Assert.Contains(WorldSnapshot.AirBlockId, ship.Palette);
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)]);
        Assert.Empty(ship.Turned);
        Assert.Empty(ship.Consoles);
    }

    [Fact]
    public void SbsImport_StructuresWithOnlyAir_MultiBlock_4x4x4()
    {
        int sx = 4, sy = 4, sz = 4;
        int total = sx * sy * sz;
        byte[] sbsBytes = SbsFixtures.CreateSbsArchive(
            name: "GhostHull",
            author: "Tester",
            sizeX: sx,
            sizeY: sy,
            sizeZ: sz,
            palette: ["default:air", "default:steel_hull"],
            blocks: new int[total], // all 0 (air)
            orientations: new byte[total]);

        using var ms = new MemoryStream(sbsBytes);
        var ship = SpaceboxStructureBridge.Import(ms);

        Assert.Equal(16, ship.Size);
        // Verify every cell in the active region is air (0)
        for (int z = 0; z < sz; z++)
        {
            for (int y = 0; y < sy; y++)
            {
                for (int x = 0; x < sx; x++)
                {
                    Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, x, y, z)]);
                }
            }
        }
    }

    [Fact]
    public void SbsImport_PaletteWithoutAirBlock_InsertsAirBlockAtPaletteZero()
    {
        // Construct an SBS where palette index 0 is NOT air (e.g. steel_hull)
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "NoAirInPalette");
        root.SetString("Author", "TestArchitect");
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var hull = new NbtCompound();
        hull.SetString("Name", "default:steel_hull");
        palette.Add(hull);
        root.Add(palette);

        root.SetIntArray("Blocks", [0]); // references palette[0] = steel_hull
        root.SetByteArray("Orientations", [0]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // OpenCorneroids domain invariant: palette[0] MUST be AirBlockId (-1)
        Assert.Equal(WorldSnapshot.AirBlockId, ship.Palette[0]);

        // Steel hull should be added to palette at index 1
        Assert.Equal(11, ship.Palette[1]);

        // Cell (0, 0, 0) should reference palette index 1 (steel hull)
        ushort cellVal = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)];
        Assert.Equal(1, cellVal);
        Assert.Equal(11, ship.Palette[cellVal]);
    }

    [Fact]
    public void SbsImport_PaletteAsListOfStrings_InsteadOfCompounds()
    {
        // Spacebox tools occasionally save Palette as TAG_List of TAG_String
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "StringPaletteShip");
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 2 });

        var palette = new NbtList("Palette", NbtTagType.String)
        {
            new NbtString("default:air"),
            new NbtString("default:thruster")
        };
        root.Add(palette);

        root.SetIntArray("Blocks", [0, 1]); // air, thruster
        root.SetByteArray("Orientations", [0, 0]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.NotNull(ship);
        Assert.Contains(28, ship.Palette); // 28 is Thruster
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)]); // Air
        ushort c1 = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 1)];
        Assert.True(c1 > 0);
        Assert.Equal(28, ship.Palette[c1]);
    }

    [Fact]
    public void SbsImport_Consoles_WithoutGroups_ImportsGracefully()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetString("Name", "NoGroupsConsoleShip");
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var cockpit = new NbtCompound(); cockpit.SetString("Name", "default:cockpit"); palette.Add(cockpit);
        root.Add(palette);
        root.SetIntArray("Blocks", [0]);

        // Console compound without "Groups" tag
        var consoles = new NbtList("Consoles", NbtTagType.Compound);
        var c1 = new NbtCompound();
        c1.Add(new NbtList("Cell", NbtTagType.Int) { 0, 0, 0 });
        consoles.Add(c1);
        root.Add(consoles);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.Single(ship.Consoles);
        Assert.Equal(new Int3(0, 0, 0), ship.Consoles[0].Cell);
        Assert.Empty(ship.Consoles[0].Groups);
        Assert.Null(ship.Consoles[0].Camera);
    }

    [Fact]
    public void SbsImport_Consoles_WithGroupsWithoutTargets_ImportsGracefully()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var cockpit = new NbtCompound(); cockpit.SetString("Name", "default:cockpit"); palette.Add(cockpit);
        root.Add(palette);
        root.SetIntArray("Blocks", [0]);

        // Console with a group lacking "Targets"
        var consoles = new NbtList("Consoles", NbtTagType.Compound);
        var c1 = new NbtCompound();
        c1.Add(new NbtList("Cell", NbtTagType.Int) { 0, 0, 0 });

        var groups = new NbtList("Groups", NbtTagType.Compound);
        var g1 = new NbtCompound();
        g1.SetInt("Index", 0);
        g1.SetString("Key", "Space");
        // No "Targets" tag
        groups.Add(g1);
        c1.Add(groups);
        consoles.Add(c1);
        root.Add(consoles);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.Single(ship.Consoles);
        var g = Assert.Single(ship.Consoles[0].Groups);
        Assert.Equal("Space", g.Key);
        Assert.Empty(g.Targets);
    }

    [Fact]
    public void SbsImport_Consoles_TargetsAsIntArray_RatherThanList()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 2, 2, 2 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var cockpit = new NbtCompound(); cockpit.SetString("Name", "default:cockpit"); palette.Add(cockpit);
        root.Add(palette);
        root.SetIntArray("Blocks", new int[8]);

        var consoles = new NbtList("Consoles", NbtTagType.Compound);
        var c1 = new NbtCompound();
        c1.Add(new NbtList("Cell", NbtTagType.Int) { 0, 0, 0 });

        var groups = new NbtList("Groups", NbtTagType.Compound);
        var g1 = new NbtCompound();
        g1.SetInt("Index", 0);
        g1.SetString("Key", "W");

        // Targets list containing NbtIntArray instead of NbtList
        var targets = new NbtList("Targets", NbtTagType.IntArray)
        {
            new NbtIntArray("", [1, 0, 1]),
            new NbtIntArray("", [0, 1, 1])
        };
        g1.Add(targets);
        groups.Add(g1);
        c1.Add(groups);
        consoles.Add(c1);
        root.Add(consoles);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.Single(ship.Consoles);
        var g = Assert.Single(ship.Consoles[0].Groups);
        Assert.Equal(2, g.Targets.Count);
        Assert.Equal(new Int3(1, 0, 1), g.Targets[0]);
        Assert.Equal(new Int3(0, 1, 1), g.Targets[1]);
    }

    [Fact]
    public void SbsImport_PaletteHeuristics_SpecialCharacters_Whitespace_Punctuation()
    {
        // Exhaustive test of heuristic palette resolver branches
        string[] testNames =
        [
            "  default:stone  ",
            "mod:warp_engine_mk2",
            "weapons:plasma_cannon#9",
            "cockpit_primary",
            "reinforced_glass_window",
            "ceiling_lamp_bright",
            "fusion_power_reactor",
            "custom:block_555",
            "ns:pure_air",
            "unknown_mod:alien_device!@#$"
        ];

        int[] expectedIds =
        [
            0,   // stone
            28,  // engine -> thruster
            30,  // cannon -> weapon
            23,  // cockpit
            15,  // glass
            16,  // lamp -> light
            20,  // reactor
            555, // block_555
            WorldSnapshot.AirBlockId, // air
            11   // unknown fallback -> steel hull
        ];

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { testNames.Length, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        for (int i = 0; i < testNames.Length; i++)
        {
            var entry = new NbtCompound();
            entry.SetString("Name", testNames[i]);
            palette.Add(entry);
        }
        root.Add(palette);

        int[] blockIndices = new int[testNames.Length];
        for (int i = 0; i < testNames.Length; i++) blockIndices[i] = i;
        root.SetIntArray("Blocks", blockIndices);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        for (int i = 0; i < testNames.Length; i++)
        {
            ushort cellVal = ship.Cells[WorldSnapshot.CellIndex(ship.Size, i, 0, 0)];
            int resolvedId = ship.Palette[cellVal];
            Assert.Equal(expectedIds[i], resolvedId);
        }
    }

    [Fact]
    public void Adversarial_ExposesBug_NaiveAirSubstringMatch_ErasesSolidBlocksToVacuumAir()
    {
        // Bug 1: When Spacebox structures contain blocks whose local names contain "air" as a substring
        // (such as "airlock", "pilot_chair", "hull_repair_station", "metal_stairs"),
        // SpaceboxStructureBridge.ResolveBlockNameToId naively evaluates:
        //   if (localName.Contains("air", StringComparison.OrdinalIgnoreCase)) return WorldSnapshot.AirBlockId;
        // This causes solid blocks to be converted into empty vacuum air (ID -1), creating holes in ships.
        string[] solidBlocksWithAirSubstring =
        [
            "station:docking_airlock",
            "cockpit:pilot_chair",
            "mechanic:hull_repair_station",
            "corridor:metal_stairs"
        ];

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { solidBlocksWithAirSubstring.Length, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        for (int i = 0; i < solidBlocksWithAirSubstring.Length; i++)
        {
            var entry = new NbtCompound();
            entry.SetString("Name", solidBlocksWithAirSubstring[i]);
            palette.Add(entry);
        }
        root.Add(palette);

        int[] blockIndices = new int[solidBlocksWithAirSubstring.Length];
        for (int i = 0; i < solidBlocksWithAirSubstring.Length; i++) blockIndices[i] = i;
        root.SetIntArray("Blocks", blockIndices);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // Assert that none of these solid ship blocks are turned into vacuum air (AirBlockId = -1)
        for (int i = 0; i < solidBlocksWithAirSubstring.Length; i++)
        {
            ushort cellVal = ship.Cells[WorldSnapshot.CellIndex(ship.Size, i, 0, 0)];
            int resolvedId = ship.Palette[cellVal];

            // Expected: Solid block ID (should NOT be AirBlockId (-1))
            // Actual (Bug in SpaceboxStructureBridge.cs:630): Erased to -1
            Assert.NotEqual(WorldSnapshot.AirBlockId, resolvedId);
        }
    }

    [Fact]
    public void Adversarial_ExposesBug_DoorAndCargoHeuristics_MissingFromCategoryMatching()
    {
        // Bug 2: SpaceboxStructureBridge has dictionary entries for "door" (25) and "cargo"/"container" (26),
        // but its heuristic category matching in ResolveBlockNameToId omits "door", "cargo", and "container".
        // Consequently, namespaced/descriptive door and cargo blocks (e.g. "mod:heavy_door", "freighter:cargo_bay")
        // fail to resolve to 25 and 26, falling back to 11 (steel hull).
        string[] doorAndCargoBlocks =
        [
            "mod:heavy_door",
            "freighter:cargo_bay",
            "station:storage_container"
        ];

        int[] expectedIds = [25, 26, 26];

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { doorAndCargoBlocks.Length, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        for (int i = 0; i < doorAndCargoBlocks.Length; i++)
        {
            var entry = new NbtCompound();
            entry.SetString("Name", doorAndCargoBlocks[i]);
            palette.Add(entry);
        }
        root.Add(palette);

        int[] blockIndices = new int[doorAndCargoBlocks.Length];
        for (int i = 0; i < doorAndCargoBlocks.Length; i++) blockIndices[i] = i;
        root.SetIntArray("Blocks", blockIndices);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        for (int i = 0; i < doorAndCargoBlocks.Length; i++)
        {
            ushort cellVal = ship.Cells[WorldSnapshot.CellIndex(ship.Size, i, 0, 0)];
            int resolvedId = ship.Palette[cellVal];

            // Expected: 25 for door, 26 for cargo/container
            // Actual (Bug in SpaceboxStructureBridge.cs:630-651): Falls back to 11 (steel hull)
            Assert.Equal(expectedIds[i], resolvedId);
        }
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(15, 16)]
    [InlineData(16, 16)]
    [InlineData(17, 32)]
    [InlineData(32, 32)]
    [InlineData(33, 64)]
    [InlineData(64, 64)]
    [InlineData(65, 128)]
    [InlineData(128, 128)]
    [InlineData(129, 256)]
    [InlineData(256, 256)]
    [InlineData(257, 512)]
    [InlineData(512, 512)]
    public void SbsImport_NonPowerOfTwo_EnclosingPowerOfTwoSteps(int maxDim, int expectedSize)
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { maxDim, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); palette.Add(hull);
        root.Add(palette);

        root.SetIntArray("Blocks", new int[maxDim]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.Equal(expectedSize, ship.Size);
    }

    [Fact]
    public void SbsImport_DimensionExceeding512_ThrowsWorldSaveException()
    {
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 513, 1, 1 });

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        Assert.Throws<WorldSaveException>(() => SpaceboxStructureBridge.Import(ms));
    }

    [Fact]
    public void SbsImport_ZMajor_StrideAccommodation_WhenSyNotEqualSz()
    {
        // When sy != sz, test automatic detection of Z-major vs Y-major stride ordering
        int sx = 2, sy = 3, sz = 4;
        int total = sx * sy * sz;

        // Suppose console is at cx = 1, cy = 2, cz = 0
        // idxY = (cy * sz + cz) * sx + cx = (2 * 4 + 0) * 2 + 1 = 17
        // idxZ = (cz * sy + cy) * sx + cx = (0 * 3 + 2) * 2 + 1 = 5
        var blocks = new int[total];
        blocks[5] = 1;  // Solid block at Z-major index 5 (idxZ)
        blocks[17] = 0; // Air block at Y-major index 17 (idxY)

        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { sx, sy, sz });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); palette.Add(air);
        var cockpit = new NbtCompound(); cockpit.SetString("Name", "default:cockpit"); palette.Add(cockpit);
        root.Add(palette);

        root.SetIntArray("Blocks", blocks);

        // Add console pointing to (1, 2, 0)
        var consoles = new NbtList("Consoles", NbtTagType.Compound);
        var c1 = new NbtCompound();
        c1.Add(new NbtList("Cell", NbtTagType.Int) { 1, 2, 0 });
        consoles.Add(c1);
        root.Add(consoles);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // The bridge should detect that idxY is air while idxZ is solid, flipping useZMajor = true
        ushort cellVal = ship.Cells[WorldSnapshot.CellIndex(ship.Size, 1, 2, 0)];
        Assert.True(cellVal > 0, "Cell (1, 2, 0) should be solid via Z-major stride detection.");
        Assert.Equal(23, ship.Palette[cellVal]);
    }

    [Fact]
    public void SbsImport_BlocksAsByteArray_DecodesCorrectly()
    {
        byte[] sbsBlocks = [0, 1, 0, 1]; // 4 cells: air, solid, air, solid
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 2, 2, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var air = new NbtCompound(); air.SetString("Name", "default:air"); palette.Add(air);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); palette.Add(hull);
        root.Add(palette);

        // Blocks formatted as TAG_Byte_Array
        root.SetByteArray("Blocks", sbsBlocks);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.Equal(0, ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)]);
        Assert.True(ship.Cells[WorldSnapshot.CellIndex(ship.Size, 1, 0, 0)] > 0);
    }

    [Fact]
    public void SbsImport_QuarterTurnOrientations_HighBytes_ResolvedViaChiralMath()
    {
        // Orientations with bytes >= 24 (quarter turns)
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); palette.Add(hull);
        root.Add(palette);

        root.SetIntArray("Blocks", [0]);

        // Quarter turn byte: yaw=1, pitch=2, roll=1 -> 1 | (2 << 2) | (1 << 4) = 1 | 8 | 16 = 25
        byte quarterTurnByte = 25;
        root.SetByteArray("Orientations", [quarterTurnByte]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRootToGzip(ms, root);
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);

        // FromByte(25) maps to an orientation in 0..23
        int expectedOrient = CorneroidsOrientation.FromByte(quarterTurnByte);
        if (expectedOrient > 0)
        {
            var tc = Assert.Single(ship.Turned);
            Assert.Equal(new Int3(0, 0, 0), tc.Cell);
            Assert.Equal(expectedOrient, tc.Orientation);
        }
    }

    [Fact]
    public void SbsImport_UncompressedStream_HandledTransparently()
    {
        // Uncompressed NBT stream (starts with 0x0A rather than GZIP magic 0x1F, 0x8B)
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.Add(new NbtList("Size", NbtTagType.Int) { 1, 1, 1 });

        var palette = new NbtList("Palette", NbtTagType.Compound);
        var hull = new NbtCompound(); hull.SetString("Name", "default:steel_hull"); palette.Add(hull);
        root.Add(palette);
        root.SetIntArray("Blocks", [0]);

        using var ms = new MemoryStream();
        NbtWriter.WriteRoot(ms, root); // Raw uncompressed NBT
        ms.Position = 0;

        var ship = SpaceboxStructureBridge.Import(ms);
        Assert.NotNull(ship);
        Assert.Equal(16, ship.Size);
        Assert.True(ship.Cells[WorldSnapshot.CellIndex(ship.Size, 0, 0, 0)] > 0);
    }

    // =========================================================================
    // Category 3: Spacebox SBS Export Edge Cases & Boundary Conditions
    // =========================================================================

    [Fact]
    public void SbsExport_SingleVoxel_AtMaxCorner_NormalizedTo1x1x1()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        var palette = new List<int> { WorldSnapshot.AirBlockId, 11 };

        // Single solid block at the extreme corner (15, 15, 15)
        cells[WorldSnapshot.CellIndex(size, 15, 15, 15)] = 1;

        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells);

        using var sbsMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, sbsMs, "CornerArchitect");
        sbsMs.Position = 0;

        // Inspect exported NBT
        var exportedNbt = NbtReader.ReadRoot(sbsMs);
        var sizeList = exportedNbt.GetList("Size");
        Assert.NotNull(sizeList);
        Assert.Equal(1, ((NbtInt)sizeList[0]).Value);
        Assert.Equal(1, ((NbtInt)sizeList[1]).Value);
        Assert.Equal(1, ((NbtInt)sizeList[2]).Value);

        var blocks = exportedNbt.GetIntArray("Blocks");
        Assert.NotNull(blocks);
        Assert.Single(blocks);
        Assert.Equal(1, blocks[0]); // references non-air palette entry

        // Re-import and verify normalized location (0, 0, 0)
        sbsMs.Position = 0;
        var imported = SpaceboxStructureBridge.Import(sbsMs);
        Assert.Equal(16, imported.Size);
        ushort importedVal = imported.Cells[WorldSnapshot.CellIndex(imported.Size, 0, 0, 0)];
        Assert.True(importedVal > 0);
        Assert.Equal(11, imported.Palette[importedVal]);
    }

    [Fact]
    public void SbsExport_SingleVoxel_AtMinCorner_NormalizedTo1x1x1()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        var palette = new List<int> { WorldSnapshot.AirBlockId, 23 }; // Cockpit
        cells[WorldSnapshot.CellIndex(size, 0, 0, 0)] = 1;

        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells);

        using var sbsMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, sbsMs);
        sbsMs.Position = 0;

        var exportedNbt = NbtReader.ReadRoot(sbsMs);
        var sizeList = exportedNbt.GetList("Size");
        Assert.NotNull(sizeList);
        Assert.Equal(1, ((NbtInt)sizeList[0]).Value);
        Assert.Equal(1, ((NbtInt)sizeList[1]).Value);
        Assert.Equal(1, ((NbtInt)sizeList[2]).Value);
    }

    [Fact]
    public void SbsExport_DiagonalOppositeCorners_SpansFullGrid()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        var palette = new List<int> { WorldSnapshot.AirBlockId, 11 };

        // Two voxels at diagonal opposite corners of the 16^3 grid
        cells[WorldSnapshot.CellIndex(size, 0, 0, 0)] = 1;
        cells[WorldSnapshot.CellIndex(size, 15, 15, 15)] = 1;

        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells);

        using var sbsMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, sbsMs);
        sbsMs.Position = 0;

        var exportedNbt = NbtReader.ReadRoot(sbsMs);
        var sizeList = exportedNbt.GetList("Size");
        Assert.NotNull(sizeList);
        Assert.Equal(16, ((NbtInt)sizeList[0]).Value);
        Assert.Equal(16, ((NbtInt)sizeList[1]).Value);
        Assert.Equal(16, ((NbtInt)sizeList[2]).Value);

        var blocks = exportedNbt.GetIntArray("Blocks");
        Assert.NotNull(blocks);
        Assert.Equal(4096, blocks.Length);
        Assert.Equal(1, blocks[0]);
        Assert.Equal(1, blocks[4095]);
    }

    [Fact]
    public void SbsExport_ConsoleTarget_And_Camera_OutsideBoundingBox()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        var palette = new List<int> { WorldSnapshot.AirBlockId, 23 }; // Cockpit

        // Single solid cockpit at (5, 5, 5)
        cells[WorldSnapshot.CellIndex(size, 5, 5, 5)] = 1;

        // Target and Camera are located outside the 1x1x1 bounding box of solid cells
        var console = new SavedConsole(
            Cell: new Int3(5, 5, 5),
            Groups:
            [
                new SavedConsoleGroup(Index: 0, Key: "Space", Targets: [new Int3(12, 10, 14)]) // Target in empty space
            ],
            Camera: new Int3(5, 8, 5)); // Camera above the cockpit

        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells)
        {
            Consoles = [console]
        };

        using var sbsMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, sbsMs);
        sbsMs.Position = 0;

        // Shift vector was (5, 5, 5)
        // Console Cell -> (0, 0, 0)
        // Target -> (12 - 5, 10 - 5, 14 - 5) = (7, 5, 9)
        // Camera -> (5 - 5, 8 - 5, 5 - 5) = (0, 3, 0)
        var imported = SpaceboxStructureBridge.Import(sbsMs);
        Assert.Single(imported.Consoles);
        var impConsole = imported.Consoles[0];
        Assert.Equal(new Int3(0, 0, 0), impConsole.Cell);
        Assert.Equal(new Int3(0, 3, 0), impConsole.Camera);
        Assert.Single(impConsole.Groups);
        Assert.Single(impConsole.Groups[0].Targets);
        Assert.Equal(new Int3(7, 5, 9), impConsole.Groups[0].Targets[0]);
    }

    [Fact]
    public void SbsExport_TurnedCells_OnAirBlocks_IgnoredGracefully()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        var palette = new List<int> { WorldSnapshot.AirBlockId, 11 };

        cells[WorldSnapshot.CellIndex(size, 2, 2, 2)] = 1;

        // Turned orientation specified on an AIR cell (10, 10, 10) which is outside bounding box
        var turned = new List<TurnedCell>
        {
            new(new Int3(2, 2, 2), 5),
            new(new Int3(10, 10, 10), 12)
        };

        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells)
        {
            Turned = turned
        };

        using var sbsMs = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, sbsMs);
        sbsMs.Position = 0;

        var imported = SpaceboxStructureBridge.Import(sbsMs);
        Assert.Single(imported.Turned);
        Assert.Equal(new Int3(0, 0, 0), imported.Turned[0].Cell);
        Assert.Equal(5, imported.Turned[0].Orientation);
    }

    [Fact]
    public void SbsExport_Author_Null_And_SpecialCharacters()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        cells[WorldSnapshot.CellIndex(size, 1, 1, 1)] = 1;

        var ship = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: [WorldSnapshot.AirBlockId, 11],
            Cells: cells);

        // 1. Author = null defaults to "OpenCorneroids"
        using var msNull = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, msNull, author: null!);
        msNull.Position = 0;
        var nbtNull = NbtReader.ReadRoot(msNull);
        Assert.Equal("OpenCorneroids", nbtNull.GetString("Author"));

        // 2. Author with Unicode & emojis
        string authorUnicode = "Переяслав 🇺🇦 Arch_01";
        using var msUnicode = new MemoryStream();
        SpaceboxStructureBridge.Export(ship, msUnicode, author: authorUnicode);
        msUnicode.Position = 0;
        var nbtUnicode = NbtReader.ReadRoot(msUnicode);
        Assert.Equal(authorUnicode, nbtUnicode.GetString("Author"));
    }

    [Fact]
    public void SbsExport_And_Import_ThreeCycleRoundTrip_Convergence()
    {
        int size = 16;
        var cells = new ushort[size * size * size];
        var palette = new List<int> { WorldSnapshot.AirBlockId, 11, 23, 28, 30 };

        // Multi-block structure
        cells[WorldSnapshot.CellIndex(size, 3, 3, 3)] = 1; // Hull
        cells[WorldSnapshot.CellIndex(size, 3, 3, 4)] = 2; // Cockpit
        cells[WorldSnapshot.CellIndex(size, 3, 3, 5)] = 3; // Thruster
        cells[WorldSnapshot.CellIndex(size, 4, 3, 4)] = 4; // Cannon

        var turned = new List<TurnedCell>
        {
            new(new Int3(3, 3, 4), 7),
            new(new Int3(3, 3, 5), 18)
        };

        var consoles = new List<SavedConsole>
        {
            new(
                Cell: new Int3(3, 3, 4),
                Groups:
                [
                    new SavedConsoleGroup(Index: 0, Key: "Space", Targets: [new Int3(3, 3, 5)]),
                    new SavedConsoleGroup(Index: 1, Key: "MouseLeft", Targets: [new Int3(4, 3, 4)])
                ],
                Camera: new Int3(3, 4, 4))
        };

        var originalShip = new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: size,
            Palette: palette,
            Cells: cells)
        {
            Turned = turned,
            Consoles = consoles
        };

        // Cycle 1: Export -> Import (normalizes to 0-offset)
        using var sbs1 = new MemoryStream();
        SpaceboxStructureBridge.Export(originalShip, sbs1);
        sbs1.Position = 0;
        var ship1 = SpaceboxStructureBridge.Import(sbs1);

        // Cycle 2: Export -> Import (already normalized, must be strictly identical)
        using var sbs2 = new MemoryStream();
        SpaceboxStructureBridge.Export(ship1, sbs2);
        sbs2.Position = 0;
        var ship2 = SpaceboxStructureBridge.Import(sbs2);

        // Cycle 3: Export -> Import (re-verify stability)
        using var sbs3 = new MemoryStream();
        SpaceboxStructureBridge.Export(ship2, sbs3);
        sbs3.Position = 0;
        var ship3 = SpaceboxStructureBridge.Import(sbs3);

        // Assert Ship 2 and Ship 3 are byte-for-byte and structurally identical
        Assert.Equal(ship2.Size, ship3.Size);
        Assert.Equal(ship2.Palette, ship3.Palette);
        Assert.Equal(ship2.Cells, ship3.Cells);
        Assert.Equal(ship2.Turned.Count, ship3.Turned.Count);
        for (int i = 0; i < ship2.Turned.Count; i++)
        {
            Assert.Equal(ship2.Turned[i].Cell, ship3.Turned[i].Cell);
            Assert.Equal(ship2.Turned[i].Orientation, ship3.Turned[i].Orientation);
        }
        Assert.Equal(ship2.Consoles.Count, ship3.Consoles.Count);
        for (int i = 0; i < ship2.Consoles.Count; i++)
        {
            Assert.Equal(ship2.Consoles[i].Cell, ship3.Consoles[i].Cell);
            Assert.Equal(ship2.Consoles[i].Camera, ship3.Consoles[i].Camera);
            Assert.Equal(ship2.Consoles[i].Groups.Count, ship3.Consoles[i].Groups.Count);
        }
    }

    private static void WriteNbtString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)bytes.Length);
        stream.Write(len);
        if (bytes.Length > 0)
        {
            stream.Write(bytes);
        }
    }
}
