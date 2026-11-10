using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Simulation.Grids;
using OpenCorneroids.Simulation.Settings;
using OpenCorneroids.Simulation.Shapes;
using static OpenCorneroids.Simulation.Tests.Shapes.Support.ShapeData;

namespace OpenCorneroids.Simulation.Tests.Grids;

public class MeshBatchTests
{
    /// <summary>One triangle: three vertices with distinct normals and texture coordinates.</summary>
    static MeshPart Triangle(float shift = 0) => new(
        [new(0 + shift, 0, 0), new(1 + shift, 0, 0), new(0 + shift, 1, 0)],
        [Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX],
        [new(0, 0), new(1, 0), new(0, 1)],
        [0, 1, 2]);

    /// <summary>A quad as two triangles.</summary>
    static MeshPart Quad() => new(
        [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)],
        [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
        [0, 1, 2, 0, 2, 3]);

    [Fact]
    public void Each_copy_is_moved_by_its_offset_and_its_indices_follow_the_vertices_before_it()
    {
        var batch = new MeshBatch();
        batch.Add(Quad(), new Vector3(10, 0, 0));
        batch.Add(Triangle(), new Vector3(0, 5, -2));

        Assert.Equal(7, batch.VertexCount);
        Assert.Equal(9, batch.IndexCount);
        Assert.Equal(new Vector3(10, 0, 0), batch.Positions[0]);
        Assert.Equal(new Vector3(11, 1, 0), batch.Positions[2]);
        Assert.Equal(new Vector3(0, 5, -2), batch.Positions[4]);
        Assert.Equal(new Vector3(1, 5, -2), batch.Positions[5]);
        Assert.Equal([0, 1, 2, 0, 2, 3, 4, 5, 6], batch.Indices.ToArray());
        // Normals and texture coordinates are copied as they are: moving a model changes neither.
        Assert.Equal([.. Quad().Normals, .. Triangle().Normals], batch.Normals.ToArray());
        Assert.Equal([.. Quad().Uvs, .. Triangle().Uvs], batch.Uvs.ToArray());
    }

    [Fact]
    public void Merging_copies_draws_the_same_triangles_as_separate_meshes()
    {
        var parts = new[] { (Quad(), new Vector3(3, 4, 5)), (Triangle(0.5f), new Vector3(-1, 0, 7)), (Quad(), Vector3.Zero) };
        var batch = new MeshBatch();
        foreach (var (part, offset) in parts) batch.Add(part, offset);

        var merged = Triangles(batch.Positions.ToArray(), batch.Indices.ToArray());
        var separate = parts.SelectMany(p => Triangles([.. p.Item1.Positions.Select(v => v + p.Item2)], p.Item1.Indices)).ToList();
        Assert.Equal(separate, merged);
    }

    static List<(Vector3, Vector3, Vector3)> Triangles(Vector3[] positions, int[] indices) =>
        [.. Enumerable.Range(0, indices.Length / 3).Select(t => (positions[indices[3 * t]], positions[indices[3 * t + 1]], positions[indices[3 * t + 2]]))];

    [Fact]
    public void Clear_starts_an_empty_batch()
    {
        var batch = new MeshBatch();
        batch.Add(Quad(), Vector3.One);
        batch.Clear();
        Assert.True(batch.IsEmpty);
        Assert.Equal(0, batch.IndexCount);

        batch.Add(Triangle(), Vector3.Zero);
        Assert.Equal([0, 1, 2], batch.Indices.ToArray());
        Assert.Equal(Vector3.Zero, batch.Positions[0]);
    }

    [Fact]
    public void A_part_with_mismatched_arrays_or_stray_indices_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new MeshPart([Vector3.Zero], [], [Vector2.Zero], []));
        Assert.Throws<ArgumentException>(() => new MeshPart([Vector3.Zero], [Vector3.Zero], [Vector2.Zero], [0, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeshPart([Vector3.Zero], [Vector3.Zero], [Vector2.Zero], [0, 0, 1]));
    }

    /// <summary>
    /// One cached part, built at origin zero, serves every placed copy only because a model's faces depend on its origin
    /// by a translation alone: same texture coordinates, regions and normals, corners moved by the origin.
    /// </summary>
    [Theory]
    [InlineData(TextureMapping.Stretch)]
    [InlineData(TextureMapping.Tile)]
    public void A_model_built_at_origin_zero_and_moved_matches_the_model_built_in_place(TextureMapping mapping)
    {
        var origins = new[] { new Int3(3, 7, 11), new Int3(60, 1, 33), new Int3(127, 127, 0) };
        foreach (var shape in new[] { BlastDoor, BayDoor, SpinalCannon, Shape(new(1, 3, 1), new(1, 1, 1), new(0, -1, 0)) })
            foreach (var o in Orientation.All)
            {
                var atZero = ShapeQuads.Quads(shape, Int3.Zero, o, default, mapping);
                foreach (var origin in origins)
                {
                    var inPlace = ShapeQuads.Quads(shape, origin, o, default, mapping);
                    Assert.Equal(atZero.Length, inPlace.Length);
                    for (int i = 0; i < atZero.Length; i++)
                    {
                        var (z, p) = (atZero[i], inPlace[i]);
                        Assert.Equal((z.UvA, z.UvB, z.UvC, z.UvD, z.Region, z.Normal, z.Face), (p.UvA, p.UvB, p.UvC, p.UvD, p.Region, p.Normal, p.Face));
                        Vector3 shift = origin;
                        foreach (var (moved, built) in new[] { (z.A + shift, p.A), (z.B + shift, p.B), (z.C + shift, p.C), (z.D + shift, p.D) })
                            Assert.True(Vector3.Distance(moved, built) < 1e-4f, $"{shape.Size} {o.Index} {origin}: {moved} vs {built}");
                    }
                }
            }
    }

    [Fact]
    public void Merged_object_meshes_are_on_by_default_as_a_player_preference()
    {
        Assert.Equal("cl_batch_objects", Cvars.BatchObjects.Name);
        Assert.Equal(1, Cvars.BatchObjects.Default);
        Assert.Contains(Cvars.BatchObjects, Cvars.All);
        Assert.False(Cvars.BatchObjects.Flags.HasFlag(CvarFlags.World));
    }
}
