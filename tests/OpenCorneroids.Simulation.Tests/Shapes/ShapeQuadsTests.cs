using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;
using static OpenCorneroids.Simulation.Tests.Shapes.Support.ShapeData;

namespace OpenCorneroids.Simulation.Tests.Shapes;

public class ShapeQuadsTests
{
    static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    static IEnumerable<BlockShape> Shapes =>
    [
        Shape(Vector3.One), BlastDoor, BayDoor, SpinalCannon,
        Shape(new(1, 3, 1), new(1, 1, 1), new(0, -1, 0)),
        Shape(new(2, 7, 2), new(2, 2, 2), new(0, -2.5f, 0)),
    ];

    [Fact]
    public void Winding_is_clockwise_from_outside_with_right_cross_up_along_the_normal()
    {
        foreach (var shape in Shapes)
            foreach (var o in Orientation.All)
            {
                var origin = new Int3(3, -2, 8);
                var box = shape.ModelBox(origin, o);
                var quads = ShapeQuads.Quads(shape, origin, o);
                Assert.Equal(6, quads.Length);
                Assert.Equal(6, quads.Select(q => q.Normal).Distinct().Count());
                foreach (var q in quads)
                {
                    AssertClose(q.Normal, Vector3.Normalize(Vector3.Cross(q.B - q.A, q.A - q.D)));
                    // Right-handed coordinates: clockwise seen from outside means the triangle's (B−A)×(C−A) points in.
                    Assert.True(Vector3.Dot(Vector3.Cross(q.B - q.A, q.C - q.A), q.Normal) < 0);
                    // A parallelogram (rectangle), lying on the model box's side that faces Normal.
                    AssertClose(q.A + q.C, q.B + q.D);
                    var center = (q.A + q.B + q.C + q.D) / 4;
                    Assert.True(Vector3.Dot(center - box.Center, q.Normal) > 0);
                }
                AssertClose(box.Min, quads.SelectMany(q => new[] { q.A, q.B, q.C, q.D }).Aggregate(Vector3.Min));
                AssertClose(box.Max, quads.SelectMany(q => new[] { q.A, q.B, q.C, q.D }).Aggregate(Vector3.Max));
            }
    }

    [Fact]
    public void Top_bottom_and_walls_follow_the_local_axes_under_rotation()
    {
        foreach (var o in Orientation.All)
        {
            var quads = ShapeQuads.Quads(BlastDoor, Int3.Zero, o);
            var up = o.Up.ToInt3().ToVector3();
            Assert.Equal(up, Assert.Single(quads, q => q.Face == FaceKind.Top).Normal);
            Assert.Equal(-up, Assert.Single(quads, q => q.Face == FaceKind.Bottom).Normal);
            var walls = quads.Where(q => q.Face == FaceKind.Walls).ToList();
            Assert.Equal(4, walls.Count);
            foreach (var wall in walls)
            {
                Assert.Equal(0, Vector3.Dot(wall.Normal, up));
                // Wall textures stand upright in the block's own frame: their up is local +Y.
                AssertClose(up, Vector3.Normalize(wall.A - wall.D));
            }
        }
    }

    [Fact]
    public void Single_cell_default_matches_the_chunk_mesher()
    {
        var quads = ShapeQuads.Quads(Shape(Vector3.One), new Int3(4, 5, 6), Orientation.Default);
        var east = Assert.Single(quads, q => q.Normal == Vector3.UnitX);
        // Chunk mesher +X face: right (0,0,−1), up +Y, corners top-left, top-right, bottom-right, bottom-left.
        Assert.Equal(new Vector3(5, 6, 7), east.A);
        Assert.Equal(new Vector3(5, 6, 6), east.B);
        Assert.Equal(new Vector3(5, 5, 6), east.C);
        Assert.Equal(new Vector3(5, 5, 7), east.D);
        var top = Assert.Single(quads, q => q.Face == FaceKind.Top);
        // Top: right +X, up −Z.
        Assert.Equal(new Vector3(4, 6, 6), top.A);
        Assert.Equal(new Vector3(5, 6, 6), top.B);
        Assert.Equal(new Vector3(5, 6, 7), top.C);
        var bottom = Assert.Single(quads, q => q.Face == FaceKind.Bottom);
        // Bottom: right +X, up +Z.
        Assert.Equal(new Vector3(4, 5, 7), bottom.A);
        Assert.Equal(new Vector3(5, 5, 7), bottom.B);
    }

    [Fact]
    public void Stretch_maps_the_whole_region_once()
    {
        foreach (var o in Orientation.All)
            foreach (var q in ShapeQuads.Quads(SpinalCannon, Int3.Zero, o))
            {
                Assert.Equal(Vector2.Zero, q.UvA);
                Assert.Equal(new Vector2(1, 0), q.UvB);
                Assert.Equal(Vector2.One, q.UvC);
                Assert.Equal(new Vector2(0, 1), q.UvD);
            }
    }

    [Fact]
    public void Faces_carry_their_texture_region()
    {
        var quads = ShapeQuads.Quads(SpinalCannon, Int3.Zero, Orientation.Default);
        Assert.All(quads.Where(q => q.Face == FaceKind.Walls), q => Assert.Equal(new CellRect(12, 5, 2, 4), q.Region));
        Assert.Equal(new CellRect(1, 0, 1, 1), Assert.Single(quads, q => q.Face == FaceKind.Top).Region);
        Assert.Equal(new CellRect(2, 0, 1, 1), Assert.Single(quads, q => q.Face == FaceKind.Bottom).Region);

        var inactive = new FaceSet(new CellRect(8, 5, 2, 4), Cell, Cell);
        var gun = Def(new(3, 18, 3), walls: new CellRect(12, 5, 2, 4), inactive: inactive);
        var unpowered = ShapeQuads.Quads(BlockShape.Build(gun), Int3.Zero, Orientation.Default, active: false);
        Assert.All(unpowered.Where(q => q.Face == FaceKind.Walls), q => Assert.Equal(new CellRect(8, 5, 2, 4), q.Region));
    }

    [Fact]
    public void Tile_repeats_in_units_of_the_region()
    {
        // Spinal cannon: 3 wide, 18 tall walls over a 2 × 4 region.
        var wall = ShapeQuads.Quads(SpinalCannon, Int3.Zero, Orientation.Default, mapping: TextureMapping.Tile)
            .First(q => q.Face == FaceKind.Walls);
        Assert.Equal(Vector2.Zero, wall.UvA);
        Assert.Equal(new Vector2(1.5f, 0), wall.UvB);
        Assert.Equal(new Vector2(1.5f, 4.5f), wall.UvC);
        Assert.Equal(new Vector2(0, 4.5f), wall.UvD);

        // Blast door: the 2 × 2 front fits its 2 × 2 region exactly; the 1-deep sides show half of it.
        var door = ShapeQuads.Quads(BlastDoor, Int3.Zero, Orientation.Default, mapping: TextureMapping.Tile);
        Assert.Equal(Vector2.One, Assert.Single(door, q => q.Normal == Vector3.UnitZ).UvC);
        Assert.Equal(new Vector2(0.5f, 1), Assert.Single(door, q => q.Normal == Vector3.UnitX).UvC);
    }

    [Fact]
    public void Offset_moves_the_quads_in_local_space()
    {
        var o = Orientation.From(AxisDirection.NegZ, 1);
        var closed = ShapeQuads.Quads(BlastDoor, Int3.Zero, o);
        var open = ShapeQuads.Quads(BlastDoor, Int3.Zero, o, new Vector3(0, 2, 0));
        var shift = o.Apply(new Vector3(0, 2, 0));
        for (int i = 0; i < closed.Length; i++)
        {
            AssertClose(closed[i].A + shift, open[i].A);
            AssertClose(closed[i].C + shift, open[i].C);
        }
    }
}
