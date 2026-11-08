using System.IO.Compression;
using System.Numerics;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.E2E;

/// <summary>
/// Tier 5 White-Box Adversarial Tests for Milestone 1 (CorneroidsWrlReader).
/// Exercises boundary conditions, corrupt data handling, missing tables/columns,
/// extreme quaternions, multi-entity sorting, and console/key resolution math.
/// Purely in-memory fixtures (:memory: SQLite). NEVER touches disk or user data.
/// </summary>
public sealed class Tier5_WrlAdversarialTests
{
    // =========================================================================
    // 1. WorldInfo Table White-Box Edge Cases
    // =========================================================================

    [Fact]
    public void WorldInfo_EmptyTableZeroRows_FallsBackToSafeDefaults()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        // Table exists, but has 0 rows inserted
        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);

        Assert.NotNull(snapshot);
        Assert.Equal("Imported World", snapshot.Name);
        Assert.Equal(0, snapshot.Seed);
        Assert.Equal("Default", snapshot.Itemset.Key);
        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.SavedAt);
        Assert.Empty(snapshot.Ships);
    }

    [Fact]
    public void WorldInfo_EmptyTableWithCustomDefaultKey_UsesCustomDefault()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn, defaultItemsetKey: "CustomPackKey");

        Assert.NotNull(snapshot);
        Assert.Equal("CustomPackKey", snapshot.Itemset.Key);
    }

    [Fact]
    public void WorldInfo_MissingColumnsInTable_ThrowsWorldSaveException()
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Pooling=false");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            // Table worldinfo exists, but lacks expected schema (e.g., only has id)
            cmd.CommandText = "CREATE TABLE worldinfo (id INT); INSERT INTO worldinfo VALUES (1);";
            cmd.ExecuteNonQuery();
        }

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(conn));
        Assert.Contains("Failed to import Corneroids .wrl database", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void WorldInfo_WhitespaceItemsetKey_FallsBackToProvidedDefault(string whitespaceKey)
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "WhitespaceKeyWorld", 1.2f, 123, whitespaceKey, 1000L);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn, defaultItemsetKey: "FallbackKey");
        Assert.Equal("FallbackKey", snapshot.Itemset.Key);
    }

    [Fact]
    public void WorldInfo_NullNameAndSeed_EmploysSafeDefaults()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO worldinfo (name, version, seed, itemsetKey, timestamp, token) VALUES (NULL, 1.2, NULL, NULL, NULL, 'tok');";
            cmd.ExecuteNonQuery();
        }

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        Assert.Equal("Imported World", snapshot.Name);
        Assert.Equal(0, snapshot.Seed);
        Assert.Equal("Default", snapshot.Itemset.Key);
        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.SavedAt);
    }

    // =========================================================================
    // 2. Players Table White-Box Edge Cases
    // =========================================================================

    [Fact]
    public void Players_MultiplePlayersInTable_StrictlyPicksLowestId()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "MultiPlayerWorld", 1.2f, 1, "default", 1000L);

        // Insert in scrambled order: ID 10, ID 2, ID 5
        WrlFixtures.InsertPlayer(conn, 10, "Player10",
            WrlFixtures.EncodeFixedPoint(100f), WrlFixtures.EncodeFixedPoint(200f), WrlFixtures.EncodeFixedPoint(300f),
            0f, 0f, 0f, 1f);
        WrlFixtures.InsertPlayer(conn, 2, "Player2_Lowest",
            WrlFixtures.EncodeFixedPoint(10f), WrlFixtures.EncodeFixedPoint(20f), WrlFixtures.EncodeFixedPoint(30f),
            0f, 0f, 0f, 1f);
        WrlFixtures.InsertPlayer(conn, 5, "Player5",
            WrlFixtures.EncodeFixedPoint(50f), WrlFixtures.EncodeFixedPoint(60f), WrlFixtures.EncodeFixedPoint(70f),
            0f, 0f, 0f, 1f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
        // Player 2 must be chosen (ORDER BY id LIMIT 1)
        Assert.Equal(10f, snapshot.Player.Position.X, 0.01f);
        Assert.Equal(20f, snapshot.Player.Position.Y, 0.01f);
        Assert.Equal(30f, snapshot.Player.Position.Z, 0.01f);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9999)]
    public void Players_BoardedEntityValues_HandledCleanlyRegardlessOfState(int boardedEntity)
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "BoardedWorld", 1.2f, 1, "default", 1000L);
        WrlFixtures.InsertPlayer(conn, 1, "Astronaut",
            WrlFixtures.EncodeFixedPoint(5f), WrlFixtures.EncodeFixedPoint(0f), WrlFixtures.EncodeFixedPoint(0f),
            0f, 0f, 0f, 1f, boardedEntity: boardedEntity);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
        Assert.Equal(5f, snapshot.Player.Position.X, 0.01f);
    }

    [Fact]
    public void Players_ExtremeAndBoundaryQuaternions_NeverProduceNaNOrCrash()
    {
        var testQuaternions = new (float ox, float oy, float oz, float ow)[]
        {
            // Zero quaternion
            (0f, 0f, 0f, 0f),
            // Denormalized tiny quaternion (< 1e-6)
            (1e-8f, 0f, 0f, 0f),
            // Gigantic quaternion
            (1e8f, 1e8f, 1e8f, 1e8f),
            // Gimbal lock pitch +90°: q = (sin(45°), 0, 0, cos(45°))
            (MathF.Sin(MathF.PI / 4f), 0f, 0f, MathF.Cos(MathF.PI / 4f)),
            // Gimbal lock pitch -90°: q = (-sin(45°), 0, 0, cos(45°))
            (-MathF.Sin(MathF.PI / 4f), 0f, 0f, MathF.Cos(MathF.PI / 4f)),
            // Pure Yaw 90°: q = (0, sin(45°), 0, cos(45°))
            (0f, MathF.Sin(MathF.PI / 4f), 0f, MathF.Cos(MathF.PI / 4f)),
            // Pure Roll 90°: q = (0, 0, sin(45°), cos(45°))
            (0f, 0f, MathF.Sin(MathF.PI / 4f), MathF.Cos(MathF.PI / 4f)),
        };

        foreach (var (ox, oy, oz, ow) in testQuaternions)
        {
            using var conn = WrlFixtures.CreateMemoryDatabase();
            WrlFixtures.InsertWorldInfo(conn, "RotWorld", 1.2f, 1, "default", 1000L);
            WrlFixtures.InsertPlayer(conn, 1, "Tester", 0L, 0L, 0L, ox, oy, oz, ow);

            var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
            Assert.False(float.IsNaN(snapshot.Player.Pitch), $"NaN pitch for q=({ox},{oy},{oz},{ow})");
            Assert.False(float.IsNaN(snapshot.Player.Yaw), $"NaN yaw for q=({ox},{oy},{oz},{ow})");
            Assert.False(float.IsNaN(snapshot.Player.Roll), $"NaN roll for q=({ox},{oy},{oz},{ow})");
            Assert.True(float.IsFinite(snapshot.Player.Pitch));
            Assert.True(float.IsFinite(snapshot.Player.Yaw));
            Assert.True(float.IsFinite(snapshot.Player.Roll));
        }

        // Direct mathematical verification of QuaternionToEulerYxz with identity and extreme orientations
        var (p1, y1, r1) = CorneroidsWrlReader.QuaternionToEulerYxz(Quaternion.Identity);
        Assert.Equal(0f, p1);
        Assert.Equal(0f, y1);
        Assert.Equal(0f, r1);
    }

    // =========================================================================
    // 3. Entities Table and Multi-Entity Sorting
    // =========================================================================

    [Fact]
    public void Entities_MultipleEntities_StrictlyOrderedById()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "MultiEntityWorld", 1.2f, 1, "default", 1000L);

        // Insert scrambled IDs: 50, 2, 100
        WrlFixtures.InsertEntity(conn, 50, true, WrlFixtures.EncodeFixedPoint(500f), 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertEntity(conn, 2, true, WrlFixtures.EncodeFixedPoint(20f), 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertEntity(conn, 100, true, WrlFixtures.EncodeFixedPoint(1000f), 0L, 0L, 0f, 0f, 0f, 1f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        Assert.Equal(3, snapshot.Ships.Count);

        // First ship must be ID 2 (pos X = 20)
        Assert.Equal(20f, snapshot.Ships[0].Position.X, 0.01f);
        // Second ship must be ID 50 (pos X = 500)
        Assert.Equal(500f, snapshot.Ships[1].Position.X, 0.01f);
        // Third ship must be ID 100 (pos X = 1000)
        Assert.Equal(1000f, snapshot.Ships[2].Position.X, 0.01f);
    }

    [Fact]
    public void Entities_LoadedAndUnloadedEntities_BothImported()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "LoadedStateWorld", 1.2f, 1, "default", 1000L);

        WrlFixtures.InsertEntity(conn, 1, loaded: false, WrlFixtures.EncodeFixedPoint(10f), 0L, 0L, 0f, 0f, 0f, 1f);
        WrlFixtures.InsertEntity(conn, 2, loaded: true, WrlFixtures.EncodeFixedPoint(20f), 0L, 0L, 0f, 0f, 0f, 1f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        Assert.Equal(2, snapshot.Ships.Count);
        Assert.Equal(10f, snapshot.Ships[0].Position.X, 0.01f);
        Assert.Equal(20f, snapshot.Ships[1].Position.X, 0.01f);
    }

    [Fact]
    public void Entities_FarApartSectors_SizesToNextPowerOfTwo()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "SpanWorld", 1.2f, 1, "default", 1000L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Sector (0, 0, 0) has a solid block at (0, 0, 0)
        var mask0 = WrlFixtures.BuildFormationMask([(0, 0, 0)]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0,
            WrlFixtures.CompressGzip(mask0),
            WrlFixtures.CompressGzip([5]),
            WrlFixtures.CompressGzip([0]));

        // Sector (3, 0, 0) has a solid block at (15, 0, 0) -> global X = 3*16 + 15 = 63. Span = 63 - 0 + 1 = 64
        var mask3 = WrlFixtures.BuildFormationMask([(15, 0, 0)]);
        WrlFixtures.InsertBlockSector(conn, 1, 3, 0, 0,
            WrlFixtures.CompressGzip(mask3),
            WrlFixtures.CompressGzip([6]),
            WrlFixtures.CompressGzip([0]));

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        var ship = Assert.Single(snapshot.Ships);
        Assert.Equal(64, ship.Size);
        Assert.Contains(5, ship.Palette);
        Assert.Contains(6, ship.Palette);
    }

    [Fact]
    public void Entities_SectorsBeyond512Limit_CapsAt512WithoutIndexOutOfRange()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "HugeSpanWorld", 1.2f, 1, "default", 1000L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Sector (0, 0, 0) at origin
        var mask0 = WrlFixtures.BuildFormationMask([(0, 0, 0)]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0,
            WrlFixtures.CompressGzip(mask0),
            WrlFixtures.CompressGzip([1]),
            WrlFixtures.CompressGzip([0]));

        // Sector (100, 0, 0) -> global X = 1600. Span = 1601 > 512
        var mask100 = WrlFixtures.BuildFormationMask([(0, 0, 0)]);
        WrlFixtures.InsertBlockSector(conn, 1, 100, 0, 0,
            WrlFixtures.CompressGzip(mask100),
            WrlFixtures.CompressGzip([2]),
            WrlFixtures.CompressGzip([0]));

        // Reader caps size at 512; voxels outside grid boundary are safely skipped
        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        var ship = Assert.Single(snapshot.Ships);
        Assert.Equal(512, ship.Size);
        // The block at (0, 0, 0) is preserved
        Assert.Contains(1, ship.Palette);
    }

    // =========================================================================
    // 4. Blocksectors Formation & Orientation White-Box Edge Cases
    // =========================================================================

    [Fact]
    public void Blocksectors_OrphanedSectorOwnerId_IgnoredWithoutCorruptingWorld()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "OrphanSectorWorld", 1.2f, 1, "default", 1000L);
        // Entity 1
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);
        var mask = WrlFixtures.BuildFormationMask([(0, 0, 0)]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0,
            WrlFixtures.CompressGzip(mask),
            WrlFixtures.CompressGzip([1]),
            WrlFixtures.CompressGzip([0]));

        // Orphaned sector for non-existent entity 9999
        WrlFixtures.InsertBlockSector(conn, 9999, 0, 0, 0,
            WrlFixtures.CompressGzip(mask),
            WrlFixtures.CompressGzip([99]),
            WrlFixtures.CompressGzip([0]));

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        var ship = Assert.Single(snapshot.Ships);
        Assert.Contains(1, ship.Palette);
        Assert.DoesNotContain(99, ship.Palette);
    }

    [Fact]
    public void Blocksectors_ExcessTrailingBytesInBlobs_ParsedSuccessfully()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "TrailingBytesWorld", 1.2f, 1, "default", 1000L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // 2 solid voxels: (0, 0, 0) and (1, 0, 0)
        var mask = WrlFixtures.BuildFormationMask([(0, 0, 0), (1, 0, 0)]);

        // blockIds has 8 bytes (excess bytes trailing beyond the required 2)
        byte[] blockIdsWithTrailing = [10, 20, 99, 99, 99, 99, 99, 99];
        // orientations has 5 bytes (excess bytes trailing beyond required 2)
        byte[] orientationsWithTrailing = [0, 4, 12, 16, 20];

        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0,
            WrlFixtures.CompressGzip(mask),
            WrlFixtures.CompressGzip(blockIdsWithTrailing),
            WrlFixtures.CompressGzip(orientationsWithTrailing));

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        var ship = Assert.Single(snapshot.Ships);
        Assert.Contains(10, ship.Palette);
        Assert.Contains(20, ship.Palette);
        Assert.DoesNotContain(99, ship.Palette);
    }

    [Fact]
    public void Blocksectors_CheckerboardDensePattern2048Voxels_LoadsFully()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "CheckerboardWorld", 1.2f, 1, "default", 1000L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Construct 512-byte formation with alternating bits 0xAA (every other bit is solid -> exactly 2048 solid voxels)
        byte[] formation = new byte[512];
        Array.Fill(formation, (byte)0xAA);

        byte[] blockIds = new byte[2048];
        Array.Fill(blockIds, (byte)42);

        byte[] orientations = new byte[2048];
        Array.Fill(orientations, (byte)0);

        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0,
            WrlFixtures.CompressGzip(formation),
            WrlFixtures.CompressGzip(blockIds),
            WrlFixtures.CompressGzip(orientations));

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        var ship = Assert.Single(snapshot.Ships);
        Assert.Equal(16, ship.Size);

        // Count non-air voxels
        int nonAirCount = ship.Cells.Count(c => c != WorldSnapshot.Air);
        Assert.Equal(2048, nonAirCount);
    }

    [Fact]
    public void Blocksectors_BoundaryVoxelPlacements_MinAndMaxCorners()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "CornersWorld", 1.2f, 1, "default", 1000L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Corner 0: (0, 0, 0) and Corner 1: (15, 15, 15)
        var mask = WrlFixtures.BuildFormationMask([(0, 0, 0), (15, 15, 15)]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0,
            WrlFixtures.CompressGzip(mask),
            WrlFixtures.CompressGzip([7, 8]),
            WrlFixtures.CompressGzip([0, 0]));

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        var ship = Assert.Single(snapshot.Ships);
        Assert.Equal(16, ship.Size);

        // Origin voxel cell (0, 0, 0)
        ushort val0 = ship.Cells[WorldSnapshot.CellIndex(16, 0, 0, 0)];
        Assert.NotEqual(WorldSnapshot.Air, val0);
        Assert.Equal(7, ship.Palette[val0]);

        // Far corner cell (15, 15, 15)
        ushort val15 = ship.Cells[WorldSnapshot.CellIndex(16, 15, 15, 15)];
        Assert.NotEqual(WorldSnapshot.Air, val15);
        Assert.Equal(8, ship.Palette[val15]);
    }

    // =========================================================================
    // 5. Consoles, Bindings, and XNA Key Resolution White-Box Edge Cases
    // =========================================================================

    [Fact]
    public void Consoles_MultipleConsolesWithDiverseGroupsAndCamera_ImportedCorrectly()
    {
        using var conn = WrlFixtures.CreateMemoryDatabase();
        WrlFixtures.InsertWorldInfo(conn, "ConsolesWorld", 1.2f, 1, "default", 1000L);
        WrlFixtures.InsertEntity(conn, 1, true, 0L, 0L, 0L, 0f, 0f, 0f, 1f);

        // Put solid block at (0, 0, 0)
        var mask = WrlFixtures.BuildFormationMask([(0, 0, 0)]);
        WrlFixtures.InsertBlockSector(conn, 1, 0, 0, 0,
            WrlFixtures.CompressGzip(mask),
            WrlFixtures.CompressGzip([1]),
            WrlFixtures.CompressGzip([0]));

        // Console 1 (id=10, bindingId=50) at (0, 0, 0)
        WrlFixtures.InsertControlBlock(conn, 10, bindingId: 50, ownerEntityId: 1, 0f, 0f, 0f);
        // Console 2 (id=20, bindingId=0) at (2, 0, 0)
        WrlFixtures.InsertControlBlock(conn, 20, bindingId: 0, ownerEntityId: 1, 2f, 0f, 0f);

        // Camera block for Console 1 (id=50) at (1, 1, 1)
        WrlFixtures.InsertControlledBlock(conn, 50, boundTo: 10, bindingColor: 0, ownerEntityId: 1, 1f, 1f, 1f);

        // Controlled block for Console 1 under color 2 at (3, 0, 0)
        WrlFixtures.InsertControlledBlock(conn, 51, boundTo: 10, bindingColor: 2, ownerEntityId: 1, 3f, 0f, 0f);

        // Key mappings: Console 1, color 2 -> Key 'Space' (XNA keycode 32)
        WrlFixtures.InsertKeyColorMapping(conn, ownerEntityId: 1, blockId: 10, color: 2, key: 32);

        // Orphaned controlled blocks for non-existent console 999
        WrlFixtures.InsertControlledBlock(conn, 999, boundTo: 999, bindingColor: 0, ownerEntityId: 1, 0f, 0f, 0f);
        // Orphaned key mapping for non-existent console 999
        WrlFixtures.InsertKeyColorMapping(conn, ownerEntityId: 1, blockId: 999, color: 0, key: 32);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        var ship = Assert.Single(snapshot.Ships);
        Assert.Equal(2, ship.Consoles.Count);

        // Find console 1 (at (0, 0, 0))
        var c1 = ship.Consoles.First(c => c.Cell == new Int3(0, 0, 0));
        Assert.NotNull(c1.Camera);
        Assert.Equal(new Int3(1, 1, 1), c1.Camera!.Value);

        // Check group 2 in c1
        var g2 = c1.Groups.FirstOrDefault(g => g.Index == 2);
        Assert.NotNull(g2);
        Assert.Equal("Space", g2.Key);
        Assert.Single(g2.Targets);
        Assert.Equal(new Int3(3, 0, 0), g2.Targets[0]);

        // Console 2 (at (2, 0, 0)) has no camera
        var c2 = ship.Consoles.First(c => c.Cell == new Int3(2, 0, 0));
        Assert.Null(c2.Camera);
    }

    [Theory]
    [InlineData(65, "A")]
    [InlineData(90, "Z")]
    [InlineData(48, "0")]
    [InlineData(57, "9")]
    [InlineData(8, "Back")]
    [InlineData(9, "Tab")]
    [InlineData(13, "Enter")]
    [InlineData(19, "Pause")]
    [InlineData(20, "CapsLock")]
    [InlineData(27, "Escape")]
    [InlineData(32, "Space")]
    [InlineData(33, "PageUp")]
    [InlineData(34, "PageDown")]
    [InlineData(35, "End")]
    [InlineData(36, "Home")]
    [InlineData(37, "Left")]
    [InlineData(38, "Up")]
    [InlineData(39, "Right")]
    [InlineData(40, "Down")]
    [InlineData(45, "Insert")]
    [InlineData(46, "Delete")]
    [InlineData(96, "NumPad0")]
    [InlineData(105, "NumPad9")]
    [InlineData(106, "Multiply")]
    [InlineData(107, "Add")]
    [InlineData(109, "Subtract")]
    [InlineData(110, "Decimal")]
    [InlineData(111, "Divide")]
    [InlineData(112, "F1")]
    [InlineData(123, "F12")]
    [InlineData(160, "LeftShift")]
    [InlineData(161, "RightShift")]
    [InlineData(162, "LeftControl")]
    [InlineData(163, "RightControl")]
    [InlineData(164, "LeftAlt")]
    [InlineData(165, "RightAlt")]
    [InlineData(186, "OemSemicolon")]
    [InlineData(187, "OemPlus")]
    [InlineData(188, "OemComma")]
    [InlineData(189, "OemMinus")]
    [InlineData(190, "OemPeriod")]
    [InlineData(191, "OemQuestion")]
    [InlineData(192, "OemTilde")]
    [InlineData(219, "OemOpenBrackets")]
    [InlineData(220, "OemPipe")]
    [InlineData(221, "OemCloseBrackets")]
    [InlineData(222, "OemQuotes")]
    [InlineData(0, "None")]
    [InlineData(9999, "None")]
    public void ResolveXnaKey_AllKeyCodes_MapToAccurateString(int keyCode, string expectedKey)
    {
        string actual = CorneroidsWrlReader.ResolveXnaKey(keyCode);
        Assert.Equal(expectedKey, actual);
    }
}
