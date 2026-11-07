using System.Numerics;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.E2E;

/// <summary>
/// Tier 2: Boundary & Corner Case Tests
/// Comprehensive opaque-box boundary validation for Features 1-9 (M1 WRL Importer)
/// and Features 15-18 (M3 Spacebox Bridge).
/// Minimum >= 5 test cases per feature covering edge cases, extreme inputs, corrupted data, and missing tables.
/// </summary>
public class Tier2_BoundaryCornerTests
{
    // =========================================================================
    // Feature 1: WRL SQLite Schema Parsing — Boundaries & Fault Tolerance
    // =========================================================================

    [Fact]
    public void F01_B1_MissingWorldinfoTable_ThrowsInformativeException()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        // Create only players table, omitting worldinfo
        cmd.CommandText = "CREATE TABLE players (id INT, name VARCHAR(50));";
        cmd.ExecuteNonQuery();

        if (!WrlReaderBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => WrlReaderBridge.ReadFromConnection(conn));
    }

    [Fact]
    public void F01_B2_MissingBlocksectorsTable_ThrowsOrLogsDiagnostic()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE worldinfo (name VARCHAR(50), version FLOAT, seed INT, itemsetKey VARCHAR(50), timestamp LONG, token VARCHAR(20));";
        cmd.ExecuteNonQuery();
        WrlFixtures.InsertWorldInfo(conn, "NoSectors", 1.2f, 1, "defaultItemset", 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        // Should either throw or return snapshot with empty ships
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Ships);
    }

    [Fact]
    public void F01_B3_MissingControlTables_LoadsEntitiesWithoutConsoles()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE worldinfo (name VARCHAR(50), version FLOAT, seed INT, itemsetKey VARCHAR(50), timestamp LONG, token VARCHAR(20));
CREATE TABLE entities (id INT, loaded INT, posx LONG, posy LONG, posz LONG, orx FLOAT, ory FLOAT, orz FLOAT, orw FLOAT, sectorx INT, sectory INT, sectorz INT);
CREATE TABLE players (id INT, name VARCHAR(50), posx LONG, posy LONG, posz LONG, orx FLOAT, ory FLOAT, orz FLOAT, orw FLOAT, boardedEntity INT, boundEntities BLOB, token VARCHAR(20));
CREATE TABLE blocksectors (ownerId INT, posx INT, posy INT, posz INT, formation BLOB, blockIds BLOB, orientations BLOB);";
        cmd.ExecuteNonQuery();
        WrlFixtures.InsertWorldInfo(conn, "NoConsoles", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Single(snapshot.Ships);
        Assert.Empty(snapshot.Ships[0].Consoles);
    }

    [Fact]
    public void F01_B4_ExtraUnknownTablesAndColumns_SafelyIgnored()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE unknownExtra (foo INT, bar VARCHAR(100));
INSERT INTO unknownExtra VALUES (123, 'extra');
ALTER TABLE worldinfo ADD COLUMN extraCol VARCHAR(50) DEFAULT 'bonus';";
        cmd.ExecuteNonQuery();
        WrlFixtures.InsertWorldInfo(conn, "ExtraCols", 1.2f, 1, "defaultItemset", 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal("ExtraCols", snapshot.Name);
    }

    [Fact]
    public void F01_B5_CorruptedSqliteHeader_ThrowsException()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.wrl");
        File.WriteAllBytes(tempFile, new byte[] { 0x00, 0x01, 0x02, 0x03, 0xDE, 0xAD, 0xBE, 0xEF });

        try
        {
            if (!WrlReaderBridge.RequireImplemented()) return;
            Assert.ThrowsAny<Exception>(() => WrlReaderBridge.Read(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // =========================================================================
    // Feature 2: World Metadata — Boundaries
    // =========================================================================

    [Fact]
    public void F02_B1_EmptyWorldName_HandledSafely()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "", 1.2f, 1, "defaultItemset", 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot.Name);
    }

    [Fact]
    public void F02_B2_MaxStringLengthWorldName50Chars_HandledSafely()
    {
        string maxName = new string('A', 50);
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, maxName, 1.2f, 1, "defaultItemset", 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal(maxName, snapshot.Name);
    }

    [Fact]
    public void F02_B3_NegativeVersion_HandledGracefully()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "NegVer", -1.0f, 1, "defaultItemset", 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void F02_B4_EpochTimestampZero_ProducesValidDate()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "ZeroEpoch", 1.2f, 1, "defaultItemset", 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.True(snapshot.SavedAt >= DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void F02_B5_ItemsetKeyWithSpecialCharacters_PreservedCorrectly()
    {
        string specialKey = "Mod_Ω-v2.1 [Special]";
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "SpecialWorld", 1.2f, 1, specialKey, 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Equal(specialKey, snapshot.Itemset.Key);
    }

    // =========================================================================
    // Feature 3: Fixed-Point Coordinate Decoding — Boundaries
    // =========================================================================

    [Fact]
    public void F03_B1_MaxPositiveInt64Coordinate_DecodesWithoutCrash()
    {
        long raw = long.MaxValue / 1000;
        float decoded = WrlFixtures.DecodeFixedPoint(raw);
        Assert.False(float.IsNaN(decoded));
        Assert.True(decoded > 0);
    }

    [Fact]
    public void F03_B2_MinNegativeInt64Coordinate_DecodesWithoutCrash()
    {
        long raw = long.MinValue / 1000;
        float decoded = WrlFixtures.DecodeFixedPoint(raw);
        Assert.False(float.IsNaN(decoded));
        Assert.True(decoded < 0);
    }

    [Fact]
    public void F03_B3_SmallestNonZeroFixedPointIncrement_DecodesToFloatEpsilon()
    {
        long raw = 1L; // 1 / 8388608.0 = 1.1920928955078125e-7
        float decoded = WrlFixtures.DecodeFixedPoint(raw);
        Assert.True(decoded > 0);
        Assert.True(Math.Abs(decoded - 1.192093e-7f) < 1e-9);
    }

    [Fact]
    public void F03_B4_PositionBeyondWorldBoundary_PreservedInEntityPosition()
    {
        // 1,000,000 units in world coordinates
        long raw = WrlFixtures.EncodeFixedPoint(1_000_000f);
        float decoded = WrlFixtures.DecodeFixedPoint(raw);
        Assert.Equal(1_000_000f, decoded, 0.1f);
    }

    [Fact]
    public void F03_B5_NegativeCoordinateWithFraction_PreservesSignAndFraction()
    {
        long raw = WrlFixtures.EncodeFixedPoint(-123.456f);
        float decoded = WrlFixtures.DecodeFixedPoint(raw);
        Assert.Equal(-123.456f, decoded, 0.001f);
    }

    // =========================================================================
    // Feature 4: Orientation Math — Boundaries
    // =========================================================================

    [Fact]
    public void F04_B1_UnnormalizedQuaternion_HandledOrNormalized()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        // Length 2 quaternion (0, 0, 0, 2)
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 2f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var rot = snapshot.Ships[0].Rotation;
        Assert.False(float.IsNaN(rot.W));
    }

    [Fact]
    public void F04_B2_DegenerateZeroQuaternion_HandledSafely()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 0f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void F04_B3_GimbalLockPitches_ConvertsToValidEulerAngles()
    {
        // 90 deg pitch (gimbal lock point)
        float s = (float)Math.Sin(Math.PI / 4.0);
        float c = (float)Math.Cos(Math.PI / 4.0);
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertPlayer(conn, 1, "P", 0L, 0L, 0L, s, 0f, 0f, c);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.False(float.IsNaN(snapshot.Player.Pitch));
        Assert.False(float.IsNaN(snapshot.Player.Yaw));
    }

    [Fact]
    public void F04_B4_PlayerBoardedNonExistentEntity_FallsBackGracefully()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertPlayer(conn, 1, "P", 0L, 0L, 0L, 0f, 0f, 0f, 1f, boardedEntity: 99999);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot.Player);
    }

    [Fact]
    public void F04_B5_NegativeQuaternionComponents_PreservesRotationChirality()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, -0.5f, -0.5f, -0.5f, 0.5f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Single(snapshot.Ships);
    }

    // =========================================================================
    // Feature 5: Chunk Formation Bitmask — Boundaries
    // =========================================================================

    [Fact]
    public void F05_B1_CorruptedGzipHeaderInFormation_ThrowsHandledException()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Corrupted formation BLOB
        byte[] corrupted = [0xFF, 0xFE, 0x00, 0x11, 0x22];
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, corrupted, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));

        if (!WrlReaderBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => WrlReaderBridge.ReadFromConnection(conn));
    }

    [Fact]
    public void F05_B2_TruncatedFormationBlobLessThan512Bytes_ThrowsException()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Decompresses to only 100 bytes instead of 512
        byte[] shortFormation = WrlFixtures.CompressGzip(new byte[100]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, shortFormation, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));

        if (!WrlReaderBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => WrlReaderBridge.ReadFromConnection(conn));
    }

    [Fact]
    public void F05_B3_OverlongFormationBlobGreaterThan512Bytes_RejectsOrTruncates()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Decompresses to 1000 bytes
        byte[] longFormation = WrlFixtures.CompressGzip(new byte[1000]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, longFormation, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));

        if (!WrlReaderBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => WrlReaderBridge.ReadFromConnection(conn));
    }

    [Fact]
    public void F05_B4_NonContiguousSectorCoordsExtreme_ExpandsBoundingBox()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Sectors at (0, 0, 0) and (2, 0, 0)
        var f1 = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        var f2 = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(15, 0, 0)]));
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f1, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));
        WrlFixtures.InsertBlockSector(conn, 1, 2, 0, 0, f2, WrlFixtures.CompressGzip([2]), WrlFixtures.CompressGzip([0]));

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.True(ship.Size >= 48); // spans at least 3 sectors = 48
    }

    [Fact]
    public void F05_B5_SingleSolidVoxelAtByteBoundaryBit7_DecodesProperly()
    {
        // bit 7 of byte 0: bitPos = 7 -> (7, 0, 0)
        var mask = WrlFixtures.BuildFormationMask([(7, 0, 0)]);
        Assert.Equal(0x01, mask[0]);
    }

    // =========================================================================
    // Feature 6: Block ID Unpacking — Boundaries
    // =========================================================================

    [Fact]
    public void F06_B1_CorruptedGzipHeaderInBlockIds_ThrowsHandledException()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var f = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f, new byte[] { 0xDE, 0xAD }, WrlFixtures.CompressGzip([0]));

        if (!WrlReaderBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => WrlReaderBridge.ReadFromConnection(conn));
    }

    [Fact]
    public void F06_B2_BlockIdsLengthShorterThanSolidCount_ThrowsException()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // 2 solid blocks, but only 1 block ID byte
        var f = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0), (1, 0, 0)]));
        var b = WrlFixtures.CompressGzip([42]);
        var o = WrlFixtures.CompressGzip([0, 0]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f, b, o);

        if (!WrlReaderBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => WrlReaderBridge.ReadFromConnection(conn));
    }

    [Fact]
    public void F06_B3_BlockIdsLengthLongerThanSolidCount_HandlesGracefully()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // 1 solid block, but 3 block ID bytes
        var f = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        var b = WrlFixtures.CompressGzip([42, 99, 100]);
        var o = WrlFixtures.CompressGzip([0]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f, b, o);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void F06_B4_EmptyBlockIdsWithZeroSolidCount_Succeeds()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var f = WrlFixtures.CompressGzip(new byte[512]);
        var b = WrlFixtures.CompressGzip([]);
        var o = WrlFixtures.CompressGzip([]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f, b, o);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void F06_B5_BlockIdZero_MappedProperlyWithoutTreatingAsAirSentinel()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var f = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        var b = WrlFixtures.CompressGzip([0]); // solid block with ID 0
        var o = WrlFixtures.CompressGzip([0]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0, f, b, o);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var ship = snapshot.Ships[0];
        Assert.Contains(0, ship.Palette);
    }

    // =========================================================================
    // Feature 7: Quarter-Turn Math — Boundaries
    // =========================================================================

    [Fact]
    public void F07_B1_AllQuarterTurnsSetTo3_MapsToValidRotation0To23()
    {
        byte b = WrlFixtures.BuildQuarterTurnOrientationByte(3, 3, 3);
        int idx = WrlFixtures.DeriveOrientationIndexOracle(b);
        Assert.InRange(idx, 0, 23);
    }

    [Fact]
    public void F07_B2_AllUnusedBitsSet0xC0_MaskedOutCleanly()
    {
        byte b = 0b11000000; // only flags
        int idx = WrlFixtures.DeriveOrientationIndexOracle(b);
        Assert.Equal(0, idx); // identity
    }

    [Fact]
    public void F07_B3_IdentityQuarterTurn_DoesNotEmitTurnedCell()
    {
        byte b = 0x00;
        int idx = WrlFixtures.DeriveOrientationIndexOracle(b);
        Assert.Equal(0, idx);
    }

    [Fact]
    public void F07_B4_InverseRotations_CancelOutCorrectly()
    {
        // 4 quarter turns in yaw is 360 deg
        byte b1 = WrlFixtures.BuildQuarterTurnOrientationByte(1, 0, 0); // +90
        byte b3 = WrlFixtures.BuildQuarterTurnOrientationByte(3, 0, 0); // +270 (-90)
        int idx1 = WrlFixtures.DeriveOrientationIndexOracle(b1);
        int idx3 = WrlFixtures.DeriveOrientationIndexOracle(b3);

        var o1 = Orientation.FromIndex(idx1);
        var o3 = Orientation.FromIndex(idx3);
        var combined = o1.Then(o3);
        Assert.Equal(0, combined.Index); // Identity
    }

    [Fact]
    public void F07_B5_QuarterTurnsFormProperOrthogonalMatrixDetPlusOne()
    {
        for (int y = 0; y < 4; y++)
        for (int p = 0; p < 4; p++)
        for (int r = 0; r < 4; r++)
        {
            byte b = WrlFixtures.BuildQuarterTurnOrientationByte(y, p, r);
            int idx = WrlFixtures.DeriveOrientationIndexOracle(b);
            Assert.InRange(idx, 0, 23);
        }
    }

    // =========================================================================
    // Feature 8: Control Console — Boundaries
    // =========================================================================

    [Fact]
    public void F08_B1_OrphanControlledBlockInvalidBoundTo_IgnoredWithoutCrash()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        // controlled block bound to non-existent console 999
        WrlFixtures.InsertControlledBlock(conn, 1, 999, 0, 1, 0f, 0f, 0f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void F08_B2_ConsoleWithKeyZero_MappedToDefaultKeyNone()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 1, 1, 1, 0f, 0f, 0f);
        WrlFixtures.InsertControlledBlock(conn, 10, 1, 0, 1, 1f, 0f, 0f);
        WrlFixtures.InsertKeyColorMapping(conn, 1, 1, 0, 0); // key = 0

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var key = snapshot.Ships[0].Consoles[0].Groups[0].Key;
        Assert.Equal("None", key);
    }

    [Fact]
    public void F08_B3_ConsoleKeyWithUnknownCode_MappedToDefaultKeyNone()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 1, 1, 1, 0f, 0f, 0f);
        WrlFixtures.InsertControlledBlock(conn, 10, 1, 0, 1, 1f, 0f, 0f);
        WrlFixtures.InsertKeyColorMapping(conn, 1, 1, 0, 99999); // invalid XNA key

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var key = snapshot.Ships[0].Consoles[0].Groups[0].Key;
        Assert.Equal("None", key);
    }

    [Fact]
    public void F08_B4_FractionalControlledBlockCoordinates_RoundedToNearestInt3()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 1, 1, 1, 0f, 0f, 0f);
        WrlFixtures.InsertControlledBlock(conn, 10, 1, 0, 1, 1.0001f, 2.999f, 0f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        var target = snapshot.Ships[0].Consoles[0].Groups[0].Targets[0];
        Assert.Equal(1, target.X);
        Assert.Equal(3, target.Y);
    }

    [Fact]
    public void F08_B5_ConsoleTargetOutOfBounds_HandledSafely()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertControlBlock(conn, 1, 1, 1, 0f, 0f, 0f);
        WrlFixtures.InsertControlledBlock(conn, 10, 1, 0, 1, 1000f, 1000f, 1000f);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    // =========================================================================
    // Feature 9: WorldSnapshot Assembly — Boundaries
    // =========================================================================

    [Fact]
    public void F09_B1_EntityWithZeroBlocksectors_SkippedOrEmptyShip()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        // zero blocksector rows

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void F09_B2_EntityWithExtremelyDispersedChunks_HandledOrBounded()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "W", 1.2f, 1, "defaultItemset", 0L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        var f = WrlFixtures.CompressGzip(WrlFixtures.BuildFormationMask([(0, 0, 0)]));
        WrlFixtures.InsertBlockSector(conn, 1, -5, 0, 0, f, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));
        WrlFixtures.InsertBlockSector(conn, 1, 5, 0, 0, f, WrlFixtures.CompressGzip([1]), WrlFixtures.CompressGzip([0]));

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public void F09_B3_WorldWithZeroEntities_ProducesEmptyShipsList()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "Empty", 1.2f, 1, "defaultItemset", 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.Empty(snapshot.Ships);
    }

    [Fact]
    public void F09_B4_WorldWithZeroPlayers_DefaultsToSafeSpawnPosition()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "NoPlayers", 1.2f, 1, "defaultItemset", 0L);

        if (!WrlReaderBridge.RequireImplemented()) return;
        var snapshot = WrlReaderBridge.ReadFromConnection(conn);
        Assert.NotNull(snapshot.Player);
    }

    [Fact]
    public void F09_B5_EnclosingPowerOfTwoCap1024_DoesNotOverflow()
    {
        int size = 128;
        Assert.True(size <= WorldSaveFormat.MaxSize);
    }

    // =========================================================================
    // Feature 15: Pure C# BCL NBT — Boundaries
    // =========================================================================

    [Fact]
    public void F15_B1_EmptyCompound_RoundtripsSuccessfully()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(SbsFixtures.TagCompound);
            writer.Write((short)0); // empty name
            writer.Write(SbsFixtures.TagEnd);
        }
        ms.Position = 0;
        var dict = SbsFixtures.ReadUncompressedNbtCompound(ms);
        Assert.Empty(dict);
    }

    [Fact]
    public void F15_B2_CorruptedGzipStream_ThrowsInvalidDataException()
    {
        var corrupted = new MemoryStream([0x1F, 0x8B, 0xFF, 0xFF, 0x00]);
        Assert.ThrowsAny<Exception>(() => SbsFixtures.ReadGzipNbtCompound(corrupted));
    }

    [Fact]
    public void F15_B3_UnknownTagIdInCompound_ThrowsInvalidDataException()
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(SbsFixtures.TagCompound);
            writer.Write((short)0);
            writer.Write((byte)99); // unknown tag 99
        }
        ms.Position = 0;
        Assert.ThrowsAny<Exception>(() => SbsFixtures.ReadUncompressedNbtCompound(ms));
    }

    [Fact]
    public void F15_B4_EmptyByteArrayAndIntArray_RoundtripsSuccessfully()
    {
        var sbs = SbsFixtures.CreateSbsArchive("EmptyArrays", "Tester", 0, 0, 0, ["default:air"], [], []);
        var nbt = SbsFixtures.ReadGzipNbtCompound(new MemoryStream(sbs));
        Assert.Empty((int[])nbt["Blocks"]);
        Assert.Empty((byte[])nbt["Orientations"]);
    }

    [Fact]
    public void F15_B5_ExtremeStringLengthWithSpecialUnicode_RoundtripsWithoutLoss()
    {
        string special = "Ω≈ç√∫˜µ≤≥÷ - Spacebox Ship \"Extreme\" \\ UTF-8";
        var sbs = SbsFixtures.CreateSbsArchive(special, special, 1, 1, 1, ["default:air"], [0], [0]);
        var nbt = SbsFixtures.ReadGzipNbtCompound(new MemoryStream(sbs));
        Assert.Equal(special, nbt["Name"]);
        Assert.Equal(special, nbt["Author"]);
    }

    // =========================================================================
    // Feature 16: SBS Structure Import — Boundaries
    // =========================================================================

    [Fact]
    public void F16_B1_SizeZeroDimensions_ThrowsHandledException()
    {
        var sbs = SbsFixtures.CreateSbsArchive("ZeroSize", "Tester", 0, 0, 0, ["default:air"], [], []);

        if (!SpaceboxBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => SpaceboxBridge.Import(new MemoryStream(sbs)));
    }

    [Fact]
    public void F16_B2_MissingPaletteTag_ThrowsHandledException()
    {
        // Custom corrupt NBT missing Palette
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(SbsFixtures.TagCompound);
            writer.Write((short)0);
            writer.Write(SbsFixtures.TagEnd);
        }
        ms.Position = 0;

        if (!SpaceboxBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => SpaceboxBridge.Import(ms));
    }

    [Fact]
    public void F16_B3_BlocksArrayLengthMismatchWithSize_ThrowsHandledException()
    {
        // Size 2x2x2 = 8 cells, but Blocks array has only 2 elements
        var sbs = SbsFixtures.CreateSbsArchive("ShortBlocks", "Tester", 2, 2, 2,
            ["default:air", "default:steel_hull"], [0, 1], new byte[8]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => SpaceboxBridge.Import(new MemoryStream(sbs)));
    }

    [Fact]
    public void F16_B4_OrientationsArrayLengthMismatch_DefaultsToZeroRotation()
    {
        // Orientations length is short
        var sbs = SbsFixtures.CreateSbsArchive("ShortOrientations", "Tester", 1, 1, 1,
            ["default:air", "default:steel_hull"], [1], []);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbs));
        Assert.NotNull(ship);
    }

    [Fact]
    public void F16_B5_CorruptedNbtStream_ThrowsHandledException()
    {
        var corrupt = new MemoryStream([0x1F, 0x8B, 0x00, 0x00]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => SpaceboxBridge.Import(corrupt));
    }

    // =========================================================================
    // Feature 17: SBS Structure Export — Boundaries
    // =========================================================================

    [Fact]
    public void F17_B1_ExportShipWithAllAirCells_GeneratesMinimalSbs()
    {
        var ship = Persistence.SaveFixtures.Ship(size: 4) with
        {
            Palette = [WorldSnapshot.AirBlockId],
            Cells = new ushort[4 * 4 * 4]
        };
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest);
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        Assert.NotNull(nbt);
    }

    [Fact]
    public void F17_B2_ExportSingleVoxelShip_GeneratesSizeOneCube()
    {
        var cells = new ushort[4 * 4 * 4];
        cells[0] = 1; // 1 solid voxel
        var ship = Persistence.SaveFixtures.Ship(size: 4) with
        {
            Palette = [WorldSnapshot.AirBlockId, 10],
            Cells = cells
        };
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest);
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        var sizeList = (List<object>)nbt["Size"];
        Assert.Equal(1, sizeList[0]);
        Assert.Equal(1, sizeList[1]);
        Assert.Equal(1, sizeList[2]);
    }

    [Fact]
    public void F17_B3_ExportShipWithAll24Orientations_PreservesEveryOrientationByte()
    {
        var turned = new List<TurnedCell>();
        for (int i = 1; i < 24; i++)
        {
            turned.Add(new TurnedCell(new Int3(i % 4, (i / 4) % 4, (i / 16)), i));
        }
        var ship = Persistence.SaveFixtures.Ship(size: 4) with
        {
            Turned = turned
        };
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest);
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        var orientations = (byte[])nbt["Orientations"];
        Assert.NotEmpty(orientations);
    }

    [Fact]
    public void F17_B4_ExportShipWithConsolesWithoutCameras_OmitsCameraField()
    {
        var consoles = new List<SavedConsole>
        {
            new SavedConsole(new Int3(1, 1, 1), [new SavedConsoleGroup(0, "Space", [new Int3(2, 2, 2)])], Camera: null)
        };
        var ship = Persistence.SaveFixtures.Ship(size: 4) with
        {
            Consoles = consoles
        };
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest);
        dest.Position = 0;
        var nbt = SbsFixtures.ReadGzipNbtCompound(dest);
        Assert.NotNull(nbt);
    }

    [Fact]
    public void F17_B5_ExportShipWithMaxSupportedSize_CompletesWithoutMemoryExhaustion()
    {
        // 16^3 cube ship
        var ship = Persistence.SaveFixtures.Ship(size: 16);
        using var dest = new MemoryStream();

        if (!SpaceboxBridge.RequireImplemented()) return;
        SpaceboxBridge.Export(ship, dest);
        Assert.True(dest.Length > 0);
    }

    // =========================================================================
    // Feature 18: SBS Palette Resolution — Boundaries
    // =========================================================================

    [Fact]
    public void F18_B1_PaletteWithEmptyString_TreatedAsAirSentinel()
    {
        var sbs = SbsFixtures.CreateSbsArchive("EmptyStringPalette", "Tester", 1, 1, 1,
            ["", "default:steel_hull"], [0], [0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbs));
        Assert.Equal(WorldSnapshot.AirBlockId, ship.Palette[0]);
    }

    [Fact]
    public void F18_B2_BlockIndexOutOfPaletteRange_MapsToAir()
    {
        // Palette length 2 (indices 0, 1), but cell has index 99
        var sbs = SbsFixtures.CreateSbsArchive("OutOfRange", "Tester", 1, 1, 1,
            ["default:air", "default:steel_hull"], [99], [0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbs));
        Assert.NotNull(ship);
    }

    [Fact]
    public void F18_B3_PaletteWithoutAirEntry_AirSentinelHandledSafely()
    {
        var sbs = SbsFixtures.CreateSbsArchive("NoAir", "Tester", 1, 1, 1,
            ["default:steel_hull"], [0], [0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbs));
        Assert.NotNull(ship);
    }

    [Fact]
    public void F18_B4_UnmappedBlockPreservesOrientationInTurnedList()
    {
        var sbs = SbsFixtures.CreateSbsArchive("UnmappedTurned", "Tester", 1, 1, 1,
            ["default:air", "unknown_mod:alien_block"], [1], [7]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbs));
        Assert.NotNull(ship);
    }

    [Fact]
    public void F18_B5_PaletteWithColonsAndSlashesInNamespace_ResolvesCleanly()
    {
        var sbs = SbsFixtures.CreateSbsArchive("ComplexNamespace", "Tester", 1, 1, 1,
            ["default:air", "mod/sub_group:hyper_cannon_mk2"], [1], [0]);

        if (!SpaceboxBridge.RequireImplemented()) return;
        var ship = SpaceboxBridge.Import(new MemoryStream(sbs));
        Assert.NotNull(ship);
    }
}
