using System.Numerics;
using System.Text.Json.Nodes;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>The optional turned grid blocks (<see cref="WorldSnapshot.Turned"/>, <see cref="ShipSnapshot.Turned"/>).</summary>
public class TurnedSaveFormatTests
{
    static readonly TurnedCell[] Turns = [new(new Int3(0, 1, 2), 5), new(new Int3(3, 3, 3), 23)];

    [Fact]
    public void Round_trip_keeps_turned_cells_of_the_world_and_of_ships()
    {
        var original = SmallWorld() with
        {
            Turned = Turns,
            Ships = [Ship() with { Turned = [new TurnedCell(new Int3(1, 0, 0), 7)] }],
        };

        var read = FromBytes(ToBytes(original));

        Assert.Equal(Turns, read.Turned);
        Assert.Equal([new TurnedCell(new Int3(1, 0, 0), 7)], read.Ships[0].Turned);
    }

    [Fact]
    public void Turned_cells_make_version_5_and_none_keep_the_old_one()
    {
        Assert.True(WorldSaveFormat.TurnedVersion < WorldSaveFormat.CurrentVersion);
        Assert.Equal(WorldSaveFormat.TurnedVersion, Split(ToBytes(SmallWorld() with { Turned = Turns })).Version);
        Assert.Equal(WorldSaveFormat.TurnedVersion,
            Split(ToBytes(SmallWorld() with { Ships = [Ship() with { Turned = [new TurnedCell(Int3.Zero, 1)] }] })).Version);
        Assert.Equal(WorldSaveFormat.ObjectsVersion, Split(ToBytes(SmallWorld())).Version);
        Assert.False(JsonNode.Parse(Split(ToBytes(SmallWorld())).Json)!.AsObject().ContainsKey("turned"));
        Assert.Empty(FromBytes(ToBytes(SmallWorld())).Turned);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]      // the default orientation is never stored
    [InlineData(0, 0, 0, 24)]     // there are only 24
    [InlineData(-1, 0, 0, 3)]     // outside the grid
    [InlineData(0, 0, 99, 3)]
    public void Bad_turned_cells_are_refused_when_writing(int x, int y, int z, int orientation)
    {
        var world = SmallWorld() with { Turned = [new TurnedCell(new Int3(x, y, z), orientation)] };
        Assert.Throws<ArgumentException>(() => ToBytes(world));
    }

    [Fact]
    public void A_missing_ship_list_is_refused_rather_than_crashing() =>
        Assert.Throws<ArgumentException>(() => ToBytes(SmallWorld() with { Turned = Turns, Ships = null! }));
}
