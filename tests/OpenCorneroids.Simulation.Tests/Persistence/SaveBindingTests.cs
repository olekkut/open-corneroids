using OpenCorneroids.Simulation.Persistence;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

public class SaveBindingTests
{
    /// <summary>Palette [-1, 0, 7, 99]; cells use blockIds 0 and 7. 99 is a leftover entry no cell references.</summary>
    static WorldSnapshot Saved() =>
        World(2, [WorldSnapshot.AirBlockId, 0, 7, 99], [0, 1, 1, 0, 2, 0, 0, 1]) with
        {
            Itemset = new ItemsetBinding("MiniItemset", "Mini", "pack-a", "ab12cd34"),
        };

    [Fact]
    public void Same_fingerprint_is_exact_even_from_another_pack()
    {
        var result = SaveBinding.Check(Saved(), Itemset("MiniItemset", "ab12cd34", 0, 7), "pack-b");
        Assert.Equal(BindingStatus.Exact, result.Status);
        Assert.Empty(result.MissingBlockIds);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Fingerprint_comparison_ignores_hex_case()
    {
        var result = SaveBinding.Check(Saved(), Itemset("MiniItemset", "AB12CD34", 0, 7), "pack-a");
        Assert.Equal(BindingStatus.Exact, result.Status);
    }

    [Fact]
    public void Changed_content_with_every_used_block_present_is_ContentChanged()
    {
        // 99 is in the palette but unused, so its absence doesn't matter.
        var result = SaveBinding.Check(Saved(), Itemset("MiniItemset", "ffff0000", 0, 7, 12), "pack-a");
        Assert.Equal(BindingStatus.ContentChanged, result.Status);
        Assert.Empty(result.MissingBlockIds);
    }

    [Fact]
    public void Missing_used_blocks_are_Incompatible_and_listed_in_order()
    {
        var result = SaveBinding.Check(Saved(), Itemset("MiniItemset", "ffff0000", 12), "pack-a");
        Assert.Equal(BindingStatus.Incompatible, result.Status);
        Assert.Equal([0, 7], result.MissingBlockIds);
        Assert.Contains("0, 7", result.Message);
    }

    [Fact]
    public void BlockId_zero_counts_as_a_real_block()
    {
        var result = SaveBinding.Check(Saved(), Itemset("MiniItemset", "ffff0000", 7), "pack-a");
        Assert.Equal(BindingStatus.Incompatible, result.Status);
        Assert.Equal([0], result.MissingBlockIds);
    }

    [Fact]
    public void Different_key_is_DifferentItemset_regardless_of_blocks()
    {
        var result = SaveBinding.Check(Saved(), Itemset("DarthNoxItemset", "ab12cd34", 0, 7), "pack-a");
        Assert.Equal(BindingStatus.DifferentItemset, result.Status);
        Assert.Empty(result.MissingBlockIds);
        Assert.Contains("DarthNoxItemset", result.Message);
    }

    [Fact]
    public void All_air_world_is_never_incompatible()
    {
        var world = AllAir(3) with { Itemset = Saved().Itemset };
        var result = SaveBinding.Check(world, Itemset("MiniItemset", "ffff0000"), "pack-a");
        Assert.Equal(BindingStatus.ContentChanged, result.Status);
    }

    [Fact]
    public void Used_block_ids_skip_air_and_unreferenced_palette_entries()
    {
        Assert.Equal(new HashSet<int> { 0, 7 }, SaveBinding.UsedBlockIds(Saved()));
    }

    [Fact]
    public void Survives_a_round_trip_through_the_file_format()
    {
        var copy = FromBytes(ToBytes(Saved()));
        var result = SaveBinding.Check(copy, Itemset("MiniItemset", "ffff0000", 7), "pack-a");
        Assert.Equal([0], result.MissingBlockIds);
    }
}
