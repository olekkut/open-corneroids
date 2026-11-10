using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;
using static OpenCorneroids.Simulation.Tests.Shapes.Support.ShapeData;

namespace OpenCorneroids.Simulation.Tests.Shapes;

public class ModelCellSpanTests
{
    /// <summary>The cells a door model fills entirely, by the rule itself: within the epsilon of full on every axis.</summary>
    static HashSet<Int3> FilledByRule(BlockShape shape, Int3 origin, Orientation o, Vector3 offset)
    {
        var box = shape.ModelBox(origin, o, offset);
        const float e = BlockShape.CellEpsilon;
        return [.. shape.ModelCells(origin, o, offset).Where(c =>
            box.Min.X <= c.X + e && box.Min.Y <= c.Y + e && box.Min.Z <= c.Z + e &&
            box.Max.X >= c.X + 1 - e && box.Max.Y >= c.Y + 1 - e && box.Max.Z >= c.Z + 1 - e)];
    }

    static readonly DoorComponent Door = new(new Vector3(0, 1, 0), 2, 1, 3000);

    public static TheoryData<int> Orientations()
    {
        var data = new TheoryData<int>();
        foreach (var o in Orientation.All) data.Add(o.Index);
        return data;
    }

    [Theory]
    [MemberData(nameof(Orientations))]
    public void Equal_spans_mean_equal_cells_along_a_door_slide(int orientation)
    {
        var o = Orientation.FromIndex(orientation);
        var origin = new Int3(5, 6, 7);
        var seen = new Dictionary<ModelCellSpan, (HashSet<Int3> Cells, HashSet<Int3> Full)>();
        for (int step = 0; step <= 400; step++)
        {
            var offset = DoorMotion.Offset(step / 400f, Door);
            var span = BlastDoor.CellSpan(origin, o, offset);
            var cells = BlastDoor.ModelCells(origin, o, offset).ToHashSet();
            var full = FilledByRule(BlastDoor, origin, o, offset);

            Assert.Equal(full, cells.Where(span.Fills).ToHashSet());
            if (seen.TryGetValue(span, out var before))
            {
                Assert.True(before.Cells.SetEquals(cells), $"step {step}: same span, different cells");
                Assert.True(before.Full.SetEquals(full), $"step {step}: same span, different filled cells");
            }
            else seen[span] = (cells, full);
        }
        // The door crosses cells as it slides, so its span does change.
        Assert.True(seen.Count > 2);
    }

    [Fact]
    public void A_closed_model_filling_its_cells_fills_all_of_them()
    {
        var span = BlastDoor.CellSpan(Int3.Zero, Orientation.Default);

        Assert.Equal(new Int3(0, 0, 0), span.Lo);
        Assert.Equal(new Int3(1, 1, 0), span.Hi);
        Assert.Equal(span.Lo, span.FullLo);
        Assert.Equal(span.Hi, span.FullHi);
    }

    [Fact]
    public void A_thin_model_covers_cells_but_fills_none()
    {
        var plate = Shape(new(1, 1, 1), new(1, 0.5f, 1));

        var span = plate.CellSpan(Int3.Zero, Orientation.Default);

        Assert.Equal(Int3.Zero, span.Lo);
        Assert.Equal(Int3.Zero, span.Hi);
        Assert.False(span.Fills(Int3.Zero));
    }
}
