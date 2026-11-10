using System.Numerics;
using OpenCorneroids.Simulation.Creatures;
using OpenCorneroids.Simulation.Debugging;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Debugging;

/// <summary>The <c>stress</c> command's parsing, limits and layout (ours).</summary>
public class StressTestTests
{
    static StressRequest? Parse(string line, out string? error) =>
        StressTest.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), out error);

    [Theory]
    [InlineData("ship 6000", StressKind.Ship, 6000, CreatureKind.MineralParasite)]
    [InlineData("guns 64", StressKind.Guns, 64, CreatureKind.MineralParasite)]
    [InlineData("objects 500", StressKind.Objects, 500, CreatureKind.MineralParasite)]
    [InlineData("creatures 40", StressKind.Creatures, 40, CreatureKind.MineralParasite)]
    [InlineData("creatures scavenger 60", StressKind.Creatures, 60, CreatureKind.Scavenger)]
    [InlineData("CREATURES Eggs 3", StressKind.Creatures, 3, CreatureKind.SwarmEgg)]
    public void Parse_reads_the_kind_the_creature_and_the_count(string line, StressKind kind, int count, CreatureKind creature)
    {
        Assert.Equal(new StressRequest(kind, count, creature), Parse(line, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("", "Usage")]
    [InlineData("bananas 5", "Unknown stress test")]
    [InlineData("ship", "Usage")]
    [InlineData("ship many", "Usage")]
    [InlineData("ship 5 6", "Usage")]
    [InlineData("ship 0", "out of range")]
    [InlineData("ship 20001", "out of range")]
    [InlineData("guns 513", "out of range")]
    [InlineData("creatures dragon 5", "Unknown creature")]
    [InlineData("creatures scavenger", "Usage")]
    public void Parse_refuses_what_does_not_fit(string line, string expected)
    {
        Assert.Null(Parse(line, out var error));
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1000)]
    [InlineData(6000)]
    [InlineData(StressTest.MaxShipBlocks)]
    public void HullSize_holds_the_count_with_less_than_one_extra_layer(int blocks)
    {
        var size = StressTest.HullSize(blocks);
        long volume = size.Volume;
        Assert.True(volume >= blocks);
        Assert.True(volume - blocks < size.X * size.Z);
        Assert.Equal(size.X, size.Z);
        Assert.InRange(size.Y, 1, StressTest.MaxHullEdge);
        Assert.InRange(size.X, 1, StressTest.MaxHullEdge);
    }

    [Fact]
    public void HullSize_is_near_a_cube()
    {
        Assert.Equal(new Int3(18, 19, 18), StressTest.HullSize(6000));
    }

    [Fact]
    public void FaceSlots_space_fittings_evenly_over_every_face_top_first()
    {
        var size = new Int3(10, 10, 10);
        var slots = StressTest.FaceSlots(size, spacing: 5).ToList();

        Assert.Equal(6 * 2 * 2, slots.Count);   // slots at 2 and 7 both ways on each face
        Assert.All(slots.Take(4), s => Assert.Equal(Int3.UnitY, s.Normal));
        Assert.Contains((new Int3(2, 9, 7), Int3.UnitY), slots);
        foreach (var (cell, normal) in slots)
        {
            Assert.True(StressTest.IsShell(cell, size));
            var beyond = cell + normal;
            Assert.True(beyond.X is < 0 or >= 10 || beyond.Y is < 0 or >= 10 || beyond.Z is < 0 or >= 10);
        }
    }

    [Fact]
    public void PlateSize_has_room_on_top_for_every_gun_when_they_fit()
    {
        var plate = StressTest.PlateSize(64, spacing: 2);
        Assert.Equal(new Int3(16, 2, 16), plate);
        Assert.Equal(64, StressTest.FaceSlots(plate, 2).Count(s => s.Normal == Int3.UnitY));

        // Too many for the top alone: the edge stops at the limit and the bottom and sides take the rest.
        var big = StressTest.PlateSize(StressTest.MaxGuns, spacing: 2);
        Assert.Equal(StressTest.MaxHullEdge, big.X);
        Assert.True(StressTest.FaceSlots(big, 2).Count() >= StressTest.MaxGuns);
    }

    [Fact]
    public void IsShell_is_the_outside_layer_only()
    {
        var size = new Int3(3, 4, 5);
        int shell = 0;
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 5; z++)
            if (StressTest.IsShell(new Int3(x, y, z), size)) shell++;
        Assert.Equal(3 * 4 * 5 - 1 * 2 * 3, shell);
    }

    [Fact]
    public void SpreadDirection_stays_inside_the_cone_and_does_not_repeat()
    {
        var forward = new Vector3(0, 0, -1);
        const int count = 50;
        var directions = Enumerable.Range(0, count).Select(i => StressTest.SpreadDirection(forward, i, count, 30)).ToList();

        Assert.Equal(forward, directions[0]);
        float cosEdge = MathF.Cos(30 * MathF.PI / 180);
        Assert.All(directions, d =>
        {
            Assert.Equal(1, d.Length(), 4);
            Assert.True(Vector3.Dot(d, forward) >= cosEdge - 1e-4f);
        });
        Assert.Equal(cosEdge, Vector3.Dot(directions[^1], forward), 4);
        for (int i = 0; i < count; i++)
        for (int j = i + 1; j < count; j++)
            Assert.True(Vector3.Distance(directions[i], directions[j]) > 1e-3f);
    }

    [Fact]
    public void LatticePoint_fills_rows_then_layers()
    {
        Assert.Equal(new Int3(0, 0, 0), StressTest.LatticePoint(0, 3));
        Assert.Equal(new Int3(2, 0, 0), StressTest.LatticePoint(2, 3));
        Assert.Equal(new Int3(0, 0, 1), StressTest.LatticePoint(3, 3));
        Assert.Equal(new Int3(1, 1, 0), StressTest.LatticePoint(10, 3));
        Assert.Equal(27, Enumerable.Range(0, 27).Select(i => StressTest.LatticePoint(i, 3)).Distinct().Count());
    }

    [Fact]
    public void The_spawn_command_and_stress_share_creature_names()
    {
        Assert.Equal(CreatureKind.MineralParasite, StressTest.CreatureNamed("mineral"));
        Assert.Equal(CreatureKind.Needle, StressTest.CreatureNamed("Needle"));
        Assert.Null(StressTest.CreatureNamed("dragon"));
    }
}
