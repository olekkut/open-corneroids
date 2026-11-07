using System.IO.Compression;
using System.Numerics;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>
/// Adversarial test suite for Milestone 1 (CorneroidsWrlReader).
/// Stress-tests corrupted GZIP streams, length mismatches, missing tables,
/// malformed data, and edge conditions to verify that CorneroidsWrlReader
/// rejects invalid inputs cleanly with WorldSaveException and never suffers unhandled crashes.
/// </summary>
public sealed class CorneroidsWrlAdversarialTests
{
    // =========================================================================
    // Category A: Corrupted and Truncated GZIP Streams
    // =========================================================================

    [Theory]
    [InlineData(new byte[] { 0x00, 0x11, 0x22 })] // Arbitrary non-gzip garbage
    [InlineData(new byte[] { 0x1F, 0x8B })]       // Incomplete 2-byte gzip header
    [InlineData(new byte[] { 0x1F, 0x8B, 0x08, 0x00 })] // Truncated gzip header
    public void Formation_CorruptedOrTruncatedGzip_ThrowsWorldSaveException(byte[] badGzip)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithRawSector(1, 0, 0, 0, badGzip, CorneroidsWrlFixtureBuilder.GzipCompress([1]), CorneroidsWrlFixtureBuilder.GzipCompress([0]));

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("formation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formation_CorruptedGzipPayload_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        // Valid GZip header + corrupted compressed data payload
        byte[] validGzip = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[512]);
        byte[] corrupted = (byte[])validGzip.Clone();
        for (int i = 10; i < Math.Min(30, corrupted.Length); i++)
        {
            corrupted[i] ^= 0xAA;
        }

        fixture.WithRawSector(1, 0, 0, 0, corrupted, CorneroidsWrlFixtureBuilder.GzipCompress([1]), CorneroidsWrlFixtureBuilder.GzipCompress([0]));

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("formation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formation_EmptyBlob_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithRawSector(1, 0, 0, 0, [], CorneroidsWrlFixtureBuilder.GzipCompress([1]), CorneroidsWrlFixtureBuilder.GzipCompress([0]));

        Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
    }

    [Fact]
    public void Formation_NullBlob_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        using (var cmd = fixture.Connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO blocksectors VALUES (1, 0, 0, 0, NULL, @blockIds, @orientations);";
            cmd.Parameters.AddWithValue("@blockIds", CorneroidsWrlFixtureBuilder.GzipCompress([1]));
            cmd.Parameters.AddWithValue("@orientations", CorneroidsWrlFixtureBuilder.GzipCompress([0]));
            cmd.ExecuteNonQuery();
        }

        Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0xFD })] // Garbage non-gzip bytes
    [InlineData(new byte[] { 0x1F, 0x8B })]       // Truncated header
    public void BlockIds_CorruptedOrTruncatedGzip_ThrowsWorldSaveException(byte[] badBlockIds)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        // Formation with 1 solid voxel at (0, 0, 0)
        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0x80;
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);

        fixture.WithRawSector(1, 0, 0, 0, formationGzip, badBlockIds, CorneroidsWrlFixtureBuilder.GzipCompress([0]));

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("blockIds", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlockIds_CorruptedGzipPayload_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0x80;
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);

        byte[] validGzip = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[] { 1, 2, 3, 4, 5 });
        byte[] corrupted = (byte[])validGzip.Clone();
        for (int i = 8; i < corrupted.Length - 4; i++)
            corrupted[i] = (byte)(corrupted[i] ^ 0xCC);

        fixture.WithRawSector(1, 0, 0, 0, formationGzip, corrupted, CorneroidsWrlFixtureBuilder.GzipCompress([0]));

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("blockIds", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlockIds_NullBlob_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0x80;
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);

        using (var cmd = fixture.Connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO blocksectors VALUES (1, 0, 0, 0, @formation, NULL, @orientations);";
            cmd.Parameters.AddWithValue("@formation", formationGzip);
            cmd.Parameters.AddWithValue("@orientations", CorneroidsWrlFixtureBuilder.GzipCompress([0]));
            cmd.ExecuteNonQuery();
        }

        Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
    }

    [Theory]
    [InlineData(new byte[] { 0xBA, 0xAD, 0xF0, 0x0D })]
    [InlineData(new byte[] { 0x1F, 0x8B })]
    public void Orientations_CorruptedOrTruncatedGzip_ThrowsWorldSaveException(byte[] badOrientations)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0x80; // 1 solid voxel
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);

        fixture.WithRawSector(1, 0, 0, 0, formationGzip, CorneroidsWrlFixtureBuilder.GzipCompress([42]), badOrientations);

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("orientations", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Orientations_CorruptedGzipPayload_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0x80;
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);

        byte[] validGzip = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[] { 0, 1, 2, 3 });
        byte[] corrupted = (byte[])validGzip.Clone();
        for (int i = 8; i < corrupted.Length - 4; i++)
            corrupted[i] = (byte)(corrupted[i] ^ 0xFF);

        fixture.WithRawSector(1, 0, 0, 0, formationGzip, CorneroidsWrlFixtureBuilder.GzipCompress([10]), corrupted);

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("orientations", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Orientations_NullBlob_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0x80;
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);

        using (var cmd = fixture.Connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO blocksectors VALUES (1, 0, 0, 0, @formation, @blockIds, NULL);";
            cmd.Parameters.AddWithValue("@formation", formationGzip);
            cmd.Parameters.AddWithValue("@blockIds", CorneroidsWrlFixtureBuilder.GzipCompress([1]));
            cmd.ExecuteNonQuery();
        }

        Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
    }

    // =========================================================================
    // Category B: Decompressed Length Mismatches
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    [InlineData(511)]
    public void Formation_LengthShorterThan512Bytes_ThrowsWorldSaveException(int length)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        byte[] badFormation = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[length]);
        fixture.WithRawSector(1, 0, 0, 0, badFormation, CorneroidsWrlFixtureBuilder.GzipCompress([1]), CorneroidsWrlFixtureBuilder.GzipCompress([0]));

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("expected 512 bytes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(513)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void Formation_LengthLongerThan512Bytes_ThrowsWorldSaveException(int length)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        byte[] badFormation = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[length]);
        fixture.WithRawSector(1, 0, 0, 0, badFormation, CorneroidsWrlFixtureBuilder.GzipCompress([1]), CorneroidsWrlFixtureBuilder.GzipCompress([0]));

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("expected 512 bytes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void BlockIds_LengthShorterThanSolidCount_ThrowsWorldSaveException(int blockIdsCount)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        // 5 solid voxels (0b11111000 in first byte)
        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0xF8;
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);

        byte[] shortBlockIds = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[blockIdsCount]);
        byte[] validOrientations = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[5]);

        fixture.WithRawSector(1, 0, 0, 0, formationGzip, shortBlockIds, validOrientations);

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("blockIds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expected 5 bytes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Orientations_LengthShorterThanSolidCount_ThrowsWorldSaveException(int orientationsCount)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        // 4 solid voxels (0b11110000 in first byte)
        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0xF0;
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);

        byte[] validBlockIds = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[4]);
        byte[] shortOrientations = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[orientationsCount]);

        fixture.WithRawSector(1, 0, 0, 0, formationGzip, validBlockIds, shortOrientations);

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("orientations", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expected 4 bytes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlockIds_And_Orientations_BothShorterThanSolidCount_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0xC0; // 2 solid voxels
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);

        fixture.WithRawSector(1, 0, 0, 0, formationGzip,
            CorneroidsWrlFixtureBuilder.GzipCompress([1]),
            CorneroidsWrlFixtureBuilder.GzipCompress([]));

        Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
    }

    // =========================================================================
    // Category C: Missing Tables
    // =========================================================================

    [Fact]
    public void MissingWorldinfoTable_ThrowsWorldSaveException()
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Pooling=false");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE dummy (id INT);";
            cmd.ExecuteNonQuery();
        }

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(conn));
        Assert.Contains("worldinfo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletelyEmptyDatabase_ThrowsWorldSaveException()
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Pooling=false");
        conn.Open();

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(conn));
        Assert.Contains("worldinfo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingPlayersTable_LoadsCleanlyWithDefaultPlayer()
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Pooling=false");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE worldinfo (name VARCHAR(50), version FLOAT, seed INT, itemsetKey VARCHAR(50), timestamp LONG, token VARCHAR(20));
                INSERT INTO worldinfo VALUES ('NoPlayerWorld', 1.2, 42, 'default', 1000, 'tok');
                """;
            cmd.ExecuteNonQuery();
        }

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
        Assert.Equal("NoPlayerWorld", snapshot.Name);
        Assert.Equal(Vector3.Zero, snapshot.Player.Position);
        Assert.Equal(0f, snapshot.Player.Pitch);
        Assert.Equal(0f, snapshot.Player.Yaw);
        Assert.Equal(0f, snapshot.Player.Roll);
    }

    [Fact]
    public void MissingEntitiesTable_LoadsCleanlyWithZeroShips()
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Pooling=false");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE worldinfo (name VARCHAR(50), version FLOAT, seed INT, itemsetKey VARCHAR(50), timestamp LONG, token VARCHAR(20));
                INSERT INTO worldinfo VALUES ('NoEntitiesWorld', 1.2, 42, 'default', 1000, 'tok');
                """;
            cmd.ExecuteNonQuery();
        }

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Ships);
    }

    [Fact]
    public void MissingBlocksectorsTable_LoadsEntitiesAsEmptyShipsWithoutThrowing()
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Pooling=false");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE worldinfo (name VARCHAR(50), version FLOAT, seed INT, itemsetKey VARCHAR(50), timestamp LONG, token VARCHAR(20));
                CREATE TABLE entities (id INT, loaded INT, posx LONG, posy LONG, posz LONG, orx FLOAT, ory FLOAT, orz FLOAT, orw FLOAT, sectorx INT, sectory INT, sectorz INT);
                INSERT INTO worldinfo VALUES ('NoBlocksectorsWorld', 1.2, 42, 'default', 1000, 'tok');
                INSERT INTO entities VALUES (1, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0);
                """;
            cmd.ExecuteNonQuery();
        }

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn);
        Assert.NotNull(snapshot);
        Assert.Single(snapshot.Ships);
        Assert.Equal(16, snapshot.Ships[0].Size);
        Assert.All(snapshot.Ships[0].Cells, cell => Assert.Equal(WorldSnapshot.Air, cell));
    }

    [Fact]
    public void MissingControlTables_LoadsEntitiesWithoutConsoles()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 5, 0);

        // Drop console tables
        using (var cmd = fixture.Connection.CreateCommand())
        {
            cmd.CommandText = """
                DROP TABLE IF EXISTS controlblocks;
                DROP TABLE IF EXISTS controlledblocks;
                DROP TABLE IF EXISTS keyColorMappings;
                """;
            cmd.ExecuteNonQuery();
        }

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.Single(snapshot.Ships);
        Assert.Empty(snapshot.Ships[0].Consoles);
    }

    // =========================================================================
    // Category D: Malformed & Corrupted Data in Tables
    // =========================================================================

    [Fact]
    public void Worldinfo_AllColumnsNull_EmploysSafeDefaults()
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Pooling=false");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE worldinfo (name VARCHAR(50), version FLOAT, seed INT, itemsetKey VARCHAR(50), timestamp LONG, token VARCHAR(20));
                INSERT INTO worldinfo VALUES (NULL, NULL, NULL, NULL, NULL, NULL);
                """;
            cmd.ExecuteNonQuery();
        }

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn, defaultItemsetKey: "CustomDefault");
        Assert.Equal("Imported World", snapshot.Name);
        Assert.Equal(0, snapshot.Seed);
        Assert.Equal("CustomDefault", snapshot.Itemset.Key);
        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.SavedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Worldinfo_EmptyOrWhitespaceItemsetKey_FallsBackToDefault(string rawKey)
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Pooling=false");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE worldinfo (name VARCHAR(50), version FLOAT, seed INT, itemsetKey VARCHAR(50), timestamp LONG, token VARCHAR(20));
                INSERT INTO worldinfo VALUES ('EmptyKeyWorld', 1.2, 100, @key, 0, 'tok');
                """;
            cmd.Parameters.AddWithValue("@key", rawKey);
            cmd.ExecuteNonQuery();
        }

        var snapshot = CorneroidsWrlReader.ReadFromConnection(conn, defaultItemsetKey: "FallbackItemset");
        Assert.Equal("FallbackItemset", snapshot.Itemset.Key);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-99999999L)]
    [InlineData(0L)]
    public void Worldinfo_NegativeOrZeroTimestamp_DefaultsToUnixEpoch(long ts)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo("TsWorld", 1.2f, 1, "default", ts);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.SavedAt);
    }

    [Fact]
    public void Worldinfo_ExtremeTimestamp_WrappedInWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo("ExtremeTsWorld", 1.2f, 1, "default", long.MaxValue);

        // long.MaxValue ms from epoch exceeds DateTimeOffset max range, throwing ArgumentOutOfRangeException
        // CorneroidsWrlReader must cleanly wrap this into WorldSaveException
        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Players_NullColumns_CleanlyRejectedWithWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();

        using (var cmd = fixture.Connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO players VALUES (1, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, NULL, 'tok');";
            cmd.ExecuteNonQuery();
        }

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("NULL", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Players_ExtremeCoordinates_DecodesWithoutOverflow()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithPlayer(1, "Voyager", long.MaxValue, long.MinValue, 0L);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.True(float.IsFinite(snapshot.Player.Position.X));
        Assert.True(float.IsFinite(snapshot.Player.Position.Y));
        Assert.Equal(0f, snapshot.Player.Position.Z);
    }

    [Fact]
    public void Players_AllZeroQuaternion_DefaultsToIdentityRotation()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        // Zero quaternion (0, 0, 0, 0)
        fixture.WithPlayer(1, "ZeroRot", 0L, 0L, 0L, orx: 0f, ory: 0f, orz: 0f, orw: 0f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.False(float.IsNaN(snapshot.Player.Pitch));
        Assert.False(float.IsNaN(snapshot.Player.Yaw));
        Assert.False(float.IsNaN(snapshot.Player.Roll));
        Assert.Equal(0f, snapshot.Player.Pitch);
        Assert.Equal(0f, snapshot.Player.Yaw);
        Assert.Equal(0f, snapshot.Player.Roll);
    }

    [Fact]
    public void Players_GimbalLockPitchPositive90_EulerAnglesRemainFinite()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        // Pitch = +90 degrees: q = (sin(45°), 0, 0, cos(45°))
        float s = MathF.Sin(MathF.PI / 4f);
        float c = MathF.Cos(MathF.PI / 4f);
        fixture.WithPlayer(1, "Zenith", 0L, 0L, 0L, orx: s, ory: 0f, orz: 0f, orw: c);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.False(float.IsNaN(snapshot.Player.Pitch));
        Assert.False(float.IsNaN(snapshot.Player.Yaw));
        Assert.False(float.IsNaN(snapshot.Player.Roll));
        Assert.Equal(MathF.PI / 2f, snapshot.Player.Pitch, 0.01f);
    }

    [Fact]
    public void Entities_NullColumns_CleanlyRejectedWithWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();

        using (var cmd = fixture.Connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO entities VALUES (1, 1, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0, 0, 0);";
            cmd.ExecuteNonQuery();
        }

        var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
        Assert.Contains("NULL", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Entities_ZeroQuaternion_DefaultsToIdentityRotation()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L, orx: 0f, ory: 0f, orz: 0f, orw: 0f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.Single(snapshot.Ships);
        Assert.Equal(Quaternion.Identity, snapshot.Ships[0].Rotation);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(999)]
    [InlineData(-9999)]
    public void ControlledBlocks_InvalidBindingColors_IgnoredCleanly(int invalidColor)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0);
        fixture.WithConsole(10, 1, 0f, 0f, 0f);
        fixture.WithControlledBlock(100, 10, invalidColor, 1, 0f, 0f, 0f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var console = snapshot.Ships[0].Consoles[0];
        Assert.Empty(console.Groups);
    }

    [Fact]
    public void ControlledBlocks_OrphanedConsoleTargets_IgnoredWithoutCrash()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0);

        // Controlled blocks bound to non-existent consoles (9999 and 8888)
        fixture.WithControlledBlock(100, 9999, 0, 1, 0f, 0f, 0f);
        fixture.WithControlledBlock(101, 8888, 1, 1, 1f, 0f, 0f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.Single(snapshot.Ships);
        Assert.Empty(snapshot.Ships[0].Consoles);
    }

    [Fact]
    public void ControlledBlocks_DuplicateTargetsSameGroup_Deduplicated()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0);
        fixture.WithConsole(10, 1, 0f, 0f, 0f);

        // 3 entries for identical target cell (0, 0, 0) under color 0
        fixture.WithControlledBlock(100, 10, 0, 1, 0f, 0f, 0f);
        fixture.WithControlledBlock(101, 10, 0, 1, 0.001f, 0f, 0f);
        fixture.WithControlledBlock(102, 10, 0, 1, 0f, 0.001f, 0f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var console = snapshot.Ships[0].Consoles[0];
        Assert.Single(console.Groups);
        Assert.Single(console.Groups[0].Targets);
        Assert.Equal(new Int3(0, 0, 0), console.Groups[0].Targets[0]);
    }

    [Fact]
    public void ControlledBlocks_OutOfBoundsTargets_FilteredOutSafely()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0); // size 16
        fixture.WithConsole(10, 1, 0f, 0f, 0f);

        fixture.WithControlledBlock(100, 10, 0, 1, 100f, 100f, 100f);
        fixture.WithControlledBlock(101, 10, 1, 1, -5f, 0f, 0f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var console = snapshot.Ships[0].Consoles[0];
        Assert.Empty(console.Groups);
    }

    [Fact]
    public void ControlBlocks_OrphanedCameraTarget_CameraRemainsNull()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0);
        // Console bindingId = 555 which does not exist in controlledblocks
        fixture.WithConsole(10, 1, 0f, 0f, 0f, bindingId: 555);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var console = snapshot.Ships[0].Consoles[0];
        Assert.Null(console.Camera);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(10)]
    [InlineData(100)]
    public void KeyColorMappings_InvalidColors_IgnoredCleanly(int invalidColor)
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0);
        fixture.WithConsole(10, 1, 0f, 0f, 0f);
        fixture.WithControlledBlock(100, 10, 0, 1, 0f, 0f, 0f);
        fixture.WithKeyMapping(1, 10, invalidColor, 32);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var console = snapshot.Ships[0].Consoles[0];
        Assert.Single(console.Groups);
        // Group 0 should retain default key "None"
        Assert.Equal("None", console.Groups[0].Key);
    }

    [Fact]
    public void KeyColorMappings_OrphanedConsoleId_IgnoredSafely()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0);

        // Key mapping for console 999 which does not exist
        fixture.WithKeyMapping(1, 999, 0, 32);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.Single(snapshot.Ships);
        Assert.Empty(snapshot.Ships[0].Consoles);
    }

    [Fact]
    public void Blocksectors_FormationZeroSolidCount_SkippedCleanly()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        // Formation all zeros (0 solid voxels)
        byte[] emptyFormation = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[512]);
        // Empty blockIds and orientations
        fixture.WithRawSector(1, 0, 0, 0, emptyFormation, CorneroidsWrlFixtureBuilder.GzipCompress([]), CorneroidsWrlFixtureBuilder.GzipCompress([]));

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.Single(snapshot.Ships);
        Assert.Equal(16, snapshot.Ships[0].Size);
        Assert.All(snapshot.Ships[0].Cells, cell => Assert.Equal(WorldSnapshot.Air, cell));
    }

    [Fact]
    public void Blocksectors_FormationAllSolidCount4096_ParsedFully()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1);

        byte[] allSolidFormation = new byte[512];
        Array.Fill(allSolidFormation, (byte)0xFF);

        byte[] blockIds = new byte[4096];
        Array.Fill(blockIds, (byte)7);

        byte[] orientations = new byte[4096];
        Array.Fill(orientations, (byte)0);

        fixture.WithRawSector(1, 0, 0, 0,
            CorneroidsWrlFixtureBuilder.GzipCompress(allSolidFormation),
            CorneroidsWrlFixtureBuilder.GzipCompress(blockIds),
            CorneroidsWrlFixtureBuilder.GzipCompress(orientations));

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var ship = snapshot.Ships[0];
        Assert.Equal(16, ship.Size);
        // All 4096 cells should be non-air
        Assert.All(ship.Cells, cell => Assert.NotEqual(WorldSnapshot.Air, cell));
    }

    [Fact]
    public void CorneroidsOrientation_All256OrientationBytes_MapToValidCubeRotations()
    {
        for (int b = 0; b < 256; b++)
        {
            int rotIdx = CorneroidsOrientation.FromByte((byte)b);
            Assert.InRange(rotIdx, 0, 23);
        }
    }

    // =========================================================================
    // Category E: File & Connection Boundaries
    // =========================================================================

    [Fact]
    public void Read_NullOrWhitespacePath_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => CorneroidsWrlReader.Read(null!));
        Assert.ThrowsAny<ArgumentException>(() => CorneroidsWrlReader.Read(""));
        Assert.ThrowsAny<ArgumentException>(() => CorneroidsWrlReader.Read("   "));
    }

    [Fact]
    public void Read_NonSqliteFileGarbageBytes_ThrowsWorldSaveException()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"garbage_{Guid.NewGuid():N}.wrl");
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE });
            var ex = Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.Read(tempFile));
            Assert.True(
                ex.Message.Contains("SQLite Error 26", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("not a database", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("Failed to read", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("Failed to import", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReadFromConnection_NullConnection_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CorneroidsWrlReader.ReadFromConnection(null!));
    }
}
