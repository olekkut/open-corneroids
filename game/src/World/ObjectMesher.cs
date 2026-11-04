using Godot;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Shapes;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Meshes one world object's model box from <see cref="ShapeQuads"/>, with the block atlas material.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TextureMapping.Tile"/> needs the UV to wrap inside one atlas region, which a sampler over the whole
/// atlas cannot do. Rather than a second shader that would need the region rectangle per vertex (a custom vertex
/// attribute), each face is cut into region-sized pieces here, each piece mapping its own part of the region: the
/// last row and column are partial when the face is not a whole number of regions. The block shader stays the one
/// the chunk mesher uses, and <see cref="TextureMapping.Stretch"/> is simply the case of one piece per face. Even the
/// largest known model (DarthNox's 11×11 bay door with a 2×2 region) is only 36 pieces a face.
/// </para>
/// <para>
/// Faces are never culled against neighbours: a model's faces don't line up with cell faces in general, and a model
/// face against a grid block faces into it and is back-face culled anyway.
/// </para>
/// </remarks>
public static class ObjectMesher
{
    /// <summary>A piece smaller than this fraction of a region is dropped; float noise, not texture.</summary>
    const float MinPiece = 1e-3f;

    /// <summary>The mesh at door offset 0, in world coordinates (the world node sits at the origin).</summary>
    public static ArrayMesh Build(BlockShape shape, Int3 origin, Orientation o, TextureMapping mapping, BlockTextures textures, Material material)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();

        foreach (var quad in ShapeQuads.Quads(shape, origin, o, default, mapping))
        {
            Vector3 a = quad.A.ToGodot(), b = quad.B.ToGodot(), d = quad.D.ToGodot();
            var normal = quad.Normal.ToGodot();
            var region = textures.Uv(quad.Region);
            // UvC holds the far corner: (1,1) when stretched, faceSize / regionSize when tiled.
            float uEnd = Math.Max(quad.UvC.X, MinPiece), vEnd = Math.Max(quad.UvC.Y, MinPiece);
            int columns = Math.Max(1, (int)Math.Ceiling(uEnd - MinPiece)), rows = Math.Max(1, (int)Math.Ceiling(vEnd - MinPiece));

            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                float u0 = column, u1 = Math.Min(column + 1, uEnd), v0 = row, v1 = Math.Min(row + 1, vEnd);
                Vector3 At(float u, float v) => a + (b - a) * (u / uEnd) + (d - a) * (v / vEnd);
                Vector2 Tex(float u, float v) => region.Position + new Vector2(u - column, v - row) * region.Size;

                int start = vertices.Count;
                vertices.AddRange([At(u0, v0), At(u1, v0), At(u1, v1), At(u0, v1)]);
                uvs.AddRange([Tex(u0, v0), Tex(u1, v0), Tex(u1, v1), Tex(u0, v1)]);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }
}
