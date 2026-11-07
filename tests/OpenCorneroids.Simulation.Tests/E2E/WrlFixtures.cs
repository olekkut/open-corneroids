using System.IO.Compression;
using System.Numerics;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.E2E;

/// <summary>
/// Authoritative synthetic fixtures and reference calculators for Corneroids 2012 .wrl SQLite databases.
/// Built strictly from disassembled IL specifications and SQLite 3 schema.
/// </summary>
public static class WrlFixtures
{
    public const double FixedPointScale = 8388608.0; // 2^23

    public static long EncodeFixedPoint(float worldCoord) =>
        (long)Math.Round(worldCoord * FixedPointScale);

    public static float DecodeFixedPoint(long rawCoord) =>
        (float)(rawCoord / FixedPointScale);

    public static SqliteConnection CreateMemoryDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        InitializeSchema(connection);
        return connection;
    }

    public static void InitializeSchema(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
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

CREATE TABLE IF NOT EXISTS spacesectors (
    sectorx INT, 
    sectory INT, 
    sectorz INT, 
    state INT, 
    PRIMARY KEY (sectorx, sectory, sectorz)
);

CREATE TABLE IF NOT EXISTS inventoryItems (
    id INT, 
    type INT, 
    posx FLOAT, 
    posy FLOAT, 
    posz FLOAT, 
    items BLOB, 
    PRIMARY KEY (id, posx, posy, posz)
);

CREATE TABLE IF NOT EXISTS beacons (
    posx LONG, 
    posy LONG, 
    posz LONG, 
    message VARCHAR(20), 
    PRIMARY KEY (posx, posy, posz)
);";
        cmd.ExecuteNonQuery();
    }

    public static void InsertWorldInfo(SqliteConnection connection, string name, float version, int seed, string itemsetKey, long timestamp, string token = "token")
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO worldinfo (name, version, seed, itemsetKey, timestamp, token) VALUES (@n, @v, @s, @i, @t, @tok)";
        cmd.Parameters.AddWithValue("@n", (object?)name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@v", version);
        cmd.Parameters.AddWithValue("@s", seed);
        cmd.Parameters.AddWithValue("@i", (object?)itemsetKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", timestamp);
        cmd.Parameters.AddWithValue("@tok", token);
        cmd.ExecuteNonQuery();
    }

    public static void InsertPlayer(SqliteConnection connection, int id, string name, long posx, long posy, long posz, float orx, float ory, float orz, float orw, int boardedEntity = -1)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO players (id, name, posx, posy, posz, orx, ory, orz, orw, boardedEntity, boundEntities, token) VALUES (@id, @n, @x, @y, @z, @ox, @oy, @oz, @ow, @b, @be, 'tok')";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@x", posx);
        cmd.Parameters.AddWithValue("@y", posy);
        cmd.Parameters.AddWithValue("@z", posz);
        cmd.Parameters.AddWithValue("@ox", orx);
        cmd.Parameters.AddWithValue("@oy", ory);
        cmd.Parameters.AddWithValue("@oz", orz);
        cmd.Parameters.AddWithValue("@ow", orw);
        cmd.Parameters.AddWithValue("@b", boardedEntity);
        cmd.Parameters.AddWithValue("@be", Array.Empty<byte>());
        cmd.ExecuteNonQuery();
    }

    public static void InsertEntity(SqliteConnection connection, int id, bool loaded, long posx, long posy, long posz, float orx, float ory, float orz, float orw, int sectorx = 0, int sectory = 0, int sectorz = 0)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO entities (id, loaded, posx, posy, posz, orx, ory, orz, orw, sectorx, sectory, sectorz) VALUES (@id, @l, @x, @y, @z, @ox, @oy, @oz, @ow, @sx, @sy, @sz)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@l", loaded ? 1 : 0);
        cmd.Parameters.AddWithValue("@x", posx);
        cmd.Parameters.AddWithValue("@y", posy);
        cmd.Parameters.AddWithValue("@z", posz);
        cmd.Parameters.AddWithValue("@ox", orx);
        cmd.Parameters.AddWithValue("@oy", ory);
        cmd.Parameters.AddWithValue("@oz", orz);
        cmd.Parameters.AddWithValue("@ow", orw);
        cmd.Parameters.AddWithValue("@sx", sectorx);
        cmd.Parameters.AddWithValue("@sy", sectory);
        cmd.Parameters.AddWithValue("@sz", sectorz);
        cmd.ExecuteNonQuery();
    }

    public static void InsertBlockSector(SqliteConnection connection, int ownerId, int posx, int posy, int posz, byte[] formationBlob, byte[] blockIdsBlob, byte[] orientationsBlob)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO blocksectors (ownerId, posx, posy, posz, formation, blockIds, orientations) VALUES (@oid, @px, @py, @pz, @f, @b, @o)";
        cmd.Parameters.AddWithValue("@oid", ownerId);
        cmd.Parameters.AddWithValue("@px", posx);
        cmd.Parameters.AddWithValue("@py", posy);
        cmd.Parameters.AddWithValue("@pz", posz);
        cmd.Parameters.AddWithValue("@f", formationBlob);
        cmd.Parameters.AddWithValue("@b", blockIdsBlob);
        cmd.Parameters.AddWithValue("@o", orientationsBlob);
        cmd.ExecuteNonQuery();
    }

    public static void InsertControlBlock(SqliteConnection connection, int id, int bindingId, int ownerEntityId, float posx, float posy, float posz)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO controlblocks (id, bindingId, ownerEntityId, posx, posy, posz) VALUES (@id, @bid, @oeid, @px, @py, @pz)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@bid", bindingId);
        cmd.Parameters.AddWithValue("@oeid", ownerEntityId);
        cmd.Parameters.AddWithValue("@px", posx);
        cmd.Parameters.AddWithValue("@py", posy);
        cmd.Parameters.AddWithValue("@pz", posz);
        cmd.ExecuteNonQuery();
    }

    public static void InsertControlledBlock(SqliteConnection connection, int id, int boundTo, int bindingColor, int ownerEntityId, float posx, float posy, float posz)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO controlledblocks (id, boundTo, bindingColor, ownerEntityId, posx, posy, posz) VALUES (@id, @bt, @bc, @oeid, @px, @py, @pz)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@bt", boundTo);
        cmd.Parameters.AddWithValue("@bc", bindingColor);
        cmd.Parameters.AddWithValue("@oeid", ownerEntityId);
        cmd.Parameters.AddWithValue("@px", posx);
        cmd.Parameters.AddWithValue("@py", posy);
        cmd.Parameters.AddWithValue("@pz", posz);
        cmd.ExecuteNonQuery();
    }

    public static void InsertKeyColorMapping(SqliteConnection connection, int ownerEntityId, int blockId, int color, int key)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO keyColorMappings (ownerEntityId, blockId, color, key) VALUES (@oeid, @bid, @c, @k)";
        cmd.Parameters.AddWithValue("@oeid", ownerEntityId);
        cmd.Parameters.AddWithValue("@bid", blockId);
        cmd.Parameters.AddWithValue("@c", color);
        cmd.Parameters.AddWithValue("@k", key);
        cmd.ExecuteNonQuery();
    }

    public static byte[] CompressGzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    public static byte[] BuildFormationMask(IEnumerable<(int x, int y, int z)> solidCoords)
    {
        var rawMask = new byte[512]; // 4096 bits
        foreach (var (x, y, z) in solidCoords)
        {
            if (x is < 0 or > 15 || y is < 0 or > 15 || z is < 0 or > 15)
                throw new ArgumentOutOfRangeException($"Coords ({x},{y},{z}) outside 16x16x16 chunk");
            int bitPos = z * 256 + y * 16 + x;
            int byteIndex = bitPos >> 3;
            int bitInByte = bitPos & 7;
            rawMask[byteIndex] |= (byte)(0x80 >> bitInByte); // MSB-first
        }
        return rawMask;
    }

    public static byte BuildQuarterTurnOrientationByte(int yaw, int pitch, int roll)
    {
        return (byte)((yaw & 3) | ((pitch & 3) << 2) | ((roll & 3) << 4));
    }

    /// <summary>
    /// Reference Oracle: Computes the 3x3 integer rotation matrix for (yaw, pitch, roll) quarter-turns
    /// and matches against OpenCorneroids Orientation.All (0..23).
    /// </summary>
    public static int DeriveOrientationIndexOracle(byte orientationByte)
    {
        int yaw = orientationByte & 3;
        int pitch = (orientationByte >> 2) & 3;
        int roll = (orientationByte >> 4) & 3;

        // Apply R = R_y(yaw * 90) * R_x(pitch * 90) * R_z(roll * 90)
        // Basis vectors:
        // Initial Up = (0, 1, 0), Initial Forward = (0, 0, 1)
        var up = RotateEulerQuarterTurns(new Vector3(0, 1, 0), yaw, pitch, roll);
        var fwd = RotateEulerQuarterTurns(new Vector3(0, 0, 1), yaw, pitch, roll);

        // Find match in Orientation.All (0..23)
        for (int i = 0; i < Orientation.Count; i++)
        {
            var o = Orientation.FromIndex(i);
            var testUp = o.Apply(new Int3(0, 1, 0));
            var testFwd = o.Apply(new Int3(0, 0, 1));

            if ((int)Math.Round(up.X) == testUp.X &&
                (int)Math.Round(up.Y) == testUp.Y &&
                (int)Math.Round(up.Z) == testUp.Z &&
                (int)Math.Round(fwd.X) == testFwd.X &&
                (int)Math.Round(fwd.Y) == testFwd.Y &&
                (int)Math.Round(fwd.Z) == testFwd.Z)
            {
                return i;
            }
        }
        return 0; // Default fallback
    }

    private static Vector3 RotateEulerQuarterTurns(Vector3 v, int yawQuarter, int pitchQuarter, int rollQuarter)
    {
        // R_z(roll * 90)
        v = RotateZ(v, rollQuarter);
        // R_x(pitch * 90)
        v = RotateX(v, pitchQuarter);
        // R_y(yaw * 90)
        v = RotateY(v, yawQuarter);
        return v;
    }

    private static Vector3 RotateZ(Vector3 v, int quarters)
    {
        quarters = (quarters % 4 + 4) % 4;
        return quarters switch
        {
            1 => new Vector3(-v.Y, v.X, v.Z),
            2 => new Vector3(-v.X, -v.Y, v.Z),
            3 => new Vector3(v.Y, -v.X, v.Z),
            _ => v
        };
    }

    private static Vector3 RotateX(Vector3 v, int quarters)
    {
        quarters = (quarters % 4 + 4) % 4;
        return quarters switch
        {
            1 => new Vector3(v.X, -v.Z, v.Y),
            2 => new Vector3(v.X, -v.Y, -v.Z),
            3 => new Vector3(v.X, v.Z, -v.Y),
            _ => v
        };
    }

    private static Vector3 RotateY(Vector3 v, int quarters)
    {
        quarters = (quarters % 4 + 4) % 4;
        return quarters switch
        {
            1 => new Vector3(v.Z, v.Y, -v.X),
            2 => new Vector3(-v.X, v.Y, -v.Z),
            3 => new Vector3(-v.Z, v.Y, v.X),
            _ => v
        };
    }
}
