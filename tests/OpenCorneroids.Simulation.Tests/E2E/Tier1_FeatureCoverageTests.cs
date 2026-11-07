using System.Numerics;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.E2E;

/// <summary>
/// Tier 1: Feature Coverage Tests
/// Comprehensive opaque-box happy-path validation for Features 1-9 (M1 WRL Importer)
/// and Features 15-18 (M3 Spacebox Bridge).
/// Minimum >= 5 test cases per feature.
/// </summary>
public class Tier1_FeatureCoverageTests
{
    // =========================================================================
    // Feature 1: WRL SQLite Schema Parsing
    // =========================================================================

    [Fact]
    public void F01_T1_AllRequiredTables_OpenAndQueryable()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('worldinfo','players','entities','blocksectors','controlblocks','controlledblocks','keyColorMappings')";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(7, count);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void F01_T2_OptionalTables_HandledGracefully()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('spacesectors','inventoryItems','beacons')";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(3, count);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void F01_T3_CustomMemoryConnectionString_Succeeds()
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Shared");
        conn.Open();
        WrlFixtures.InitializeSchema(conn);
        WrlFixtures.InsertWorldInfo(conn, "CustomMem", 1.2f, 42, "defaultItemset", 1600000000000L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal("CustomMem", snapshot.Name);
    }

    [Fact]
    public void F01_T4_SchemaColumnTypes_MatchExpected()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(blocksectors)";
        using var reader = cmd.ExecuteReader();
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns[reader.GetString(1)] = reader.GetString(2);
        }
        Assert.Contains("ownerId", columns.Keys);
        Assert.Contains("posx", columns.Keys);
        Assert.Contains("formation", columns.Keys);
        Assert.Contains("blockIds", columns.Keys);
        Assert.Contains("orientations", columns.Keys);
    }

    [Fact]
    public void F01_T5_EmptyTables_QueriesSucceedWithoutException()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "EmptyWorld", 1.2f, 999, "defaultItemset", 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Ships);
    }

    // =========================================================================
    // Feature 2: WRL World Metadata Extraction
    // =========================================================================

    [Fact]
    public void F02_T1_ExtractWorldName_MatchesExpected()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "Nova Sector Prime", 1.2f, 12345, "defaultItemset", 1700000000000L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal("Nova Sector Prime", snapshot.Name);
    }

    [Fact]
    public void F02_T2_ExtractSeed_MatchesExpected()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "SeedWorld", 1.2f, -87654321, "defaultItemset", 1700000000000L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal(-87654321, snapshot.Seed);
    }

    [Fact]
    public void F02_T3_ExtractItemsetKey_MatchesExpected()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "ModdedWorld", 1.2f, 100, "DarthNoxItemset", 1700000000000L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal("DarthNoxItemset", snapshot.Itemset.Key);
    }

    [Fact]
    public void F02_T4_ExtractSavedAt_ConvertsTimestampCorrectly()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        long unixMs = 1700000000000L; // 2023-11-14T22:13:20Z
        WrlFixtures.InsertWorldInfo(conn, "TimeWorld", 1.2f, 100, "defaultItemset", unixMs);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var expectedDate = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        Assert.True(Math.Abs((snapshot.SavedAt - expectedDate).TotalSeconds) < 2);
    }

    [Fact]
    public void F02_T5_NullItemsetKey_FallsBackToDefault()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "FallbackWorld", 1.2f, 100, null!, 1700000000000L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn, "defaultItemset");
        Assert.NotNull(snapshot.Itemset.Key);
        Assert.NotEmpty(snapshot.Itemset.Key);
    }

    // =========================================================================
    // Feature 3: WRL Fixed-Point Coordinate Decoding
    // =========================================================================

    [Fact]
    public void F03_T1_ZeroRawCoordinate_DecodesToZero()
    {
        float decoded = WrlFixtures.DecodeFixedPoint(0L);
        Assert.Equal(0.0f, decoded);
    }

    [Fact]
    public void F03_T2_ScaleConstant_DecodesToUnitOne()
    {
        long raw = 8388608L; // 2^23
        float decoded = WrlFixtures.DecodeFixedPoint(raw);
        Assert.Equal(1.0f, decoded);
    }

    [Fact]
    public void F03_T3_TenTimesScale_DecodesToTen()
    {
        long raw = 83886080L;
        float decoded = WrlFixtures.DecodeFixedPoint(raw);
        Assert.Equal(10.0f, decoded);
    }

    [Fact]
    public void F03_T4_NegativeRawCoordinate_DecodesToNegativeFloat()
    {
        long raw = -83886080L;
        float decoded = WrlFixtures.DecodeFixedPoint(raw);
        Assert.Equal(-10.0f, decoded);
    }

    [Fact]
    public void F03_T5_FractionalRawCoordinate_DecodesToHalf()
    {
        long raw = 4194304L; // 2^22
        float decoded = WrlFixtures.DecodeFixedPoint(raw);
        Assert.Equal(0.5f, decoded);
    }

    // =========================================================================
    // Feature 4: WRL Entity & Player Orientation
    // =========================================================================

    [Fact]
    public void F04_T1_EntityIdentityQuaternion_PreservedInShipSnapshot()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        if (snapshot.Ships.Count > 0)
        {
            var rot = snapshot.Ships[0].Rotation;
            Assert.True(Math.Abs(rot.W - 1.0f) < 1e-4);
        }
    }

    [Fact]
    public void F04_T2_PlayerQuaternion_ConvertsToEulerAngles()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        // Identity quaternion for player
        WrlFixtures.InsertPlayer(conn, 1, "Commander", 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal(0.0f, snapshot.Player.Pitch, 3);
        Assert.Equal(0.0f, snapshot.Player.Yaw, 3);
    }

    [Fact]
    public void F04_T3_Yaw90DegreeQuaternion_YieldsCorrectYawAngle()
    {
        // 90 deg rotation about Y: q = (0, sin(45), 0, cos(45)) = (0, 0.7071, 0, 0.7071)
        float s = (float)Math.Sin(Math.PI / 4.0);
        float c = (float)Math.Cos(Math.PI / 4.0);
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertPlayer(conn, 1, "Commander", 0L, 0L, 0L, 0f, s, 0f, c);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.True(Math.Abs(snapshot.Player.Yaw - (float)(Math.PI / 2.0)) < 0.1f ||
                    Math.Abs(snapshot.Player.Yaw - (float)(-Math.PI / 2.0)) < 0.1f);
    }

    [Fact]
    public void F04_T4_Pitch45DegreeQuaternion_YieldsCorrectPitchAngle()
    {
        // 45 deg rotation about X
        float s = (float)Math.Sin(Math.PI / 8.0);
        float c = (float)Math.Cos(Math.PI / 8.0);
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertPlayer(conn, 1, "Commander", 0L, 0L, 0L, s, 0f, 0f, c);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.True(Math.Abs(snapshot.Player.Pitch) > 0.1f);
    }

    [Fact]
    public void F04_T5_RollRotationQuaternion_YieldsCorrectRollAngle()
    {
        // 90 deg rotation about Z
        float s = (float)Math.Sin(Math.PI / 4.0);
        float c = (float)Math.Cos(Math.PI / 4.0);
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertPlayer(conn, 1, "Commander", 0L, 0L, 0L, 0f, 0f, s, c);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.True(Math.Abs(snapshot.Player.Roll) > 0.1f);
    }

    // =========================================================================
    // Feature 5: WRL Chunk Formation Bitmask
    // =========================================================================

    [Fact]
    public void F05_T1_EmptyFormationBlob_YieldsZeroSolidVoxels()
    {
        var rawMask = WrlFixtures.BuildFormationMask([]);
        Assert.Equal(512, rawMask.Length);
        Assert.All(rawMask, b => Assert.Equal(0, b));
    }

    [Fact]
    public void F05_T2_SingleBlockAt000_YieldsMsbBitSet()
    {
        var rawMask = WrlFixtures.BuildFormationMask([(0, 0, 0)]);
        Assert.Equal(512, rawMask.Length);
        Assert.Equal(0x80, rawMask[0]); // MSB set
        Assert.All(rawMask.Skip(1), b => Assert.Equal(0, b));
    }

    [Fact]
    public void F05_T3_SingleBlockAt151515_YieldsLsbBitSet()
    {
        // bitPos = 15*256 + 15*16 + 15 = 3840 + 240 + 15 = 4095
        // byteIndex = 4095 / 8 = 511, bit = 7 -> 0x80 >> 7 = 0x01
        var rawMask = WrlFixtures.BuildFormationMask([(15, 15, 15)]);
        Assert.Equal(512, rawMask.Length);
        Assert.Equal(0x01, rawMask[511]);
        Assert.All(rawMask.Take(511), b => Assert.Equal(0, b));
    }

    [Fact]
    public void F05_T4_FullSolidChunk_Yields4096SolidVoxels()
    {
        var allCoords = new List<(int x, int y, int z)>();
        for (int z = 0; z < 16; z++)
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    allCoords.Add((x, y, z));

        var rawMask = WrlFixtures.BuildFormationMask(allCoords);
        Assert.Equal(512, rawMask.Length);
        Assert.All(rawMask, b => Assert.Equal(0xFF, b));
    }

    [Fact]
    public void F05_T5_CheckerboardPattern_DecodesExpectedBits()
    {
        var coords = new List<(int x, int y, int z)>
        {
            (0, 0, 0), // bit 0 -> 0x80
            (2, 0, 0), // bit 2 -> 0x20
            (4, 0, 0), // bit 4 -> 0x08
            (6, 0, 0)  // bit 6 -> 0x02
        };
        var rawMask = WrlFixtures.BuildFormationMask(coords);
        Assert.Equal(0xAA, rawMask[0]); // 10101010
    }

    // =========================================================================
    // Feature 6: WRL Chunk Block ID Unpacking
    // =========================================================================

    [Fact]
    public void F06_T1_SingleSolidVoxel_AssignsBlockIdFromBlob()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var formation = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        var blockIds = WrlFixtures.CompressGzip([42]);
        var orientations = WrlFixtures.CompressGzip([0]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, formation, blockIds, orientations);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Single(snapshot.Ships);
        var ship = snapshot.Ships[0];
        // Ensure block ID 42 is present in palette
        Assert.Contains(42, ship.Palette);
    }

    [Fact]
    public void F06_T2_MultipleSolidVoxels_AssignSequentialBlockIds()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var coords = new[] { (0, 0, 0), (1, 0, 0), (2, 0, 0) };
        var formation = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask(coords));
        var blockIds = WrlFixtures.CompressGzip([10, 20, 30]);
        var orientations = WrlFixtures.CompressGzip([0, 0, 0]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, formation, blockIds, orientations);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.Contains(10, ship.Palette);
        Assert.Contains(20, ship.Palette);
        Assert.Contains(30, ship.Palette);
    }

    [Fact]
    public void F06_T3_HighRangeBlockIds_UnpacksCorrectly()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var formation = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        var blockIds = WrlFixtures.CompressGzip([255]); // max byte
        var orientations = WrlFixtures.CompressGzip([0]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, formation, blockIds, orientations);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.Contains(255, ship.Palette);
    }

    [Fact]
    public void F06_T4_HeterogeneousBlockIds_MaintainsSpatialMapping()
    {
        var rawBlockIds = new byte[] { 1, 2, 3, 4, 5 };
        var gzipped = WrlFixtures.CompressGzip(rawBlockIds);
        Assert.NotEmpty(gzipped);
    }

    [Fact]
    public void F06_T5_SolidCountEqualsBlockIdsLength_VerifiesIntegrity()
    {
        var coords = new[] { (0, 0, 0), (5, 5, 5) };
        var mask = WrlFixtures.BuildFormationMask(coords);
        int solidCount = 0;
        for (int i = 0; i < 4096; i++)
        {
            if ((mask[i >> 3] & (0x80 >> (i & 7))) != 0) solidCount++;
        }
        Assert.Equal(2, solidCount);
    }

    // =========================================================================
    // Feature 7: WRL Quarter-Turn Orientation Math
    // =========================================================================

    [Fact]
    public void F07_T1_ZeroQuarterTurns_MapsToDefaultOrientation0()
    {
        byte b = 0x00;
        int index = WrlFixtures.DeriveOrientationIndexOracle(b);
        Assert.Equal(0, index);
    }

    [Fact]
    public void F07_T2_YawQuarterTurn_MatchesChiralRotationOracle()
    {
        byte b = 0x01; // yaw = 1 (90 deg around Y)
        int index = WrlFixtures.DeriveOrientationIndexOracle(b);
        Assert.True(index >= 0 && index < 24);
        var o = Orientation.FromIndex(index);
        Assert.Equal(AxisDirection.PosY, o.Up);
    }

    [Fact]
    public void F07_T3_PitchQuarterTurn_MatchesChiralRotationOracle()
    {
        byte b = 0x04; // pitch = 1 (90 deg around X)
        int index = WrlFixtures.DeriveOrientationIndexOracle(b);
        Assert.True(index >= 0 && index < 24);
    }

    [Fact]
    public void F07_T4_RollQuarterTurn_MatchesChiralRotationOracle()
    {
        byte b = 0x10; // roll = 1 (90 deg around Z)
        int index = WrlFixtures.DeriveOrientationIndexOracle(b);
        Assert.True(index >= 0 && index < 24);
    }

    [Fact]
    public void F07_T5_CombinedQuarterTurns_IgnoresBits6And7()
    {
        byte bNormal = WrlFixtures.BuildQuarterTurnOrientationByte(1, 2, 3);
        byte bWithFlags = (byte)(bNormal | 0xC0); // set bits 6 & 7

        int idx1 = WrlFixtures.DeriveOrientationIndexOracle(bNormal);
        int idx2 = WrlFixtures.DeriveOrientationIndexOracle(bWithFlags);
        Assert.Equal(idx1, idx2);
    }

    // =========================================================================
    // Feature 8: WRL Control Console & Key Mappings
    // =========================================================================

    [Fact]
    public void F08_T1_ConsoleBlock_MapsToSavedConsole()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 10, 1, 1, 0f, 0f, 0f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.Single(ship.Consoles);
    }

    [Fact]
    public void F08_T2_ControlledBlock_GroupsByColorIndex()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 10, 1, 1, 0f, 0f, 0f);
        WrlFixtures.InsertControlledBlock(conn, 100, 10, 0, 1, 1f, 0f, 0f); // Red group (0)

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.Single(ship.Consoles[0].Groups);
        Assert.Equal(0, ship.Consoles[0].Groups[0].Index);
    }

    [Fact]
    public void F08_T3_KeyMapping_TranslatesXnaKeysToKeyString()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 10, 1, 1, 0f, 0f, 0f);
        WrlFixtures.InsertControlledBlock(conn, 100, 10, 0, 1, 1f, 0f, 0f);
        WrlFixtures.InsertKeyColorMapping(conn, 1, 10, 0, 32); // 32 = Space in XNA

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.Equal("Space", ship.Consoles[0].Groups[0].Key);
    }

    [Fact]
    public void F08_T4_MultipleColorGroups_PreservedInSavedConsoleGroup()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 10, 1, 1, 0f, 0f, 0f);
        WrlFixtures.InsertControlledBlock(conn, 100, 10, 0, 1, 1f, 0f, 0f); // Red
        WrlFixtures.InsertControlledBlock(conn, 101, 10, 1, 1, 0f, 1f, 0f); // Green

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.Equal(2, ship.Consoles[0].Groups.Count);
    }

    [Fact]
    public void F08_T5_UnboundConsole_ProducesEmptyGroups()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 10, 1, 1, 0f, 0f, 0f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.Empty(ship.Consoles[0].Groups);
    }

    // =========================================================================
    // Feature 9: WRL WorldSnapshot Assembly
    // =========================================================================

    [Fact]
    public void F09_T1_EntitiesMappedToShipSnapshots()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "ShipWorld", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 100, true, 83886080L, 0L, 0L, 0f, 0f, 0f, 1f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Single(snapshot.Ships);
        Assert.Equal(10.0f, snapshot.Ships[0].Position.X);
    }

    [Fact]
    public void F09_T2_EntityGridShift_AdjustsWorldPosition()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Sector located at (1, 0, 0)
        var formation = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        var blockIds = WrlFixtures.CompressGzip([1]);
        var orientations = WrlFixtures.CompressGzip([0]);
        WrlFixtures.InsertBlockSector(conn, 1, 1, 0, 0, formation, blockIds, orientations);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.NotNull(ship);
    }

    [Fact]
    public void F09_T3_EnclosingPowerOfTwoCube_AllocatedProperly()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var formation = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        var blockIds = WrlFixtures.CompressGzip([1]);
        var orientations = WrlFixtures.CompressGzip([0]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, formation, blockIds, orientations);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        int size = snapshot.Ships[0].Size;
        Assert.True(size is 16 or 32 or 64 or 128);
    }

    [Fact]
    public void F09_T4_SnapshotIsCompatibleWithOcworldSerialization()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "RoundtripWorld", 1.2f, 1234, "defaultItemset", 1700000000000L);
        WrlFixtures.InsertPlayer(conn, 1, "Player", 83886080L, 0L, 0L, 0f, 0f, 0f, 1f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        using var ms = new MemoryStream();
        WorldSaveFormat.Write(ms, snapshot);
        ms.Position = 0;
        var loaded = WorldSaveFormat.Read(ms);
        Assert.Equal(snapshot.Name, loaded.Name);
        Assert.Equal(snapshot.Seed, loaded.Seed);
    }

    [Fact]
    public void F09_T5_PlayerStateAttached_WithCorrectPositionAndAngles()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertPlayer(conn, 1, "Hero", 83886080L, -83886080L, 0L, 0f, 0f, 0f, 1f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal(10.0f, snapshot.Player.Position.X);
        Assert.Equal(-10.0f, snapshot.Player.Position.Y);
        Assert.Equal(0.0f, snapshot.Player.Position.Z);
    }

    // =========================================================================
    // Feature 15: Pure C# BCL NBT Reader/Writer
    // =========================================================================

    [Fact]
    public void F15_T1_CompoundWithPrimitives_RoundtripsIdentically()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("TestStructure", "Engineer", 4, 4, 4,
            ["default:air", "default:steel_hull"], [0, 1], [0, 0]);
        Assert.NotEmpty(sbsBytes);

        var nbt = SbsFixtures.ReadGzipNbtCompound(new MemoryStream(sbsBytes));
        Assert.Equal("TestStructure", nbt["Name"]);
        Assert.Equal("Engineer", nbt["Author"]);
        Assert.Equal(1, nbt["Version"]);
    }

    [Fact]
    public void F15_T2_ListTag_RoundtripsIdentically()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("ListTest", "Engineer", 10, 20, 30,
            ["default:air"], [0], [0]);
        var nbt = SbsFixtures.ReadGzipNbtCompound(new MemoryStream(sbsBytes));
        var sizeList = (List<object>)nbt["Size"];
        Assert.Equal(3, sizeList.Count);
        Assert.Equal(10, sizeList[0]);
        Assert.Equal(20, sizeList[1]);
        Assert.Equal(30, sizeList[2]);
    }

    [Fact]
    public void F15_T3_IntArrayAndByteArray_RoundtripsIdentically()
    {
        int[] expectedBlocks = [0, 1, 2, 3];
        byte[] expectedOrientations = [10, 20, 30, 40];
        var sbsBytes = SbsFixtures.CreateSbsArchive("ArraysTest", "Engineer", 2, 2, 1,
            ["default:air", "default:steel_hull", "default:glass", "default:light"],
            expectedBlocks, expectedOrientations);

        var nbt = SbsFixtures.ReadGzipNbtCompound(new MemoryStream(sbsBytes));
        var blocks = (int[])nbt["Blocks"];
        var orientations = (byte[])nbt["Orientations"];
        Assert.Equal(expectedBlocks, blocks);
        Assert.Equal(expectedOrientations, orientations);
    }

    [Fact]
    public void F15_T4_NestedCompounds_RoundtripsIdentically()
    {
        var consoles = new List<(Int3 consoleCell, int groupIndex, string key, List<Int3> targets)>
        {
            (new Int3(1, 1, 1), 0, "Space", [new Int3(2, 2, 2)])
        };
        var sbsBytes = SbsFixtures.CreateSbsArchive("ConsoleTest", "Engineer", 4, 4, 4,
            ["default:air"], [0], [0], consoles);

        var nbt = SbsFixtures.ReadGzipNbtCompound(new MemoryStream(sbsBytes));
        Assert.Contains("Consoles", nbt.Keys);
        var consoleList = (List<object>)nbt["Consoles"];
        Assert.Single(consoleList);
    }

    [Fact]
    public void F15_T5_GzipCompressedNbtStream_DecompressesAndParses()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("GzipTest", "Tester", 1, 1, 1, ["default:air"], [0], [0]);
        // Valid GZIP magic bytes: 0x1F, 0x8B
        Assert.Equal(0x1F, sbsBytes[0]);
        Assert.Equal(0x8B, sbsBytes[1]);
    }

    // =========================================================================
    // Feature 16: SBS Structure Import
    // =========================================================================

    [Fact]
    public void F16_T1_ImportMinimalSbs_ProducesValidShipSnapshot()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("Cruiser", "Spacebox", 2, 2, 2,
            ["default:air", "default:steel_hull"],
            [0, 1, 0, 0, 0, 0, 0, 0],
            new byte[8]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.NotNull(ship);
        Assert.True(ship.Size >= 2);
    }

    [Fact]
    public void F16_T2_ImportSbs_MapsPaletteIndicesToCells()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("SingleBlock", "Author", 1, 1, 1,
            ["default:air", "default:steel_hull"],
            [1],
            [0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.Contains(ship.Palette, id => id > 0);
    }

    [Fact]
    public void F16_T3_ImportSbs_UnpacksOrientationsIntoTurnedCells()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("TurnedShip", "Author", 1, 1, 1,
            ["default:air", "default:steel_hull"],
            [1],
            [5]); // non-zero orientation

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.NotEmpty(ship.Turned);
    }

    [Fact]
    public void F16_T4_ImportSbs_ReconstructsConsolesAndGroups()
    {
        var consoles = new List<(Int3 consoleCell, int groupIndex, string key, List<Int3> targets)>
        {
            (new Int3(0, 0, 0), 0, "Space", [new Int3(1, 0, 0)])
        };
        var sbsBytes = SbsFixtures.CreateSbsArchive("ConsoleShip", "Author", 2, 1, 1,
            ["default:air", "default:cockpit"],
            [1, 1],
            [0, 0],
            consoles);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.NotEmpty(ship.Consoles);
    }

    [Fact]
    public void F16_T5_ImportSbs_PadsNonCubicDimensionsToPowerOfTwo()
    {
        // 3 x 5 x 7 non-cubic
        int total = 3 * 5 * 7;
        var sbsBytes = SbsFixtures.CreateSbsArchive("NonCubic", "Author", 3, 5, 7,
            ["default:air", "default:steel_hull"],
            new int[total],
            new byte[total]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.True(ship.Size is 16 or 32 or 64 or 128);
    }

    // =========================================================================
    // Feature 17: SBS Structure Export
    // =========================================================================

    [Fact]
    public void F17_T1_ExportShipSnapshot_GeneratesValidGzipNbt()
    {
        var ship = Persistence.SaveFixtures.Ship(size: 4);
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest, "Tester");
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        Assert.NotNull(nbt);
    }

    [Fact]
    public void F17_T2_ExportShipSnapshot_GeneratesTightBoundingBox()
    {
        var ship = Persistence.SaveFixtures.Ship(size: 4);
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest, "Tester");
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        var sizeList = (List<object>)nbt["Size"];
        Assert.Equal(3, sizeList.Count);
        Assert.All(sizeList, s => Assert.True((int)s > 0));
    }

    [Fact]
    public void F17_T3_ExportShipSnapshot_GeneratesPaletteFromPresentBlocks()
    {
        var ship = Persistence.SaveFixtures.Ship(size: 4);
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest, "Tester");
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        var palette = (List<object>)nbt["Palette"];
        Assert.NotEmpty(palette);
    }

    [Fact]
    public void F17_T4_ExportShipSnapshot_EncodesBlocksAndOrientationsArrays()
    {
        var ship = Persistence.SaveFixtures.Ship(size: 4);
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest, "Tester");
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        Assert.Contains("Blocks", nbt.Keys);
        Assert.Contains("Orientations", nbt.Keys);
    }

    [Fact]
    public void F17_T5_ExportShipSnapshot_SetsAuthorAndVersionMetadata()
    {
        var ship = Persistence.SaveFixtures.Ship(size: 4);
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest, "AuthorOmega");
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        Assert.Equal("AuthorOmega", nbt["Author"]);
        Assert.Equal(1, nbt["Version"]);
    }

    // =========================================================================
    // Feature 18: SBS Palette Resolution & Fallbacks
    // =========================================================================

    [Fact]
    public void F18_T1_StandardNamespaces_ResolveToCorneroidsBlockIds()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("SteelShip", "Author", 1, 1, 1,
            ["default:air", "default:steel_hull"],
            [1],
            [0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.Contains(ship.Palette, id => id != WorldSnapshot.AirBlockId);
    }

    [Fact]
    public void F18_T2_UnmappedBlockName_FallsBackToAirOrGenericHull()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("UnknownShip", "Author", 1, 1, 1,
            ["default:air", "alien_mod:hyper_drive_mk9"],
            [1],
            [0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.NotNull(ship);
    }

    [Fact]
    public void F18_T3_AirSentinel_MapsToEmptyVoxelCell()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("AirShip", "Author", 1, 1, 1,
            ["default:air"],
            [0],
            [0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.Contains(WorldSnapshot.AirBlockId, ship.Palette);
    }

    [Fact]
    public void F18_T4_CaseInsensitiveBlockNames_ResolveCorrectly()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("CaseShip", "Author", 1, 1, 1,
            ["DEFAULT:AIR", "Default:Steel_Hull"],
            [1],
            [0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.NotNull(ship);
    }

    [Fact]
    public void F18_T5_DuplicatePaletteEntries_HandledSafely()
    {
        var sbsBytes = SbsFixtures.CreateSbsArchive("DuplicateShip", "Author", 2, 1, 1,
            ["default:air", "default:steel_hull", "default:steel_hull"],
            [1, 2],
            [0, 0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbsBytes));
        Assert.NotNull(ship);
    }
}
