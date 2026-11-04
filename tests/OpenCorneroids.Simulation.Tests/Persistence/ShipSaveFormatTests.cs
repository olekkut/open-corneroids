using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>The optional <see cref="WorldSnapshot.Ships"/> list of the save format.</summary>
public class ShipSaveFormatTests
{
    static WorldSnapshot WithShips() => SmallWorld() with { Ships = [Ship(), Ship(size: 7, seed: 5) with { Objects = [] }] };

    /// <summary>A valid save with one 2³ ship, its JSON edited.</summary>
    static byte[] EditShip(Action<JsonObject> edit)
    {
        var world = World(2, [WorldSnapshot.AirBlockId, 0, 5], [0, 1, 1, 2, 0, 0, 0, 2]) with
        {
            Ships = [new ShipSnapshot(Vector3.One, Quaternion.Identity, Vector3.Zero, Vector3.Zero, 2, [WorldSnapshot.AirBlockId, 5], [0, 1, 0, 0, 1, 1, 0, 0])],
        };
        var (_, json, cells) = Split(ToBytes(world));
        var root = JsonNode.Parse(json)!.AsObject();
        edit(root["ships"]![0]!.AsObject());
        return Container(Encoding.UTF8.GetBytes(root.ToJsonString()), cells);
    }

    /// <summary>The base64 cells for a 2³ ship from raw runs.</summary>
    static string ShipCells(params (int Value, uint Run)[] runs) => Convert.ToBase64String(Zlib(Runs(runs)));

    [Fact]
    public void Round_trip_preserves_every_ship_field()
    {
        var original = WithShips();
        var bytes = ToBytes(original);
        AssertSameWorld(original, FromBytes(bytes));
        WorldSaveFormat.Verify(new MemoryStream(bytes));
    }

    [Fact]
    public void Reads_ships_from_a_forward_only_stream()
    {
        var original = WithShips();
        AssertSameWorld(original, WorldSaveFormat.Read(new ForwardOnlyStream(ToBytes(original))));
    }

    [Fact]
    public void Saves_with_ships_are_a_newer_version_and_saves_without_keep_the_old_one()
    {
        Assert.Equal(WorldSaveFormat.ShipsVersion, Split(ToBytes(WithShips())).Version);
        Assert.Equal(WorldSaveFormat.ShipsVersion, WorldSaveFormat.CurrentVersion);
        Assert.Equal(WorldSaveFormat.ShipsVersion, Split(ToBytes(AllAir(2) with { Ships = [Ship()] })).Version);
        // No ships: the file is exactly what the previous version wrote, without even an empty "ships" member.
        Assert.Equal(WorldSaveFormat.ObjectsVersion, Split(ToBytes(SmallWorld())).Version);
        Assert.False(JsonNode.Parse(Split(ToBytes(SmallWorld())).Json)!.AsObject().ContainsKey("ships"));
    }

    [Fact]
    public void Player_drift_is_optional_and_left_out_when_zero()
    {
        var still = World(2, [WorldSnapshot.AirBlockId], new ushort[8]);
        var stillJson = JsonNode.Parse(Split(ToBytes(still)).Json)!.AsObject();
        Assert.False(stillJson["player"]!.AsObject().ContainsKey("drift"));
        Assert.Equal(Vector3.Zero, FromBytes(ToBytes(still)).Player.Drift);

        var drifting = still with { Player = still.Player with { Velocity = new Vector3(3, 0, -4), Drift = new Vector3(3, 0, -4) } };
        var json = JsonNode.Parse(Split(ToBytes(drifting)).Json)!.AsObject();
        Assert.Equal([3f, 0f, -4f], json["player"]!["drift"]!.AsArray().Select(n => (float)n!));
        Assert.Equal(drifting.Player, FromBytes(ToBytes(drifting)).Player);
    }

    [Fact]
    public void Older_saves_read_with_no_ships()
    {
        var old = SmallWorld();
        var (_, json, cells) = Split(ToBytes(old));
        var copy = FromBytes(Container(json, cells, version: WorldSaveFormat.ObjectsVersion));
        Assert.Empty(copy.Ships);
        AssertSameWorld(old, copy);
        Assert.Empty(FromBytes(Container(json, cells, version: WorldSaveFormat.BaseVersion)).Ships);
    }

    [Fact]
    public void Ships_are_written_as_an_optional_json_list_with_encoded_cells()
    {
        var ship = Ship();
        var json = JsonNode.Parse(Split(ToBytes(AllAir(2) with { Ships = [ship] })).Json)!.AsObject();
        var saved = json["ships"]!.AsArray()[0]!.AsObject();
        Assert.Equal([ship.Position.X, ship.Position.Y, ship.Position.Z], saved["position"]!.AsArray().Select(n => (float)n!));
        Assert.Equal([ship.Rotation.X, ship.Rotation.Y, ship.Rotation.Z, ship.Rotation.W], saved["rotation"]!.AsArray().Select(n => (float)n!));
        Assert.Equal(ship.Size, (int?)saved["size"]);
        Assert.Equal(ship.Palette, saved["palette"]!.AsArray().Select(n => (int)n!));
        Assert.Equal(22, (int?)saved["objects"]![0]!["blockId"]);
        // The same zlib-over-runs encoding as the world's cell block, as base64.
        var block = Convert.FromBase64String((string)saved["cells"]!);
        Assert.Equal(0x78, block[0]);
    }

    [Fact]
    public void Velocities_may_be_left_out()
    {
        var bytes = EditShip(s =>
        {
            s.Remove("linearVelocity");
            s.Remove("angularVelocity");
        });
        var ship = Assert.Single(FromBytes(bytes).Ships);
        Assert.Equal(Vector3.Zero, ship.LinearVelocity);
        Assert.Equal(Vector3.Zero, ship.AngularVelocity);
        Assert.Equal(5, ship.Palette[1]);
        Assert.Equal(3, ship.Cells.Count(c => c == 1));
    }

    [Fact]
    public void Summary_does_not_decode_ship_cells_but_read_and_verify_do()
    {
        var bytes = EditShip(s => s["cells"] = ShipCells((1, 3)));
        Assert.Equal("Test world", WorldSaveFormat.ReadSummary(new MemoryStream(bytes)).Name);
        Assert.Contains("ship 0", Assert.Throws<WorldSaveException>(() => FromBytes(bytes)).Message);
        Assert.Contains("ship 0", Assert.Throws<WorldSaveException>(() => WorldSaveFormat.Verify(new MemoryStream(bytes))).Message);
    }

    public static TheoryData<string, Action<JsonObject>, string> BadShips => new()
    {
        { "missing position", s => s.Remove("position"), "ships[0].position" },
        { "short rotation", s => s["rotation"] = new JsonArray(0, 0, 1), "ships[0].rotation" },
        { "zero rotation", s => s["rotation"] = new JsonArray(0, 0, 0, 0), "ship 0 has a rotation that is not a unit quaternion" },
        { "long rotation", s => s["rotation"] = new JsonArray(0, 0, 0, 1.1), "not a unit quaternion" },
        { "missing size", s => s.Remove("size"), "ships[0].size" },
        { "size zero", s => s["size"] = 0, "ship 0 has size 0" },
        { "size over cap", s => s["size"] = WorldSaveFormat.MaxShipSize + 1, $"outside 1..{WorldSaveFormat.MaxShipSize}" },
        { "missing palette", s => s.Remove("palette"), "ships[0].palette" },
        { "empty palette", s => s["palette"] = new JsonArray(), "ship 0's palette is empty" },
        { "palette without air", s => s["palette"] = new JsonArray(5, 6), "ship 0 palette entry 0 is 5" },
        { "negative palette entry", s => s["palette"] = new JsonArray(-1, -4), "ship 0 palette entry 1 is the negative blockId -4" },
        { "missing cells", s => s.Remove("cells"), "ships[0].cells" },
        { "cells not base64", s => s["cells"] = "not base64!", "cells of ship 0 are not valid base64" },
        { "cells not zlib", s => s["cells"] = Convert.ToBase64String([1, 2, 3, 4, 5, 6]), "ship 0: the cell data is not a zlib stream" },
        { "cells too few", s => s["cells"] = ShipCells((1, 7)), "ship 0: the cell data ends after 7 of 8 cells" },
        { "cells too many", s => s["cells"] = ShipCells((1, 9)), "ship 0: cell runs cover more than the 8 cells" },
        { "cell outside palette", s => s["cells"] = ShipCells((0, 4), (2, 4)), "ship 0: cell 4 uses palette index 2" },
        { "object outside", s => s["objects"] = new JsonArray(new JsonObject { ["blockId"] = 5, ["origin"] = new JsonArray(2, 0, 0), ["orientation"] = 0 }),
            "ship 0 object 0 has origin (2, 0, 0), outside the 2-cell ship grid" },
        { "object bad orientation", s => s["objects"] = new JsonArray(new JsonObject { ["blockId"] = 5, ["origin"] = new JsonArray(0, 0, 0), ["orientation"] = 30 }),
            "ship 0 object 0 has orientation 30" },
        { "object missing block", s => s["objects"] = new JsonArray(new JsonObject { ["origin"] = new JsonArray(0, 0, 0), ["orientation"] = 0 }),
            "ships[0].objects[0].blockId" },
    };

    [Theory]
    [MemberData(nameof(BadShips))]
    public void Bad_ships_are_rejected_by_name(string name, Action<JsonObject> edit, string expected)
    {
        var bytes = EditShip(edit);
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(bytes));
        Assert.True(error.Message.Contains(expected), $"{name}: '{error.Message}' does not mention '{expected}'");
    }

    [Fact]
    public void A_null_ship_is_rejected()
    {
        var (_, json, cells) = Split(ToBytes(AllAir(2) with { Ships = [Ship()] }));
        var root = JsonNode.Parse(json)!.AsObject();
        root["ships"]!.AsArray()[0] = null;
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(Container(Encoding.UTF8.GetBytes(root.ToJsonString()), cells)));
        Assert.Contains("ships[0]", error.Message);
    }

    [Fact]
    public void Ship_count_and_total_cells_are_capped_before_any_grid_is_allocated()
    {
        var (_, json, cells) = Split(ToBytes(AllAir(2) with { Ships = [Ship(size: 2) with { Objects = [] }] }));
        var root = JsonNode.Parse(json)!.AsObject();
        var one = root["ships"]![0]!.ToJsonString();

        var many = new JsonArray();
        for (int i = 0; i <= WorldSaveFormat.MaxShips; i++) many.Add(JsonNode.Parse(one));
        root["ships"] = many;
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(Container(Encoding.UTF8.GetBytes(root.ToJsonString()), cells)));
        Assert.Contains($"over the {WorldSaveFormat.MaxShips} limit", error.Message);

        // Nine maximum-size ships declared in a few hundred bytes: refused by the total, not by running out of memory.
        var big = new JsonArray();
        for (int i = 0; i < 9; i++)
        {
            var ship = JsonNode.Parse(one)!.AsObject();
            ship["size"] = WorldSaveFormat.MaxShipSize;
            big.Add(ship);
        }
        root["ships"] = big;
        error = Assert.Throws<WorldSaveException>(() => FromBytes(Container(Encoding.UTF8.GetBytes(root.ToJsonString()), cells)));
        Assert.Contains("cells together", error.Message);
    }

    [Fact]
    public void Write_rejects_inconsistent_ships()
    {
        var ship = Ship();
        Assert.Throws<ArgumentException>(() => ToBytes(AllAir(2) with { Ships = [ship with { Cells = new ushort[5] }] }));
        Assert.Throws<ArgumentException>(() => ToBytes(AllAir(2) with { Ships = [ship with { Cells = [.. ship.Cells.Select(_ => (ushort)9)] }] }));
        Assert.Throws<ArgumentException>(() => ToBytes(AllAir(2) with { Ships = [ship with { Rotation = new Quaternion(0, 0, 0, 2) }] }));
        Assert.Throws<ArgumentException>(() => ToBytes(AllAir(2) with { Ships = [ship with { Position = new Vector3(float.NaN, 0, 0) }] }));
        Assert.Throws<ArgumentException>(() => ToBytes(AllAir(2) with { Ships = [ship with { Objects = [new PlacedObject(1, new Int3(4, 0, 0), 0, 0)] }] }));
        Assert.Throws<ArgumentException>(() => ToBytes(AllAir(2) with { Ships = [null!] }));
    }

    [Fact]
    public void Truncating_a_save_with_ships_always_throws_WorldSaveException()
    {
        var bytes = ToBytes(WithShips());
        for (int length = 0; length < bytes.Length; length += 7)
            Assert.Throws<WorldSaveException>(() => FromBytes(bytes[..length]));
    }

    [Fact]
    public void Ship_blocks_count_for_the_itemset_binding()
    {
        var world = AllAir(2) with { Ships = [Ship()] };
        // Ship(): palette 0, 23, 9 (whichever cells use them) and objects 22 and 30.
        var used = SaveBinding.UsedBlockIds(world);
        Assert.Contains(22, used);
        Assert.Contains(30, used);
        var result = SaveBinding.Check(world, Itemset(Binding.Key, "other", 0, 9, 23, 22), "pack");
        Assert.Equal(BindingStatus.Incompatible, result.Status);
        Assert.Equal([30], result.MissingBlockIds);
    }
}
