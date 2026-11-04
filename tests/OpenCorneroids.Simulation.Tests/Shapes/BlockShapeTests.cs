using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;
using static OpenCorneroids.Simulation.Tests.Shapes.Support.ShapeData;

namespace OpenCorneroids.Simulation.Tests.Shapes;

public class BlockShapeTests
{
    static readonly Int3[] Normals = [Int3.UnitX, -Int3.UnitX, Int3.UnitY, -Int3.UnitY, Int3.UnitZ, -Int3.UnitZ];

    static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    // ---- building ----

    [Fact]
    public void Single_cell_block_is_one_cell_with_the_model_filling_it()
    {
        var shape = Shape(Vector3.One);
        Assert.True(shape.IsSingleCell);
        Assert.Equal(Int3.One, shape.Size);
        Assert.Equal(Vector3.One, shape.ModelSize);
        Assert.Equal(new Vector3(0.5f), shape.ModelCenter);
        Assert.Empty(shape.Problems);
        Assert.False(Shape(new(1, 1, 1), new(1, 0.5f, 1)).IsSingleCell);
        Assert.False(BlastDoor.IsSingleCell);
    }

    [Fact]
    public void Model_size_zero_means_the_bounds()
    {
        Assert.Equal(new Vector3(3, 18, 3), SpinalCannon.ModelSize);
        Assert.Equal(new Vector3(2, 2, 1), BlastDoor.ModelSize);
        Assert.Equal(new Vector3(1, 1, 0.5f), BlastDoor.ModelCenter);
    }

    [Fact]
    public void Faces_and_inactive_faces_come_from_the_definition()
    {
        var active = new FaceSet(new CellRect(1, 1, 1, 2), new CellRect(2, 2, 1, 1), new CellRect(3, 3, 1, 1));
        var inactive = new FaceSet(new CellRect(4, 4, 1, 2), new CellRect(5, 5, 1, 1), new CellRect(6, 6, 1, 1));
        var powered = BlockShape.Build(new BlockDef(1, 1, 1, 1, new(1, 2, 1), Vector3.Zero, Vector3.Zero, active, inactive, 5));
        Assert.Same(active, powered.Faces);
        Assert.Same(inactive, powered.InactiveFaces);
        Assert.True(powered.HasInactiveFaces);
        Assert.Same(inactive, powered.FacesFor(active: false));

        var plain = BlockShape.Build(new BlockDef(1, 1, 1, 1, Vector3.One, Vector3.Zero, Vector3.Zero, active, null, null));
        Assert.Same(active, plain.InactiveFaces);
        Assert.False(plain.HasInactiveFaces);
    }

    [Fact]
    public void Bad_numbers_are_clamped_without_throwing()
    {
        var shape = Shape(new(0, -3, float.NaN), new(-1, 0, float.PositiveInfinity), new(float.NaN, 0.5f, 0));
        Assert.Equal(Int3.One, shape.Size);
        Assert.Equal(Vector3.One, shape.ModelSize);
        Assert.Equal(Vector3.Zero, shape.ModelPlacement);   // y 0.5 would push the model out of its one cell
        Assert.Equal(8, shape.Problems.Count);

        var huge = Shape(new(1000, 2.4f, 1e30f));
        Assert.Equal(new Int3(BlockShape.MaxEdge, 2, BlockShape.MaxEdge), huge.Size);
        Assert.Equal(3, huge.Problems.Count);
        Assert.Throws<ArgumentNullException>(() => BlockShape.Build(null!));
    }

    [Fact]
    public void Models_are_kept_inside_their_bounds()
    {
        // DarthNox's small ion thruster: a 3-cell model at placement −2.5 in 7-cell bounds would hang half a cell below.
        var ion = Shape(new(1, 7, 1), new(1, 3, 1), new(0, -2.5f, 0));
        Assert.Equal(new Vector3(0, -2, 0), ion.ModelPlacement);
        Assert.Single(ion.Problems);
        Assert.Equal([0, 1, 2], ion.ModelCells(Int3.Zero, Orientation.Default).Select(c => c.Y));

        var oversized = Shape(new(1, 2, 1), new(3, 1, 1));
        Assert.Equal(new Vector3(1, 1, 1), oversized.ModelSize);
        Assert.Single(oversized.Problems);
    }

    // ---- real data ----

    [Theory]
    [MemberData(nameof(RealMultiBlocks), MemberType = typeof(Support.ShapeData))]
    public void Real_blocks_have_the_expected_model_box_in_the_default_orientation(
        string name, Vector3 size, Vector3 model, Vector3 placement, float modelMinY, float modelMaxY)
    {
        var shape = Shape(size, model, placement);
        Assert.Empty(shape.Problems);
        var origin = new Int3(10, 20, 30);

        var footprint = shape.Footprint(origin, Orientation.Default).ToList();
        Assert.Equal((int)(size.X * size.Y * size.Z), footprint.Count);
        Assert.Equal(footprint.Count, footprint.Distinct().Count());
        Assert.Equal(origin, footprint.Aggregate(Int3.Min));
        Assert.Equal(origin + shape.Size - Int3.One, footprint.Aggregate(Int3.Max));

        var box = shape.ModelBox(origin, Orientation.Default);
        var visual = model == Vector3.Zero ? size : model;
        AssertClose(new Vector3(origin.X, origin.Y + modelMinY, origin.Z), box.Min);
        AssertClose(new Vector3(origin.X + visual.X, origin.Y + modelMaxY, origin.Z + visual.Z), box.Max);

        var cells = shape.ModelCells(origin, Orientation.Default).ToList();
        Assert.Equal((int)(visual.X * visual.Y * visual.Z), cells.Count);
        Assert.All(cells, c => Assert.Contains(c, footprint));
        _ = name;
    }

    [Theory]
    [MemberData(nameof(RealMultiBlocks), MemberType = typeof(Support.ShapeData))]
    public void Real_blocks_stay_consistent_in_every_orientation(
        string name, Vector3 size, Vector3 model, Vector3 placement, float modelMinY, float modelMaxY)
    {
        var shape = Shape(size, model, placement);
        var origin = new Int3(-5, 7, 3);
        var visual = model == Vector3.Zero ? size : model;
        foreach (var o in Orientation.All)
        {
            var rotated = shape.RotatedSize(o);
            Assert.Equal(shape.Size.Volume, rotated.Volume);
            var footprint = shape.Footprint(origin, o).ToHashSet();
            Assert.Equal(shape.Size.Volume, footprint.Count);
            Assert.Equal(origin, footprint.Aggregate(Int3.Min));
            Assert.Equal(origin + rotated - Int3.One, footprint.Aggregate(Int3.Max));

            var box = shape.ModelBox(origin, o);
            Assert.Equal(visual.X * visual.Y * visual.Z, box.Size.X * box.Size.Y * box.Size.Z, 3);
            Assert.True(Vector3.Min(box.Min, origin) == origin && Vector3.Max(box.Max, origin + rotated) == (Vector3)(origin + rotated),
                $"{name} {o}: model {box} outside bounds {origin}..{origin + rotated}");
            var cells = shape.ModelCells(origin, o).ToList();
            Assert.Equal((int)MathF.Round(visual.X * visual.Y * visual.Z), cells.Count);
            Assert.All(cells, c => Assert.Contains(c, footprint));

            // Local ↔ world round trip.
            var p = new Vector3(0.25f, 1.5f, 0.75f);
            AssertClose(p, shape.ToLocal(shape.ToWorld(p, origin, o), origin, o));
        }
        _ = (modelMinY, modelMaxY);
    }

    [Fact]
    public void Door_slides_into_the_top_half_of_its_bounds()
    {
        var closed = BlastDoor.ModelCells(Int3.Zero, Orientation.Default).ToHashSet();
        var open = BlastDoor.ModelCells(Int3.Zero, Orientation.Default, new Vector3(0, 2, 0)).ToHashSet();
        Assert.All(closed, c => Assert.InRange(c.Y, 0, 1));
        Assert.All(open, c => Assert.InRange(c.Y, 2, 3));
        Assert.Equal(BlastDoor.Footprint(Int3.Zero, Orientation.Default).ToHashSet(), closed.Union(open).ToHashSet());

        // Half way (1.5..3.5) straddles three rows; the epsilon keeps a near-whole position (1.00001..3.00001) to two.
        Assert.Equal([1, 2, 3], BlastDoor.ModelCells(Int3.Zero, Orientation.Default, new Vector3(0, 1.5f, 0)).Select(c => c.Y).Distinct().Order());
        Assert.Equal([1, 2], BlastDoor.ModelCells(Int3.Zero, Orientation.Default, new Vector3(0, 1.00001f, 0)).Select(c => c.Y).Distinct().Order());
    }

    [Fact]
    public void Bay_door_opens_eleven_cells_up_its_own_axis()
    {
        var origin = new Int3(3, 4, 5);
        var closed = BayDoor.ModelBox(origin, Orientation.Default);
        AssertClose(new Vector3(3, 4, 5), closed.Min);
        AssertClose(new Vector3(14, 15, 7), closed.Max);
        var open = BayDoor.ModelBox(origin, Orientation.Default, new Vector3(0, 11, 0));
        AssertClose(new Vector3(3, 15, 5), open.Min);
        AssertClose(new Vector3(14, 26, 7), open.Max);
        Assert.Equal(11 * 11 * 2, BayDoor.ModelCells(origin, Orientation.Default).Count());
        Assert.Equal(11 * 22 * 2, BayDoor.Footprint(origin, Orientation.Default).Count());
    }

    [Fact]
    public void Turned_door_slides_along_its_local_up()
    {
        // Up +X: local +Y points along world +X, so the door is 4 wide in X, 2 tall in Y and opens toward +X.
        var o = Orientation.From(AxisDirection.PosX, 0);
        var origin = new Int3(10, 10, 10);
        Assert.Equal(new Int3(4, 2, 1), BlastDoor.RotatedSize(o));
        var closed = BlastDoor.ModelBox(origin, o);
        AssertClose(new Vector3(10, 10, 10), closed.Min);
        AssertClose(new Vector3(12, 12, 11), closed.Max);
        var open = BlastDoor.ModelBox(origin, o, new Vector3(0, 2, 0));
        AssertClose(new Vector3(12, 10, 10), open.Min);
        AssertClose(new Vector3(14, 12, 11), open.Max);
    }

    [Fact]
    public void Upside_down_thruster_keeps_its_model_at_the_far_end_of_its_bounds()
    {
        var thruster = Shape(new(1, 3, 1), new(1, 1, 1), new(0, -1, 0));
        var o = Orientation.From(AxisDirection.NegY, 0);
        var cells = thruster.ModelCells(Int3.Zero, o).ToList();
        Assert.Equal([new Int3(0, 2, 0)], cells);
    }

    // ---- placing against a face ----

    [Fact]
    public void Anchor_for_a_door_on_each_face()
    {
        var place = new Int3(5, 5, 5);
        var o = Orientation.Default;   // 2 × 4 × 1
        Assert.Equal(new Int3(5, 5, 5), BlastDoor.AnchorFor(place, Int3.UnitY, o));
        Assert.Equal(new Int3(5, 2, 5), BlastDoor.AnchorFor(place, -Int3.UnitY, o));
        Assert.Equal(new Int3(5, 4, 5), BlastDoor.AnchorFor(place, Int3.UnitX, o));
        Assert.Equal(new Int3(4, 4, 5), BlastDoor.AnchorFor(place, -Int3.UnitX, o));
        Assert.Equal(new Int3(5, 4, 5), BlastDoor.AnchorFor(place, Int3.UnitZ, o));
        Assert.Equal(new Int3(5, 4, 5), BlastDoor.AnchorFor(place, -Int3.UnitZ, o));
        Assert.Throws<ArgumentException>(() => BlastDoor.AnchorFor(place, new Int3(1, 1, 0), o));
        Assert.Throws<ArgumentException>(() => BlastDoor.AnchorFor(place, Int3.Zero, o));
    }

    [Theory]
    [MemberData(nameof(RealMultiBlocks), MemberType = typeof(Support.ShapeData))]
    public void Anchor_contains_the_cell_stays_on_its_side_and_is_centred(
        string name, Vector3 size, Vector3 model, Vector3 placement, float modelMinY, float modelMaxY)
    {
        var shape = Shape(size, model, placement);
        var place = new Int3(40, 41, 42);
        foreach (var o in Orientation.All)
            foreach (var normal in Normals)
            {
                var origin = shape.AnchorFor(place, normal, o);
                var footprint = shape.Footprint(origin, o).ToHashSet();
                Assert.Contains(place, footprint);
                Assert.DoesNotContain(place - normal, footprint);
                Assert.All(footprint, c => Assert.True(Int3.Dot(c - place, normal) >= 0, $"{name} {o} {normal}: {c} is behind the face"));
                var rotated = shape.RotatedSize(o);
                for (int axis = 0; axis < 3; axis++)
                {
                    if (normal[axis] != 0) continue;
                    // Centred; an even extent puts the extra cell on the positive side.
                    Assert.Equal(-(rotated[axis] - 1) / 2, origin[axis] - place[axis]);
                }
            }
        _ = (modelMinY, modelMaxY);
    }
}
