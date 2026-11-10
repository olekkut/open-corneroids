using System.Text;
using System.Text.Json.Nodes;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>The optional chest contents (<see cref="WorldSnapshot.Containers"/>, <see cref="ShipSnapshot.Containers"/>).</summary>
public class ContainerSaveFormatTests
{
    static readonly SavedContainer[] Chests =
    [
        new(new Int3(0, 1, 2), [new ItemStack(7, 3), null, null, new ItemStack(112, 1)]),
        new(new Int3(4, 4, 4), [null, new ItemStack(9999, 64)]),   // an id the itemset may not define is kept
    ];

    static WorldSnapshot WithChests() => SmallWorld() with
    {
        Containers = Chests,
        Ships = [Ship() with { Containers = [new SavedContainer(new Int3(1, 0, 3), [new ItemStack(5, 2)])] }],
    };

    [Fact]
    public void Round_trip_keeps_every_chest_of_the_world_and_of_ships_slot_for_slot()
    {
        var read = FromBytes(ToBytes(WithChests()));

        Assert.Equal(Chests.Length, read.Containers.Count);
        for (int i = 0; i < Chests.Length; i++)
        {
            Assert.Equal(Chests[i].Cell, read.Containers[i].Cell);
            Assert.Equal(Chests[i].Slots, read.Containers[i].Slots);
        }
        var shipChest = Assert.Single(read.Ships[0].Containers);
        Assert.Equal(new Int3(1, 0, 3), shipChest.Cell);
        Assert.Equal([new ItemStack(5, 2)], shipChest.Slots);
    }

    [Fact]
    public void Chests_make_version_6_and_a_world_without_them_keeps_version_5_or_older()
    {
        Assert.Equal(6, WorldSaveFormat.ContainersVersion);
        Assert.True(WorldSaveFormat.ContainersVersion < WorldSaveFormat.CurrentVersion);
        Assert.Equal(WorldSaveFormat.ContainersVersion, Split(ToBytes(SmallWorld() with { Containers = Chests })).Version);
        Assert.Equal(WorldSaveFormat.ContainersVersion, Split(ToBytes(WithChests() with { Containers = [] })).Version);

        var turned = SmallWorld() with { Turned = [new TurnedCell(Int3.Zero, 3)] };
        Assert.Equal(WorldSaveFormat.TurnedVersion, Split(ToBytes(turned)).Version);
        var json = JsonNode.Parse(Split(ToBytes(turned)).Json)!.AsObject();
        Assert.False(json.ContainsKey("containers"));
        Assert.Empty(FromBytes(ToBytes(turned)).Containers);
    }

    [Fact]
    public void A_version_5_file_loads_with_no_chests()
    {
        var turned = SmallWorld() with { Turned = [new TurnedCell(Int3.Zero, 3)] };
        var (_, json, cells) = Split(ToBytes(turned));

        var read = FromBytes(Container(json, cells, version: WorldSaveFormat.TurnedVersion));

        Assert.Empty(read.Containers);
        Assert.Equal(turned.Turned, read.Turned);
    }

    public static TheoryData<string> BadChests => new()
    {
        "outside the grid", "below the grid", "duplicate cell", "too many slots", "zero quantity", "negative quantity",
        "null chest", "null slots", "null list",
    };

    static WorldSnapshot Bad(string which) => which switch
    {
        "outside the grid" => SmallWorld() with { Containers = [new SavedContainer(new Int3(0, 0, 5), [new ItemStack(1, 1)])] },
        "below the grid" => SmallWorld() with { Containers = [new SavedContainer(new Int3(-1, 0, 0), [new ItemStack(1, 1)])] },
        "duplicate cell" => SmallWorld() with { Containers = [Chests[0], Chests[0]] },
        "too many slots" => SmallWorld() with
        {
            Containers = [new SavedContainer(Int3.Zero, [new ItemStack(1, 1), .. new ItemStack?[WorldSaveFormat.MaxContainerSlots]])],
        },
        "zero quantity" => SmallWorld() with { Containers = [new SavedContainer(Int3.Zero, [new ItemStack(1, 0)])] },
        "negative quantity" => SmallWorld() with { Containers = [new SavedContainer(Int3.Zero, [new ItemStack(1, -4)])] },
        "null chest" => SmallWorld() with { Containers = [null!] },
        "null slots" => SmallWorld() with { Containers = [new SavedContainer(Int3.Zero, null!)] },
        "null list" => SmallWorld() with { Containers = null! },
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    [Theory]
    [MemberData(nameof(BadChests))]
    public void Bad_chests_are_refused_when_writing(string which) =>
        Assert.Throws<ArgumentException>(() => ToBytes(Bad(which)));

    [Fact]
    public void A_bad_chest_on_a_ship_is_refused_too()
    {
        var world = SmallWorld() with { Ships = [Ship() with { Containers = [new SavedContainer(new Int3(0, 0, 4), [new ItemStack(1, 1)])] }] };
        var message = Assert.Throws<ArgumentException>(() => ToBytes(world)).Message;
        Assert.Contains("ship 0", message);
    }

    static byte[] EditChests(Action<JsonArray> edit)
    {
        var (_, json, cells) = Split(ToBytes(SmallWorld() with { Containers = Chests }));
        var root = JsonNode.Parse(json)!.AsObject();
        edit(root["containers"]!.AsArray());
        return Container(Encoding.UTF8.GetBytes(root.ToJsonString()), cells);
    }

    public static TheoryData<string> DamagedChests => new()
    {
        "hostile slot list", "duplicate cell", "cell outside", "cell not three numbers", "missing slots", "missing quantity",
        "zero quantity", "null chest",
    };

    static byte[] Damaged(string which) => EditChests(chests =>
    {
        var first = chests[0]!.AsObject();
        switch (which)
        {
            case "hostile slot list":
                var huge = new JsonArray();
                for (int i = 0; i <= WorldSaveFormat.MaxContainerSlots; i++) huge.Add((JsonNode?)null);
                first["slots"] = huge;
                break;
            case "duplicate cell": chests[1]!["cell"] = new JsonArray(0, 1, 2); break;
            case "cell outside": first["cell"] = new JsonArray(0, 99, 0); break;
            case "cell not three numbers": first["cell"] = new JsonArray(0, 1); break;
            case "missing slots": first.Remove("slots"); break;
            case "missing quantity": first["slots"]![0]!.AsObject().Remove("quantity"); break;
            case "zero quantity": first["slots"]![0]!["quantity"] = 0; break;
            case "null chest": chests[0] = null; break;
        }
    });

    [Theory]
    [MemberData(nameof(DamagedChests))]
    public void Damaged_chests_in_a_file_are_refused_with_a_message(string which)
    {
        var error = Assert.Throws<WorldSaveException>(() => FromBytes(Damaged(which)));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }
}
