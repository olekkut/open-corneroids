using System.Text.Json.Nodes;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Persistence;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>The optional <see cref="WorldSnapshot.Suit"/> of the save format.</summary>
public class SuitSaveFormatTests
{
    static readonly SuitState Suit = new(73.25, 12.5,
        [new ItemStack(30, 1), null, null, null, null, null, new ItemStack(31, 1)],
        [new SuitBuff(41, 12.75, 0, 10)]);

    [Fact]
    public void Round_trip_preserves_the_suit()
    {
        var original = SmallWorld() with { Suit = Suit };
        var bytes = ToBytes(original);
        AssertSameWorld(original, FromBytes(bytes));
        WorldSaveFormat.Verify(new MemoryStream(bytes));
    }

    [Fact]
    public void A_suit_makes_the_newest_version_and_no_suit_keeps_the_old_one()
    {
        Assert.Equal(WorldSaveFormat.SuitVersion, Split(ToBytes(SmallWorld() with { Suit = Suit })).Version);
        Assert.Equal(WorldSaveFormat.ObjectsVersion, Split(ToBytes(SmallWorld())).Version);
        Assert.False(JsonNode.Parse(Split(ToBytes(SmallWorld())).Json)!.AsObject().ContainsKey("suit"));
        Assert.Null(FromBytes(ToBytes(SmallWorld())).Suit);
    }

    [Fact]
    public void A_non_finite_suit_is_refused_when_writing()
    {
        Assert.Throws<ArgumentException>(() => ToBytes(SmallWorld() with { Suit = Suit with { Power = double.NaN } }));
    }
}
