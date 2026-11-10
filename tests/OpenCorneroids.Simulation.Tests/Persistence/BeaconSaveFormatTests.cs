using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Navigation;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>The optional beacons (<see cref="WorldSnapshot.Beacons"/>) in our saves and in imported .wrl worlds.</summary>
public class BeaconSaveFormatTests
{
    static readonly Beacon[] Marks =
    [
        new(new Vector3(12.5f, -3.25f, 1e5f), "Base"),
        new(new Vector3(0, 0, 0), "Kosmos Ω \"quoted\""),
    ];

    [Fact]
    public void Round_trip_keeps_every_beacon_in_order()
    {
        var read = FromBytes(ToBytes(SmallWorld() with { Beacons = Marks }));
        Assert.Equal(Marks, read.Beacons);
    }

    [Fact]
    public void Round_trip_through_a_world_file_keeps_them_too()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oc-beacons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "w.ocworld");
            WorldStore.SaveFile(path, SmallWorld() with { Beacons = Marks });
            Assert.Equal(Marks, WorldStore.Load(path).Beacons);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Beacons_make_version_7_and_a_world_without_them_keeps_its_older_version()
    {
        Assert.Equal(7, WorldSaveFormat.BeaconsVersion);
        Assert.Equal(WorldSaveFormat.BeaconsVersion, WorldSaveFormat.CurrentVersion);
        Assert.Equal(WorldSaveFormat.BeaconsVersion, Split(ToBytes(SmallWorld() with { Beacons = Marks })).Version);
        Assert.Equal(WorldSaveFormat.BeaconsVersion, Split(ToBytes(AllAir(4) with { Beacons = Marks })).Version);

        var turned = SmallWorld() with { Turned = [new TurnedCell(Int3.Zero, 3)] };
        Assert.Equal(WorldSaveFormat.TurnedVersion, Split(ToBytes(turned)).Version);
        Assert.Equal(WorldSaveFormat.BaseVersion, Split(ToBytes(AllAir(4))).Version);
        var json = JsonNode.Parse(Split(ToBytes(turned)).Json)!.AsObject();
        Assert.False(json.ContainsKey("beacons"));
        Assert.Empty(FromBytes(ToBytes(turned)).Beacons);
    }

    [Fact]
    public void A_version_6_file_loads_with_no_beacons()
    {
        var (_, json, cells) = Split(ToBytes(SmallWorld()));
        var read = FromBytes(Container(json, cells, version: WorldSaveFormat.ContainersVersion));
        Assert.Empty(read.Beacons);
    }

    public static TheoryData<string> BadBeacons => new() { "nan position", "infinite position", "null name", "huge name", "null beacon", "null list" };

    static WorldSnapshot Bad(string which) => SmallWorld() with
    {
        Beacons = which switch
        {
            "nan position" => [new Beacon(new Vector3(float.NaN, 0, 0), "a")],
            "infinite position" => [new Beacon(new Vector3(0, float.PositiveInfinity, 0), "a")],
            "null name" => [new Beacon(Vector3.Zero, null!)],
            "huge name" => [new Beacon(Vector3.Zero, new string('x', WorldSaveFormat.MaxBeaconMessage + 1))],
            "null beacon" => [null!],
            "null list" => null!,
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        },
    };

    [Theory]
    [MemberData(nameof(BadBeacons))]
    public void Bad_beacons_are_refused_when_writing(string which) =>
        Assert.Throws<ArgumentException>(() => ToBytes(Bad(which)));

    public static TheoryData<string> DamagedBeacons => new()
    {
        "null beacon", "missing message", "missing position", "position not three numbers", "huge name", "too many",
    };

    static byte[] Damaged(string which)
    {
        var (_, json, cells) = Split(ToBytes(SmallWorld() with { Beacons = Marks }));
        var root = JsonNode.Parse(json)!.AsObject();
        var beacons = root["beacons"]!.AsArray();
        var first = beacons[0]!.AsObject();
        switch (which)
        {
            case "null beacon": beacons[0] = null; break;
            case "missing message": first.Remove("message"); break;
            case "missing position": first.Remove("position"); break;
            case "position not three numbers": first["position"] = new JsonArray(1, 2); break;
            case "huge name": first["message"] = new string('x', WorldSaveFormat.MaxBeaconMessage + 1); break;
            case "too many":
                var many = new JsonArray();
                for (int i = 0; i <= WorldSaveFormat.MaxBeacons; i++) many.Add(new JsonObject { ["position"] = new JsonArray(0, 0, 0), ["message"] = "a" });
                root["beacons"] = many;
                break;
        }
        return Container(Encoding.UTF8.GetBytes(root.ToJsonString()), cells);
    }

    [Theory]
    [MemberData(nameof(DamagedBeacons))]
    public void Damaged_beacons_in_a_file_are_refused_with_a_message(string which)
    {
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(Damaged(which)));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    // ---- .wrl import ----

    static SqliteConnection WrlWorld(bool withBeaconTable, params (long X, long Y, long Z, string? Message)[] rows)
    {
        var builder = new CorneroidsWrlFixtureBuilder().WithWorldInfo();
        var connection = builder.Connection;
        if (!withBeaconTable) return connection;
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE beacons (posx LONG, posy LONG, posz LONG, message VARCHAR(20), PRIMARY KEY (posx, posy, posz));";
            create.ExecuteNonQuery();
        }
        foreach (var (x, y, z, message) in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO beacons VALUES (@x, @y, @z, @m);";
            insert.Parameters.AddWithValue("@x", x);
            insert.Parameters.AddWithValue("@y", y);
            insert.Parameters.AddWithValue("@z", z);
            insert.Parameters.AddWithValue("@m", (object?)message ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }
        return connection;
    }

    const long Unit = 1L << 23;

    [Fact]
    public void An_original_world_brings_its_beacons_at_their_positions_with_their_names()
    {
        using var connection = WrlWorld(true,
            (10 * Unit, -3 * Unit + Unit / 2, 0, "Home"),
            (-Unit / 4, 100 * Unit, 7 * Unit, "Mine"));

        var snapshot = CorneroidsWrlReader.ReadFromConnection(connection);

        Assert.Equal(2, snapshot.Beacons.Count);
        Assert.Contains(new Beacon(new Vector3(10, -2.5f, 0), "Home"), snapshot.Beacons);
        Assert.Contains(new Beacon(new Vector3(-0.25f, 100, 7), "Mine"), snapshot.Beacons);
        // An imported world with beacons is saved in the version that keeps them.
        Assert.Equal(WorldSaveFormat.BeaconsVersion, WorldSaveFormat.VersionFor(snapshot));
    }

    [Fact]
    public void An_original_world_without_the_beacon_table_or_rows_has_none()
    {
        using (var noTable = WrlWorld(false))
            Assert.Empty(CorneroidsWrlReader.ReadFromConnection(noTable).Beacons);
        using (var emptyTable = WrlWorld(true))
            Assert.Empty(CorneroidsWrlReader.ReadFromConnection(emptyTable).Beacons);
    }

    [Fact]
    public void Nameless_rows_are_skipped_and_long_names_cut_to_twenty_characters()
    {
        using var connection = WrlWorld(true,
            (0, 0, 0, null),
            (Unit, 0, 0, "   "),
            (2 * Unit, 0, 0, "A name that is far too long"));

        var beacon = Assert.Single(CorneroidsWrlReader.ReadFromConnection(connection).Beacons);
        Assert.Equal(new Beacon(new Vector3(2, 0, 0), "A name that is far t"), beacon);
    }
}
