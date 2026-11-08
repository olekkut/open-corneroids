using System.IO.Compression;
using System.Numerics;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Persistence;

/// <summary>
/// Quarter-turn chiral orientation math for legacy 2012 Corneroids blocks.
/// Unpacks yaw (bits 0..1), pitch (bits 2..3), and roll (bits 4..5) quarter-turns (90° steps)
/// and resolves them to the 24 proper rotations of a cube in OpenCorneroids (<see cref="Orientation.All"/>).
/// </summary>
public static class CorneroidsOrientation
{
    private static readonly byte[] QuarterTurnToOrientation = BuildLookupTable();

    public static int FromByte(byte b) => QuarterTurnToOrientation[b];

    private static byte[] BuildLookupTable()
    {
        var table = new byte[256];
        for (int b = 0; b < 256; b++)
        {
            int yaw = b & 3;
            int pitch = (b >> 2) & 3;
            int roll = (b >> 4) & 3;

            // R = R_y(yaw * 90) * R_x(pitch * 90) * R_z(roll * 90)
            // Initial basis: Up = (0, 1, 0), Forward = (0, 0, 1)
            var up = RotateEulerQuarterTurns(new Vector3(0, 1, 0), yaw, pitch, roll);
            var fwd = RotateEulerQuarterTurns(new Vector3(0, 0, 1), yaw, pitch, roll);

            byte match = 0;
            for (int i = 0; i < Orientation.Count; i++)
            {
                var o = Orientation.FromIndex(i);
                var testUp = o.Apply(new Int3(0, 1, 0));
                var testFwd = o.Apply(new Int3(0, 0, 1));

                if ((int)MathF.Round(up.X) == testUp.X &&
                    (int)MathF.Round(up.Y) == testUp.Y &&
                    (int)MathF.Round(up.Z) == testUp.Z &&
                    (int)MathF.Round(fwd.X) == testFwd.X &&
                    (int)MathF.Round(fwd.Y) == testFwd.Y &&
                    (int)MathF.Round(fwd.Z) == testFwd.Z)
                {
                    match = (byte)i;
                    break;
                }
            }
            table[b] = match;
        }
        return table;
    }

    private static Vector3 RotateEulerQuarterTurns(Vector3 v, int yawQuarter, int pitchQuarter, int rollQuarter)
    {
        v = RotateZ(v, rollQuarter);
        v = RotateX(v, pitchQuarter);
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
            _ => v,
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
            _ => v,
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
            _ => v,
        };
    }
}

/// <summary>
/// Deserializes legacy 2012 Corneroids SQLite 3 world databases (.wrl) into <see cref="WorldSnapshot"/> instances.
/// Preserves native OpenCorneroids persistence without modifying .ocworld.
/// </summary>
public static class CorneroidsWrlReader
{
    public const double PositionScale = 8388608.0; // 2^23 fixed-point multiplier

    public static float DecodeCoord(long raw) => (float)(raw / PositionScale);

    public static Vector3 DecodePosition(long rawX, long rawY, long rawZ) =>
        new(DecodeCoord(rawX), DecodeCoord(rawY), DecodeCoord(rawZ));

    /// <summary>
    /// Reads summary metadata from a Corneroids 2012 .wrl SQLite world file without decompressing or parsing chunks.
    /// </summary>
    public static WorldSummary ReadSummary(string sqliteFilePath, string? defaultItemsetKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqliteFilePath);
        if (!File.Exists(sqliteFilePath))
            throw new FileNotFoundException($"Corneroids world database '{sqliteFilePath}' was not found.", sqliteFilePath);

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = sqliteFilePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        try
        {
            using var connection = new SqliteConnection(csb.ToString());
            connection.Open();
            return ReadSummaryFromConnection(connection, sqliteFilePath, defaultItemsetKey);
        }
        catch (SqliteException ex)
        {
            SqliteConnection.ClearAllPools();
            throw new WorldSaveException($"Failed to read Corneroids database summary '{sqliteFilePath}': {ex.Message}", ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Reads summary metadata via an open <see cref="SqliteConnection"/>. Leaves caller connection open.
    /// </summary>
    public static WorldSummary ReadSummaryFromConnection(SqliteConnection connection, string sqliteFilePath, string? defaultItemsetKey = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!TableExists(connection, "worldinfo"))
            throw new WorldSaveException("Invalid Corneroids world: missing 'worldinfo' table.");

        string worldName = Path.GetFileNameWithoutExtension(sqliteFilePath);
        string itemsetKey = defaultItemsetKey ?? "Default";
        DateTimeOffset savedAt = DateTimeOffset.UnixEpoch;

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name, version, seed, itemsetKey, timestamp FROM worldinfo LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    var rawName = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(rawName)) worldName = rawName;
                }

                if (!reader.IsDBNull(3))
                {
                    string? rawKey = reader.GetString(3);
                    if (!string.IsNullOrWhiteSpace(rawKey))
                        itemsetKey = rawKey;
                }

                if (!reader.IsDBNull(4))
                {
                    long ts = reader.GetInt64(4);
                    savedAt = ts > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ts) : DateTimeOffset.UnixEpoch;
                }
            }
        }

        var itemsetBinding = new ItemsetBinding(itemsetKey, itemsetKey, itemsetKey, "legacy-corneroids-2012");
        return new WorldSummary(worldName, savedAt, itemsetBinding, Path.GetFullPath(sqliteFilePath)) { Creative = false };
    }

    /// <summary>
    /// Verifies the SQLite database integrity and required tables of a Corneroids 2012 .wrl file.
    /// </summary>
    public static void Verify(string sqliteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqliteFilePath);
        if (!File.Exists(sqliteFilePath))
            throw new FileNotFoundException($"Corneroids world database '{sqliteFilePath}' was not found.", sqliteFilePath);

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = sqliteFilePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        try
        {
            using var connection = new SqliteConnection(csb.ToString());
            connection.Open();
            if (!TableExists(connection, "worldinfo"))
                throw new WorldSaveException("Invalid Corneroids world: missing 'worldinfo' table.");
            if (!TableExists(connection, "players"))
                throw new WorldSaveException("Invalid Corneroids world: missing 'players' table.");
            if (!TableExists(connection, "entities"))
                throw new WorldSaveException("Invalid Corneroids world: missing 'entities' table.");
            if (!TableExists(connection, "blocksectors"))
                throw new WorldSaveException("Invalid Corneroids world: missing 'blocksectors' table.");

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check(1);";
            var result = cmd.ExecuteScalar()?.ToString();
            if (result != "ok")
                throw new WorldSaveException($"Corneroids database integrity check failed: {result}");
        }
        catch (SqliteException ex)
        {
            SqliteConnection.ClearAllPools();
            throw new WorldSaveException($"Failed to verify Corneroids database '{sqliteFilePath}': {ex.Message}", ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Reads a Corneroids 2012 .wrl SQLite world file from disk into a <see cref="WorldSnapshot"/>.
    /// </summary>
    public static WorldSnapshot Read(string sqliteFilePath, string? defaultItemsetKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqliteFilePath);
        if (!File.Exists(sqliteFilePath))
            throw new FileNotFoundException($"Corneroids world database '{sqliteFilePath}' was not found.", sqliteFilePath);

        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = sqliteFilePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        try
        {
            using var connection = new SqliteConnection(csb.ToString());
            connection.Open();
            return ReadFromConnection(connection, defaultItemsetKey);
        }
        catch (SqliteException ex)
        {
            SqliteConnection.ClearAllPools();
            throw new WorldSaveException($"Failed to read Corneroids database '{sqliteFilePath}': {ex.Message}", ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Deserializes a Corneroids 2012 .wrl SQLite database via an existing <see cref="SqliteConnection"/>
    /// into a <see cref="WorldSnapshot"/>. Leaves caller-opened connections open.
    /// </summary>
    public static WorldSnapshot ReadFromConnection(SqliteConnection connection, string? defaultItemsetKey = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        bool openedLocally = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
            openedLocally = true;
        }

        try
        {
            return ReadInternal(connection, defaultItemsetKey);
        }
        catch (WorldSaveException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new WorldSaveException($"Failed to import Corneroids .wrl database: {ex.Message}", ex);
        }
        finally
        {
            if (openedLocally)
            {
                connection.Close();
                SqliteConnection.ClearAllPools();
            }
        }
    }

    private static WorldSnapshot ReadInternal(SqliteConnection connection, string? defaultItemsetKey)
    {
        // 1. worldinfo table
        if (!TableExists(connection, "worldinfo"))
            throw new WorldSaveException("Invalid Corneroids world: missing 'worldinfo' table.");

        string worldName = "Imported World";
        int seed = 0;
        string itemsetKey = defaultItemsetKey ?? "Default";
        DateTimeOffset savedAt = DateTimeOffset.UnixEpoch;

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name, version, seed, itemsetKey, timestamp FROM worldinfo LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    worldName = reader.GetString(0);

                if (!reader.IsDBNull(2))
                    seed = reader.GetInt32(2);

                if (!reader.IsDBNull(3))
                {
                    string? rawKey = reader.GetString(3);
                    if (!string.IsNullOrWhiteSpace(rawKey))
                        itemsetKey = rawKey;
                }

                if (!reader.IsDBNull(4))
                {
                    long ts = reader.GetInt64(4);
                    savedAt = ts > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ts) : DateTimeOffset.UnixEpoch;
                }
            }
        }

        var itemsetBinding = new ItemsetBinding(itemsetKey, itemsetKey, itemsetKey, "legacy-corneroids-2012");

        // 2. players table
        PlayerState player = new(Vector3.Zero, Vector3.Zero, 0f, 0f);
        if (TableExists(connection, "players"))
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT posx, posy, posz, orx, ory, orz, orw FROM players ORDER BY id LIMIT 1;";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                long px = reader.GetInt64(0);
                long py = reader.GetInt64(1);
                long pz = reader.GetInt64(2);
                float ox = reader.GetFloat(3);
                float oy = reader.GetFloat(4);
                float oz = reader.GetFloat(5);
                float ow = reader.GetFloat(6);

                Vector3 playerPos = DecodePosition(px, py, pz);
                Quaternion rot = NormalizeOrDefault(new Quaternion(ox, oy, oz, ow));
                var (pitch, yaw, roll) = QuaternionToEulerYxz(rot);
                player = new PlayerState(playerPos, Vector3.Zero, pitch, yaw) { Roll = roll };
            }
        }

        // 3. entities & blocksectors
        var ships = new List<ShipSnapshot>();
        if (TableExists(connection, "entities"))
        {
            var entitiesWithControls = new HashSet<int>();
            if (TableExists(connection, "controlblocks"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT ownerEntityId FROM controlblocks;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) entitiesWithControls.Add(reader.GetInt32(0));
            }

            var entitiesWithControlled = new HashSet<int>();
            if (TableExists(connection, "controlledblocks"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT ownerEntityId FROM controlledblocks;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) entitiesWithControlled.Add(reader.GetInt32(0));
            }

            var entitiesWithKeyMappings = new HashSet<int>();
            if (TableExists(connection, "keyColorMappings"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT ownerEntityId FROM keyColorMappings;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) entitiesWithKeyMappings.Add(reader.GetInt32(0));
            }

            var boardedEntities = new HashSet<int>();
            if (TableExists(connection, "players"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT boardedEntity FROM players WHERE boardedEntity IS NOT NULL AND boardedEntity >= 0;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) boardedEntities.Add(reader.GetInt32(0));
            }

            var entityRecords = new List<(int Id, bool Loaded, Vector3 Pos, Quaternion Rot)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, loaded, posx, posy, posz, orx, ory, orz, orw FROM entities ORDER BY id;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    bool loaded = reader.GetInt32(1) != 0;
                    long ex = reader.GetInt64(2);
                    long ey = reader.GetInt64(3);
                    long ez = reader.GetInt64(4);
                    float eox = reader.GetFloat(5);
                    float eoy = reader.GetFloat(6);
                    float eoz = reader.GetFloat(7);
                    float eow = reader.GetFloat(8);

                    Vector3 epos = DecodePosition(ex, ey, ez);
                    Quaternion erot = NormalizeOrDefault(new Quaternion(eox, eoy, eoz, eow));
                    entityRecords.Add((id, loaded, epos, erot));
                }
            }

            bool hasBlocksectors = TableExists(connection, "blocksectors");

            foreach (var (entityId, _, epos, erot) in entityRecords)
            {
                var rawVoxels = new List<(Int3 Pos, byte BlockId, byte Orient)>();

                if (hasBlocksectors)
                {
                    var sectorRows = new List<(int Sx, int Sy, int Sz, byte[] Formation, byte[] BlockIds, byte[] Orientations)>();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT posx, posy, posz, formation, blockIds, orientations FROM blocksectors WHERE ownerId = @ownerId ORDER BY posz, posy, posx;";
                        cmd.Parameters.AddWithValue("@ownerId", entityId);
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            sectorRows.Add((
                                reader.GetInt32(0),
                                reader.GetInt32(1),
                                reader.GetInt32(2),
                                (byte[])reader["formation"],
                                (byte[])reader["blockIds"],
                                (byte[])reader["orientations"]
                            ));
                        }
                    }

                    // In original Corneroids .wrl databases, chunk boundaries are centered on half-chunks:
                    // coordinates are stored in block units at 8 + 16*k (e.g. -40, -24, -8, 8, 24, 40, 56, 72, 88, 104, 120).
                    // If any sector has |pos| % 16 == 8, coordinates are in block units (scale = 1).
                    // Otherwise they are 0-based sector chunk indices (scale = 16).
                    bool isBlockCoordinates = sectorRows.Any(s =>
                        (Math.Abs(s.Sx) % 16 == 8) ||
                        (Math.Abs(s.Sy) % 16 == 8) ||
                        (Math.Abs(s.Sz) % 16 == 8));
                    int sectorScale = isBlockCoordinates ? 1 : 16;

                    foreach (var (sx, sy, sz, formationBlob, blockIdsBlob, orientationsBlob) in sectorRows)
                    {
                        byte[] formation = DecompressGzip(formationBlob, $"sector ({sx},{sy},{sz}) formation");
                        if (formation.Length != 512)
                            throw new WorldSaveException($"Corrupt formation blob at sector ({sx},{sy},{sz}): expected 512 bytes, got {formation.Length}");

                        int solidCount = 0;
                        for (int i = 0; i < 512; i++)
                            solidCount += BitOperations.PopCount(formation[i]);

                        if (solidCount == 0) continue;

                        byte[] blockIds = DecompressGzip(blockIdsBlob, $"sector ({sx},{sy},{sz}) blockIds");
                        if (blockIds.Length < solidCount)
                            throw new WorldSaveException($"Corrupt blockIds blob at sector ({sx},{sy},{sz}): expected {solidCount} bytes, got {blockIds.Length}");

                        byte[] orientations = DecompressGzip(orientationsBlob, $"sector ({sx},{sy},{sz}) orientations");
                        if (orientations.Length < solidCount)
                            throw new WorldSaveException($"Corrupt orientations blob at sector ({sx},{sy},{sz}): expected {solidCount} bytes, got {orientations.Length}");

                        int solidIdx = 0;
                        for (int bitPos = 0; bitPos < 4096; bitPos++)
                        {
                            int byteIdx = bitPos >> 3;
                            int bitInByte = bitPos & 7;
                            if ((formation[byteIdx] & (0x80 >> bitInByte)) != 0)
                            {
                                int lx = bitPos & 15;
                                int ly = (bitPos >> 4) & 15;
                                int lz = bitPos >> 8;
                                int gx = sx * sectorScale + lx;
                                int gy = sy * sectorScale + ly;
                                int gz = sz * sectorScale + lz;

                                rawVoxels.Add((new Int3(gx, gy, gz), blockIds[solidIdx], orientations[solidIdx]));
                                solidIdx++;
                            }
                        }
                    }
                }

                int minX = 0, minY = 0, minZ = 0;
                int maxX = 0, maxY = 0, maxZ = 0;
                if (rawVoxels.Count > 0)
                {
                    minX = int.MaxValue; minY = int.MaxValue; minZ = int.MaxValue;
                    maxX = int.MinValue; maxY = int.MinValue; maxZ = int.MinValue;
                    foreach (var (p, _, _) in rawVoxels)
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.Y > maxY) maxY = p.Y;
                        if (p.Z < minZ) minZ = p.Z;
                        if (p.Z > maxZ) maxZ = p.Z;
                    }
                }

                int dimX = rawVoxels.Count > 0 ? maxX - minX + 1 : 1;
                int dimY = rawVoxels.Count > 0 ? maxY - minY + 1 : 1;
                int dimZ = rawVoxels.Count > 0 ? maxZ - minZ + 1 : 1;
                int maxDim = Math.Max(dimX, Math.Max(dimY, dimZ));

                int size = 16;
                while (size < maxDim && size < 512)
                    size *= 2;

                var shift = new Int3(minX, minY, minZ);
                var adjustedPos = epos + Vector3.Transform(new Vector3(minX, minY, minZ), erot);

                var palette = new List<int> { WorldSnapshot.AirBlockId };
                var blockToPal = new Dictionary<int, ushort>();
                var cells = new ushort[size * size * size];
                var turned = new List<TurnedCell>();

                foreach (var (p, blockId, orientByte) in rawVoxels)
                {
                    var cell = p - shift;
                    if (!InGrid(cell, size)) continue;

                    if (!blockToPal.TryGetValue(blockId, out ushort palIdx))
                    {
                        palIdx = (ushort)palette.Count;
                        palette.Add(blockId);
                        blockToPal[blockId] = palIdx;
                    }
                    cells[WorldSnapshot.CellIndex(size, cell.X, cell.Y, cell.Z)] = palIdx;

                    int orientIdx = CorneroidsOrientation.FromByte(orientByte);
                    if (orientIdx != 0)
                    {
                        turned.Add(new TurnedCell(cell, orientIdx));
                    }
                }

                var consoles = ReadConsolesForEntity(connection, entityId, shift, size);
                bool isShip = IsShipEntity(entityId, entitiesWithControls, entitiesWithControlled, entitiesWithKeyMappings, boardedEntities, palette);

                ships.Add(new ShipSnapshot(adjustedPos, erot, Vector3.Zero, Vector3.Zero, size, palette, cells)
                {
                    Turned = turned,
                    Consoles = consoles,
                    IsAsteroid = !isShip,
                    Name = !isShip ? "Asteroid" : null,
                });
            }
        }

        return new WorldSnapshot(
            worldName,
            savedAt,
            itemsetBinding,
            seed,
            16,
            [WorldSnapshot.AirBlockId],
            new ushort[16 * 16 * 16],
            player,
            Array.Empty<ItemStack?>())
        {
            Ships = ships,
        };
    }

    private static IReadOnlyList<SavedConsole> ReadConsolesForEntity(SqliteConnection connection, int entityId, Int3 shift, int size)
    {
        if (!TableExists(connection, "controlblocks"))
            return [];

        var rawConsoles = new List<(int Id, int BindingId, Int3 Cell)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, bindingId, posx, posy, posz FROM controlblocks WHERE ownerEntityId = @ownerId ORDER BY id;";
            cmd.Parameters.AddWithValue("@ownerId", entityId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                int bindingId = reader.GetInt32(1);
                float cx = reader.GetFloat(2);
                float cy = reader.GetFloat(3);
                float cz = reader.GetFloat(4);

                var consoleCell = new Int3((int)MathF.Round(cx) - shift.X, (int)MathF.Round(cy) - shift.Y, (int)MathF.Round(cz) - shift.Z);
                if (InGrid(consoleCell, size))
                {
                    rawConsoles.Add((id, bindingId, consoleCell));
                }
            }
        }

        if (rawConsoles.Count == 0)
            return [];

        bool hasControlled = TableExists(connection, "controlledblocks");
        bool hasKeyMappings = TableExists(connection, "keyColorMappings");

        var result = new List<SavedConsole>();

        foreach (var (consoleId, bindingId, consoleCell) in rawConsoles)
        {
            var colorTargets = new List<Int3>[10];
            for (int i = 0; i < 10; i++)
                colorTargets[i] = [];

            if (hasControlled)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT bindingColor, posx, posy, posz FROM controlledblocks WHERE ownerEntityId = @ownerId AND boundTo = @consoleId;";
                cmd.Parameters.AddWithValue("@ownerId", entityId);
                cmd.Parameters.AddWithValue("@consoleId", consoleId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int color = reader.GetInt32(0);
                    if (color < 0 || color >= 10) continue;

                    float tx = reader.GetFloat(1);
                    float ty = reader.GetFloat(2);
                    float tz = reader.GetFloat(3);

                    var targetCell = new Int3((int)MathF.Round(tx) - shift.X, (int)MathF.Round(ty) - shift.Y, (int)MathF.Round(tz) - shift.Z);
                    if (InGrid(targetCell, size) && !colorTargets[color].Contains(targetCell))
                    {
                        colorTargets[color].Add(targetCell);
                    }
                }
            }

            var colorKeys = new string[10];
            Array.Fill(colorKeys, ConsoleBinding.DefaultKey);

            if (hasKeyMappings)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT color, key FROM keyColorMappings WHERE ownerEntityId = @ownerId AND blockId = @consoleId;";
                cmd.Parameters.AddWithValue("@ownerId", entityId);
                cmd.Parameters.AddWithValue("@consoleId", consoleId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int color = reader.GetInt32(0);
                    int keyCode = reader.GetInt32(1);
                    if (color >= 0 && color < 10)
                    {
                        colorKeys[color] = ResolveXnaKey(keyCode);
                    }
                }
            }

            Int3? cameraCell = null;
            if (bindingId != 0 && hasControlled)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT posx, posy, posz FROM controlledblocks WHERE ownerEntityId = @ownerId AND id = @bindingId LIMIT 1;";
                cmd.Parameters.AddWithValue("@ownerId", entityId);
                cmd.Parameters.AddWithValue("@bindingId", bindingId);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    float camX = reader.GetFloat(0);
                    float camY = reader.GetFloat(1);
                    float camZ = reader.GetFloat(2);
                    var camCell = new Int3((int)MathF.Round(camX) - shift.X, (int)MathF.Round(camY) - shift.Y, (int)MathF.Round(camZ) - shift.Z);
                    if (InGrid(camCell, size))
                    {
                        cameraCell = camCell;
                    }
                }
            }

            var groups = new List<SavedConsoleGroup>();
            for (int i = 0; i < 10; i++)
            {
                string key = colorKeys[i];
                var targets = colorTargets[i];
                if (!string.Equals(key, ConsoleBinding.DefaultKey, StringComparison.OrdinalIgnoreCase) || targets.Count > 0)
                {
                    groups.Add(new SavedConsoleGroup(i, key, [.. targets]));
                }
            }

            result.Add(new SavedConsole(consoleCell, groups, cameraCell));
        }

        return result;
    }

    public static string ResolveXnaKey(int keyCode)
    {
        if (keyCode == 0) return ConsoleBinding.DefaultKey;
        if (keyCode >= 65 && keyCode <= 90) return ((char)keyCode).ToString(); // "A".."Z"
        if (keyCode >= 48 && keyCode <= 57) return ((char)keyCode).ToString(); // "0".."9"

        return keyCode switch
        {
            8 => "Back",
            9 => "Tab",
            13 => "Enter",
            19 => "Pause",
            20 => "CapsLock",
            27 => "Escape",
            32 => "Space",
            33 => "PageUp",
            34 => "PageDown",
            35 => "End",
            36 => "Home",
            37 => "Left",
            38 => "Up",
            39 => "Right",
            40 => "Down",
            45 => "Insert",
            46 => "Delete",
            96 => "NumPad0", 97 => "NumPad1", 98 => "NumPad2", 99 => "NumPad3", 100 => "NumPad4",
            101 => "NumPad5", 102 => "NumPad6", 103 => "NumPad7", 104 => "NumPad8", 105 => "NumPad9",
            106 => "Multiply", 107 => "Add", 109 => "Subtract", 110 => "Decimal", 111 => "Divide",
            112 => "F1", 113 => "F2", 114 => "F3", 115 => "F4", 116 => "F5", 117 => "F6",
            118 => "F7", 119 => "F8", 120 => "F9", 121 => "F10", 122 => "F11", 123 => "F12",
            160 => "LeftShift",
            161 => "RightShift",
            162 => "LeftControl",
            163 => "RightControl",
            164 => "LeftAlt",
            165 => "RightAlt",
            186 => "OemSemicolon",
            187 => "OemPlus",
            188 => "OemComma",
            189 => "OemMinus",
            190 => "OemPeriod",
            191 => "OemQuestion",
            192 => "OemTilde",
            219 => "OemOpenBrackets",
            220 => "OemPipe",
            221 => "OemCloseBrackets",
            222 => "OemQuotes",
            _ => ConsoleBinding.DefaultKey,
        };
    }

    public static (float Pitch, float Yaw, float Roll) QuaternionToEulerYxz(Quaternion q)
    {
        float x = q.X, y = q.Y, z = q.Z, w = q.W;
        float sinPitch = Math.Clamp(2f * (w * x - y * z), -1f, 1f);
        float pitch = MathF.Asin(sinPitch);

        float yaw, roll;
        if (MathF.Abs(sinPitch) < 0.999999f)
        {
            yaw = MathF.Atan2(2f * (w * y + x * z), 1f - 2f * (x * x + y * y));
            roll = MathF.Atan2(2f * (w * z + x * y), 1f - 2f * (x * x + z * z));
        }
        else
        {
            yaw = MathF.Atan2(-2f * (y * z - w * x), 1f - 2f * (y * y + z * z));
            roll = 0f;
        }
        return (pitch, yaw, roll);
    }

    private static Quaternion NormalizeOrDefault(Quaternion q)
    {
        float lenSq = q.LengthSquared();
        if (lenSq < 1e-6f || !float.IsFinite(lenSq))
            return Quaternion.Identity;
        return Quaternion.Normalize(q);
    }

    private static readonly HashSet<int> NaturalAsteroidBlockIds =
    [
        0, 1, 2,       // Vanilla rocks (light, medium/granite, heavy/core stone)
        3, 4, 5,       // Vanilla minerals (coal, iron ore, gold ore)
        16, 17, 18,    // Vanilla minerals (platinum ore, silver ore, uranium ore)
        32, 33,        // Vanilla minerals (titanium ore, cobalt ore)
        48, 49,        // Vanilla minerals (ice / icy rock, cobaltite rock)
        65, 131, 154, 186 // Modded minerals in PasterLak (Zilium ore, Nitridium ore, etc.)
    ];

    private static bool IsShipEntity(
        int entityId,
        HashSet<int> withControls,
        HashSet<int> withControlled,
        HashSet<int> withKeys,
        HashSet<int> boarded,
        IReadOnlyList<int> palette)
    {
        if (withControls.Contains(entityId) ||
            withControlled.Contains(entityId) ||
            withKeys.Contains(entityId) ||
            boarded.Contains(entityId))
        {
            return true;
        }

        // Palette index 0 is air sentinel (-1). If any actual block is not natural celestial rock or ore, it's a ship.
        for (int i = 1; i < palette.Count; i++)
        {
            int blockId = palette[i];
            if (!NaturalAsteroidBlockIds.Contains(blockId))
                return true;
        }

        return false;
    }

    private static bool InGrid(Int3 p, int size) =>
        p.X >= 0 && p.Y >= 0 && p.Z >= 0 && p.X < size && p.Y < size && p.Z < size;

    private static bool TableExists(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name = @name LIMIT 1;";
        cmd.Parameters.AddWithValue("@name", tableName);
        return cmd.ExecuteScalar() != null;
    }

    private static byte[] DecompressGzip(byte[] data, string context)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            throw new WorldSaveException($"Failed to decompress GZIP stream for {context}: {ex.Message}", ex);
        }
    }
}
