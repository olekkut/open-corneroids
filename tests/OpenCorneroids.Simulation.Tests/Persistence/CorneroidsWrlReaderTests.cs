using System.IO.Compression;
using System.Numerics;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.Persistence;

public sealed class CorneroidsWrlFixtureBuilder : IDisposable
{
    private readonly SqliteConnection _connection;

    public CorneroidsWrlFixtureBuilder()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Pooling=false");
        _connection.Open();
        CreateSchema();
    }

    public SqliteConnection Connection => _connection;

    private void CreateSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS worldinfo (
                name VARCHAR(50),
                version FLOAT,
                seed INT,
                itemsetKey VARCHAR(50),
                timestamp LONG,
                token VARCHAR(20)
            );
            CREATE TABLE IF NOT EXISTS players (
                id INT PRIMARY KEY,
                name VARCHAR(50),
                posx LONG,
                posy LONG,
                posz LONG,
                orx FLOAT,
                ory FLOAT,
                orz FLOAT,
                orw FLOAT,
                boardedEntity INT,
                boundEntities BLOB,
                token VARCHAR(20)
            );
            CREATE TABLE IF NOT EXISTS entities (
                id INT PRIMARY KEY,
                loaded BOOLEAN,
                posx LONG,
                posy LONG,
                posz LONG,
                orx FLOAT,
                ory FLOAT,
                orz FLOAT,
                orw FLOAT,
                sectorx INT,
                sectory INT,
                sectorz INT
            );
            CREATE TABLE IF NOT EXISTS blocksectors (
                ownerId INT,
                posx INT,
                posy INT,
                posz INT,
                formation BLOB,
                blockIds BLOB,
                orientations BLOB,
                PRIMARY KEY (ownerId, posx, posy, posz)
            );
            CREATE TABLE IF NOT EXISTS controlledblocks (
                id INT,
                boundTo INT,
                bindingColor INT,
                ownerEntityId INT,
                posx FLOAT,
                posy FLOAT,
                posz FLOAT
            );
            CREATE TABLE IF NOT EXISTS controlblocks (
                id INT,
                bindingId INT,
                ownerEntityId INT,
                posx FLOAT,
                posy FLOAT,
                posz FLOAT
            );
            CREATE TABLE IF NOT EXISTS keyColorMappings (
                ownerEntityId INT,
                blockId INT,
                color INT,
                key INT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public CorneroidsWrlFixtureBuilder WithWorldInfo(string name = "Test World", float version = 1.2f, int seed = 1234, string itemsetKey = "DefaultItemset", long timestamp = 1350000000000L)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO worldinfo VALUES (@name, @version, @seed, @itemsetKey, @timestamp, 'token');";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@seed", seed);
        cmd.Parameters.AddWithValue("@itemsetKey", itemsetKey);
        cmd.Parameters.AddWithValue("@timestamp", timestamp);
        cmd.ExecuteNonQuery();
        return this;
    }

    public CorneroidsWrlFixtureBuilder WithPlayer(int id = 1, string name = "Astronaut", long posx = 83886080L, long posy = 0L, long posz = 0L,
        float orx = 0f, float ory = 0f, float orz = 0f, float orw = 1f, int boardedEntity = 0)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO players VALUES (@id, @name, @posx, @posy, @posz, @orx, @ory, @orz, @orw, @boarded, null, 'token');";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@posx", posx);
        cmd.Parameters.AddWithValue("@posy", posy);
        cmd.Parameters.AddWithValue("@posz", posz);
        cmd.Parameters.AddWithValue("@orx", orx);
        cmd.Parameters.AddWithValue("@ory", ory);
        cmd.Parameters.AddWithValue("@orz", orz);
        cmd.Parameters.AddWithValue("@orw", orw);
        cmd.Parameters.AddWithValue("@boarded", boardedEntity);
        cmd.ExecuteNonQuery();
        return this;
    }

    public CorneroidsWrlFixtureBuilder WithEntity(int id = 1, long posx = 0L, long posy = 0L, long posz = 0L,
        float orx = 0f, float ory = 0f, float orz = 0f, float orw = 1f, bool loaded = true, int sectorx = 0, int sectory = 0, int sectorz = 0)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO entities VALUES (@id, @loaded, @posx, @posy, @posz, @orx, @ory, @orz, @orw, @sectorx, @sectory, @sectorz);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@loaded", loaded ? 1 : 0);
        cmd.Parameters.AddWithValue("@posx", posx);
        cmd.Parameters.AddWithValue("@posy", posy);
        cmd.Parameters.AddWithValue("@posz", posz);
        cmd.Parameters.AddWithValue("@orx", orx);
        cmd.Parameters.AddWithValue("@ory", ory);
        cmd.Parameters.AddWithValue("@orz", orz);
        cmd.Parameters.AddWithValue("@orw", orw);
        cmd.Parameters.AddWithValue("@sectorx", sectorx);
        cmd.Parameters.AddWithValue("@sectory", sectory);
        cmd.Parameters.AddWithValue("@sectorz", sectorz);
        cmd.ExecuteNonQuery();
        return this;
    }

    public CorneroidsWrlFixtureBuilder WithSingleBlockSector(int ownerId, int sectorX, int sectorY, int sectorZ,
        int localX, int localY, int localZ, byte blockId, byte orientationByte)
    {
        byte[] formationRaw = new byte[512];
        int bitPos = (localZ * 16 + localY) * 16 + localX;
        formationRaw[bitPos >> 3] |= (byte)(0x80 >> (bitPos & 7));

        byte[] formationCompressed = GzipCompress(formationRaw);
        byte[] blockIdsCompressed = GzipCompress([blockId]);
        byte[] orientationsCompressed = GzipCompress([orientationByte]);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO blocksectors VALUES (@ownerId, @posx, @posy, @posz, @formation, @blockIds, @orientations);";
        cmd.Parameters.AddWithValue("@ownerId", ownerId);
        cmd.Parameters.AddWithValue("@posx", sectorX);
        cmd.Parameters.AddWithValue("@posy", sectorY);
        cmd.Parameters.AddWithValue("@posz", sectorZ);
        cmd.Parameters.AddWithValue("@formation", formationCompressed);
        cmd.Parameters.AddWithValue("@blockIds", blockIdsCompressed);
        cmd.Parameters.AddWithValue("@orientations", orientationsCompressed);
        cmd.ExecuteNonQuery();
        return this;
    }

    public CorneroidsWrlFixtureBuilder WithMultiBlockSector(int ownerId, int sectorX, int sectorY, int sectorZ,
        IEnumerable<(int x, int y, int z)> solidCoords, byte[] blockIds, byte[] orientations)
    {
        byte[] formationRaw = new byte[512];
        foreach (var (x, y, z) in solidCoords)
        {
            int bitPos = (z * 16 + y) * 16 + x;
            formationRaw[bitPos >> 3] |= (byte)(0x80 >> (bitPos & 7));
        }

        byte[] formationCompressed = GzipCompress(formationRaw);
        byte[] blockIdsCompressed = GzipCompress(blockIds);
        byte[] orientationsCompressed = GzipCompress(orientations);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO blocksectors VALUES (@ownerId, @posx, @posy, @posz, @formation, @blockIds, @orientations);";
        cmd.Parameters.AddWithValue("@ownerId", ownerId);
        cmd.Parameters.AddWithValue("@posx", sectorX);
        cmd.Parameters.AddWithValue("@posy", sectorY);
        cmd.Parameters.AddWithValue("@posz", sectorZ);
        cmd.Parameters.AddWithValue("@formation", formationCompressed);
        cmd.Parameters.AddWithValue("@blockIds", blockIdsCompressed);
        cmd.Parameters.AddWithValue("@orientations", orientationsCompressed);
        cmd.ExecuteNonQuery();
        return this;
    }

    public CorneroidsWrlFixtureBuilder WithRawSector(int ownerId, int sectorX, int sectorY, int sectorZ,
        byte[] formationBlob, byte[] blockIdsBlob, byte[] orientationsBlob)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO blocksectors VALUES (@ownerId, @posx, @posy, @posz, @formation, @blockIds, @orientations);";
        cmd.Parameters.AddWithValue("@ownerId", ownerId);
        cmd.Parameters.AddWithValue("@posx", sectorX);
        cmd.Parameters.AddWithValue("@posy", sectorY);
        cmd.Parameters.AddWithValue("@posz", sectorZ);
        cmd.Parameters.AddWithValue("@formation", formationBlob);
        cmd.Parameters.AddWithValue("@blockIds", blockIdsBlob);
        cmd.Parameters.AddWithValue("@orientations", orientationsBlob);
        cmd.ExecuteNonQuery();
        return this;
    }

    public CorneroidsWrlFixtureBuilder WithConsole(int id, int ownerEntityId, float posX, float posY, float posZ, int bindingId = 0)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO controlblocks VALUES (@id, @bindingId, @ownerEntityId, @posx, @posy, @posz);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@bindingId", bindingId);
        cmd.Parameters.AddWithValue("@ownerEntityId", ownerEntityId);
        cmd.Parameters.AddWithValue("@posx", posX);
        cmd.Parameters.AddWithValue("@posy", posY);
        cmd.Parameters.AddWithValue("@posz", posZ);
        cmd.ExecuteNonQuery();
        return this;
    }

    public CorneroidsWrlFixtureBuilder WithControlledBlock(int id, int boundTo, int bindingColor, int ownerEntityId, float posX, float posY, float posZ)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO controlledblocks VALUES (@id, @boundTo, @bindingColor, @ownerEntityId, @posx, @posy, @posz);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@boundTo", boundTo);
        cmd.Parameters.AddWithValue("@bindingColor", bindingColor);
        cmd.Parameters.AddWithValue("@ownerEntityId", ownerEntityId);
        cmd.Parameters.AddWithValue("@posx", posX);
        cmd.Parameters.AddWithValue("@posy", posY);
        cmd.Parameters.AddWithValue("@posz", posZ);
        cmd.ExecuteNonQuery();
        return this;
    }

    public CorneroidsWrlFixtureBuilder WithKeyMapping(int ownerEntityId, int blockId, int color, int xnaKeyCode)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO keyColorMappings VALUES (@ownerEntityId, @blockId, @color, @key);";
        cmd.Parameters.AddWithValue("@ownerEntityId", ownerEntityId);
        cmd.Parameters.AddWithValue("@blockId", blockId);
        cmd.Parameters.AddWithValue("@color", color);
        cmd.Parameters.AddWithValue("@key", xnaKeyCode);
        cmd.ExecuteNonQuery();
        return this;
    }

    public static byte[] GzipCompress(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(raw);
        return ms.ToArray();
    }

    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
    }
}

public class CorneroidsWrlReaderTests
{
    [Fact]
    public void ReadFromConnection_MetadataExtractedAccurately()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        long ts = 1690000000000L;
        fixture.WithWorldInfo("DeepSpaceOne", 1.2f, 98765, "CustomItemset", ts);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);

        Assert.Equal("DeepSpaceOne", snapshot.Name);
        Assert.Equal(98765, snapshot.Seed);
        Assert.Equal("CustomItemset", snapshot.Itemset.Key);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(ts), snapshot.SavedAt);
    }

    [Fact]
    public void ReadFromConnection_PlayerCoordinatesAndEulerOrientation()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();

        // 83886080L = 10.0f, -41943040L = -5.0f, 0L = 0.0f
        // Rotation: 90 deg Yaw about Y axis: q = (0, sin(45), 0, cos(45))
        float s = MathF.Sin(MathF.PI / 4f);
        float c = MathF.Cos(MathF.PI / 4f);
        fixture.WithPlayer(1, "Commander", 83886080L, -41943040L, 0L, 0f, s, 0f, c);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);

        Assert.Equal(10.0f, snapshot.Player.Position.X, 0.001f);
        Assert.Equal(-5.0f, snapshot.Player.Position.Y, 0.001f);
        Assert.Equal(0.0f, snapshot.Player.Position.Z, 0.001f);
        Assert.Equal(MathF.PI / 2f, snapshot.Player.Yaw, 0.01f);
        Assert.Equal(0.0f, snapshot.Player.Pitch, 0.01f);
        Assert.Equal(0.0f, snapshot.Player.Roll, 0.01f);
    }

    [Fact]
    public void ReadFromConnection_VoxelChunkDecompression_MsbFirstBitmask()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);

        // Three voxels: (0, 0, 0) [bit 0], (7, 0, 0) [bit 7], (15, 15, 15) [bit 4095]
        var coords = new[] { (0, 0, 0), (7, 0, 0), (15, 15, 15) };
        var blockIds = new byte[] { 10, 20, 30 };
        var orientations = new byte[] { 0, 0, 0 };

        fixture.WithMultiBlockSector(1, 0, 0, 0, coords, blockIds, orientations);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);

        Assert.Single(snapshot.Ships);
        var ship = snapshot.Ships[0];
        Assert.Equal(16, ship.Size);

        // Cell indices in 16x16x16 grid:
        int idx0 = WorldSnapshot.CellIndex(16, 0, 0, 0);
        int idx7 = WorldSnapshot.CellIndex(16, 7, 0, 0);
        int idx15 = WorldSnapshot.CellIndex(16, 15, 15, 15);

        Assert.NotEqual(WorldSnapshot.Air, ship.Cells[idx0]);
        Assert.NotEqual(WorldSnapshot.Air, ship.Cells[idx7]);
        Assert.NotEqual(WorldSnapshot.Air, ship.Cells[idx15]);

        Assert.Equal(10, ship.Palette[ship.Cells[idx0]]);
        Assert.Equal(20, ship.Palette[ship.Cells[idx7]]);
        Assert.Equal(30, ship.Palette[ship.Cells[idx15]]);
    }

    [Fact]
    public void ReadFromConnection_QuarterTurnOrientations_MappedToCubeRotations()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);

        // Block at (0, 0, 0) with orientation 0 (identity) -> must NOT emit TurnedCell
        // Block at (1, 0, 0) with yaw = 1 (orient byte 1) -> must emit TurnedCell with orientation != 0
        var coords = new[] { (0, 0, 0), (1, 0, 0) };
        var blockIds = new byte[] { 5, 6 };
        var orientations = new byte[] { 0, 1 };

        fixture.WithMultiBlockSector(1, 0, 0, 0, coords, blockIds, orientations);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var ship = snapshot.Ships[0];

        // Only block at (1, 0, 0) should have TurnedCell
        Assert.Single(ship.Turned);
        Assert.Equal(new Int3(1, 0, 0), ship.Turned[0].Cell);
        Assert.True(ship.Turned[0].Orientation >= 1 && ship.Turned[0].Orientation < 24);
    }

    [Fact]
    public void ReadFromConnection_EntityGridShift_CompensatesWorldPosition()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();

        // Entity at world position X = 100.0 (838860800L) with 90° Yaw rotation about Y
        long rawX = (long)(100.0 * CorneroidsWrlReader.PositionScale);
        float s = MathF.Sin(MathF.PI / 4f);
        float c = MathF.Cos(MathF.PI / 4f);
        var rot = new Quaternion(0f, s, 0f, c);

        fixture.WithEntity(1, rawX, 0L, 0L, 0f, s, 0f, c);

        // Sector at (-1, 0, 0) with voxel at local (0, 0, 0) -> entity local X = -16
        fixture.WithSingleBlockSector(1, -1, 0, 0, 0, 0, 0, 12, 0);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var ship = snapshot.Ships[0];

        // Local voxel should be shifted to local cell (0, 0, 0)
        int cellIdx = WorldSnapshot.CellIndex(ship.Size, 0, 0, 0);
        Assert.NotEqual(WorldSnapshot.Air, ship.Cells[cellIdx]);

        // World position compensation:
        // shift is (-16, 0, 0). Transformed by 90° Yaw (+X becomes -Z in standard basis):
        // Position should equal original worldPos + Transform(shift, rot)
        var expectedPos = new Vector3(100f, 0f, 0f) + Vector3.Transform(new Vector3(-16f, 0f, 0f), rot);
        Assert.Equal(expectedPos.X, ship.Position.X, 0.001f);
        Assert.Equal(expectedPos.Y, ship.Position.Y, 0.001f);
        Assert.Equal(expectedPos.Z, ship.Position.Z, 0.001f);
    }

    [Fact]
    public void ReadFromConnection_ConsoleBindingsAndKeyMappings()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);

        // Single block at (0, 0, 0)
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0);

        // Console at (2, 0, 0)
        fixture.WithConsole(10, 1, 2f, 0f, 0f);

        // Controlled block at (5, 0, 0) bound to console 10 under color 0 (Red)
        fixture.WithControlledBlock(100, 10, 0, 1, 5f, 0f, 0f);

        // Controlled block at (6, 0, 0) bound to console 10 under color 1 (Green)
        fixture.WithControlledBlock(101, 10, 1, 1, 6f, 0f, 0f);

        // Key mapping: color 0 assigned XNA Space (32), color 1 assigned XNA W (87)
        fixture.WithKeyMapping(1, 10, 0, 32);
        fixture.WithKeyMapping(1, 10, 1, 87);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var ship = snapshot.Ships[0];

        Assert.Single(ship.Consoles);
        var console = ship.Consoles[0];
        Assert.Equal(new Int3(2, 0, 0), console.Cell);
        Assert.Equal(2, console.Groups.Count);

        var redGroup = console.Groups.FirstOrDefault(g => g.Index == 0);
        Assert.NotNull(redGroup);
        Assert.Equal("Space", redGroup.Key);
        Assert.Single(redGroup.Targets);
        Assert.Equal(new Int3(5, 0, 0), redGroup.Targets[0]);

        var greenGroup = console.Groups.FirstOrDefault(g => g.Index == 1);
        Assert.NotNull(greenGroup);
        Assert.Equal("W", greenGroup.Key);
        Assert.Single(greenGroup.Targets);
        Assert.Equal(new Int3(6, 0, 0), greenGroup.Targets[0]);
    }

    [Fact]
    public void ReadFromConnection_ConsoleCameraBinding()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0);

        // Console with bindingId = 99 (camera block ID)
        fixture.WithConsole(10, 1, 1f, 0f, 0f, bindingId: 99);

        // Camera block at (0, 4, 0) with id = 99
        fixture.WithControlledBlock(99, 10, 0, 1, 0f, 4f, 0f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var console = snapshot.Ships[0].Consoles[0];

        Assert.NotNull(console.Camera);
        Assert.Equal(new Int3(0, 4, 0), console.Camera.Value);
    }

    [Fact]
    public void ReadFromConnection_CorruptFormationBlob_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);

        // Invalid formation (only 10 bytes instead of 512)
        byte[] truncated = CorneroidsWrlFixtureBuilder.GzipCompress(new byte[10]);
        fixture.WithRawSector(1, 0, 0, 0, truncated, CorneroidsWrlFixtureBuilder.GzipCompress([1]), CorneroidsWrlFixtureBuilder.GzipCompress([0]));

        Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
    }

    [Fact]
    public void ReadFromConnection_CorruptBlockIdsBlob_ThrowsWorldSaveException()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);

        // Formation with 2 solid voxels, but blockIds compressed blob has only 1 byte
        byte[] rawFormation = new byte[512];
        rawFormation[0] = 0xC0; // 2 solid voxels
        byte[] formationGzip = CorneroidsWrlFixtureBuilder.GzipCompress(rawFormation);
        byte[] shortBlockIds = CorneroidsWrlFixtureBuilder.GzipCompress([1]);
        byte[] orientations = CorneroidsWrlFixtureBuilder.GzipCompress([0, 0]);

        fixture.WithRawSector(1, 0, 0, 0, formationGzip, shortBlockIds, orientations);

        Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(fixture.Connection));
    }

    [Fact]
    public void ReadFromConnection_MissingWorldinfoTable_ThrowsWorldSaveException()
    {
        using var conn = new SqliteConnection("Data Source=:memory:;Pooling=false");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE dummy (id INT);";
            cmd.ExecuteNonQuery();
        }

        Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.ReadFromConnection(conn));
    }

    [Fact]
    public void Read_NonExistentFile_ThrowsFileNotFoundException()
    {
        string fakePath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.wrl");
        Assert.Throws<FileNotFoundException>(() => CorneroidsWrlReader.Read(fakePath));
    }

    [Fact]
    public void ReadFromConnection_OcworldFullRoundTripCompatibility()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo("RoundTripGalaxy", 1.2f, 777, "DefaultItemset", 1700000000000L);
        fixture.WithPlayer(1, "Pilot", 83886080L, 0L, 0L, 0f, 0f, 0f, 1f);
        fixture.WithEntity(1, 83886080L, 0L, 0L, 0f, 0f, 0f, 1f);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 2, 2, 2, 42, 1);
        fixture.WithConsole(10, 1, 2f, 2f, 2f);
        fixture.WithControlledBlock(100, 10, 0, 1, 2f, 2f, 2f);
        fixture.WithKeyMapping(1, 10, 0, 32);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);

        // Serialize snapshot to .ocworld binary format
        using var stream = new MemoryStream();
        WorldSaveFormat.Write(stream, snapshot);

        // Deserialize back from .ocworld
        stream.Position = 0;
        var restored = WorldSaveFormat.Read(stream);

        Assert.Equal(snapshot.Name, restored.Name);
        Assert.Equal(snapshot.Seed, restored.Seed);
        Assert.Equal(snapshot.Player.Position.X, restored.Player.Position.X, 0.001f);
        Assert.Single(restored.Ships);
        Assert.Single(restored.Ships[0].Turned);
        Assert.Single(restored.Ships[0].Consoles);
        Assert.Equal("Space", restored.Ships[0].Consoles[0].Groups[0].Key);
    }

    [Fact]
    public void ReadFromConnection_OutOfBoundsConsoleTarget_FilteredOutSafely()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 0, 0, 0, 1, 0);
        fixture.WithConsole(10, 1, 0f, 0f, 0f);

        // Target out of grid bounds (9999, 9999, 9999)
        fixture.WithControlledBlock(100, 10, 0, 1, 9999f, 9999f, 9999f);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var console = snapshot.Ships[0].Consoles[0];

        // Out-of-bounds target must be filtered out, leaving groups empty
        Assert.Empty(console.Groups);
    }

    [Fact]
    public void XnaKeyResolver_AllKeyCategories_ResolvedCorrectly()
    {
        Assert.Equal("None", CorneroidsWrlReader.ResolveXnaKey(0));
        Assert.Equal("Space", CorneroidsWrlReader.ResolveXnaKey(32));
        Assert.Equal("W", CorneroidsWrlReader.ResolveXnaKey(87));
        Assert.Equal("A", CorneroidsWrlReader.ResolveXnaKey(65));
        Assert.Equal("0", CorneroidsWrlReader.ResolveXnaKey(48));
        Assert.Equal("9", CorneroidsWrlReader.ResolveXnaKey(57));
        Assert.Equal("F1", CorneroidsWrlReader.ResolveXnaKey(112));
        Assert.Equal("F12", CorneroidsWrlReader.ResolveXnaKey(123));
        Assert.Equal("LeftShift", CorneroidsWrlReader.ResolveXnaKey(160));
        Assert.Equal("LeftControl", CorneroidsWrlReader.ResolveXnaKey(162));
        Assert.Equal("None", CorneroidsWrlReader.ResolveXnaKey(99999));
    }

    [Fact]
    public void ReadSummary_returns_summary_correctly()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo("Test World", 1.2f, 1337, "CustomSet", 1600000000000L);
        var summary = CorneroidsWrlReader.ReadSummaryFromConnection(fixture.Connection, "test.wrl");
        Assert.Equal("Test World", summary.Name);
        Assert.Equal("CustomSet", summary.Itemset.Key);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1600000000000L), summary.SavedAt);
    }

    [Fact]
    public void Verify_accepts_valid_database_and_throws_for_missing_tables()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.wrl");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={tempFile};Pooling=false"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE worldinfo (name VARCHAR(50));";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            // Missing players, entities, blocksectors
            Assert.Throws<WorldSaveException>(() => CorneroidsWrlReader.Verify(tempFile));

            // Complete schema
            using (var conn = new SqliteConnection($"Data Source={tempFile};Pooling=false"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE players (id INT PRIMARY KEY);
                    CREATE TABLE entities (id INT PRIMARY KEY);
                    CREATE TABLE blocksectors (id INT PRIMARY KEY);
                ";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            // Now valid
            CorneroidsWrlReader.Verify(tempFile);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Entity_WithOnlyNaturalRockAndOres_IsClassifiedAsAsteroid()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder()
            .WithWorldInfo("AsteroidField")
            .WithPlayer(boardedEntity: -1)
            .WithEntity(id: 1)
            .WithSingleBlockSector(ownerId: 1, 0, 0, 0, 0, 0, 0, blockId: 0, orientationByte: 0); // 0 = Light rock

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var entity = Assert.Single(snapshot.Ships);
        Assert.True(entity.IsAsteroid);
        Assert.Equal("Asteroid", entity.Name);
    }

    [Fact]
    public void Entity_WithControlBlocks_IsClassifiedAsShip()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder()
            .WithWorldInfo("ShipWorld")
            .WithPlayer(boardedEntity: 2)
            .WithEntity(id: 2)
            .WithSingleBlockSector(ownerId: 2, 0, 0, 0, 0, 0, 0, blockId: 7, orientationByte: 0) // 7 = Console
            .WithConsole(id: 1, ownerEntityId: 2, 0, 0, 0);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var entity = Assert.Single(snapshot.Ships);
        Assert.False(entity.IsAsteroid);
        Assert.Null(entity.Name);
    }

    [Fact]
    public void Entity_WithShipBlocksEvenWithoutControlBlocks_IsClassifiedAsShip()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder()
            .WithWorldInfo("DroneWorld")
            .WithPlayer(boardedEntity: -1)
            .WithEntity(id: 3)
            .WithSingleBlockSector(ownerId: 3, 0, 0, 0, 0, 0, 0, blockId: 19, orientationByte: 0); // 19 = Light aluminium hull

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var entity = Assert.Single(snapshot.Ships);
        Assert.False(entity.IsAsteroid);
    }

    [Fact]
    public void Read_RealWorldDatabases_CorrectlySeparatesShipsAndAsteroids()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var adjusterPath = Path.Combine(appData, "OpenCorneroids", "worlds", "Adjuster.wrl");
        if (!File.Exists(adjusterPath)) return;

        var snapshot = CorneroidsWrlReader.Read(adjusterPath);
        var realShips = snapshot.Ships.Where(s => !s.IsAsteroid).ToList();
        var asteroids = snapshot.Ships.Where(s => s.IsAsteroid).ToList();

        Assert.Single(realShips);
        Assert.Equal(19, asteroids.Count);
        Assert.All(asteroids, a => Assert.Equal("Asteroid", a.Name));
    }
}
