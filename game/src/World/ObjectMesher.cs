using System.Runtime.InteropServices;
using Godot;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Grids;
using OpenCorneroids.Simulation.Shapes;
using Num = System.Numerics;
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

    /// <summary>The mesh at door offset 0, in the coordinates of the grid that owns the object (its node is the grid's child).</summary>
    /// <param name="active">Powered textures, or the block's unpowered (<c>deactiveTextures</c>) ones.</param>
    public static ArrayMesh Build(BlockShape shape, Int3 origin, Orientation o, TextureMapping mapping, BlockTextures textures, Material material,
        bool active = true)
    {
        var part = Part(shape, origin, o, mapping, textures, active);
        var mesh = new ArrayMesh();
        AddSurface(mesh, part.Positions, part.Normals, part.Uvs, part.Indices, material);
        return mesh;
    }

    /// <summary>The arrays <see cref="Build"/> draws, for merging many objects into one mesh (<see cref="MeshBatch"/>):
    /// built at <see cref="Int3.Zero"/>, one part serves every copy of the block in that orientation, moved by its origin.</summary>
    public static MeshPart Part(BlockShape shape, Int3 origin, Orientation o, TextureMapping mapping, BlockTextures textures, bool active = true)
    {
        var vertices = new List<Num.Vector3>();
        var normals = new List<Num.Vector3>();
        var uvs = new List<Num.Vector2>();
        var indices = new List<int>();

        foreach (var quad in ShapeQuads.Quads(shape, origin, o, default, mapping, active))
        {
            Vector3 a = quad.A.ToGodot(), b = quad.B.ToGodot(), d = quad.D.ToGodot();
            var normal = quad.Normal;
            var region = textures.Uv(quad.Region);
            // UvC holds the far corner: (1,1) when stretched, faceSize / regionSize when tiled.
            float uEnd = Math.Max(quad.UvC.X, MinPiece), vEnd = Math.Max(quad.UvC.Y, MinPiece);
            int columns = Math.Max(1, (int)Math.Ceiling(uEnd - MinPiece)), rows = Math.Max(1, (int)Math.Ceiling(vEnd - MinPiece));

            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                float u0 = column, u1 = Math.Min(column + 1, uEnd), v0 = row, v1 = Math.Min(row + 1, vEnd);
                Num.Vector3 At(float u, float v) => (a + (b - a) * (u / uEnd) + (d - a) * (v / vEnd)).ToNumerics();
                Num.Vector2 Tex(float u, float v)
                {
                    var t = region.Position + new Vector2(u - column, v - row) * region.Size;
                    return new Num.Vector2(t.X, t.Y);
                }

                int start = vertices.Count;
                vertices.AddRange([At(u0, v0), At(u1, v0), At(u1, v1), At(u0, v1)]);
                uvs.AddRange([Tex(u0, v0), Tex(u1, v0), Tex(u1, v1), Tex(u0, v1)]);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
            }
        }
        return new MeshPart([.. vertices], [.. normals], [.. uvs], [.. indices]);
    }

    /// <summary>Adds the arrays as one triangle surface of <paramref name="mesh"/> drawn with <paramref name="material"/>.
    /// The engine's vectors have the same layout as these, so the arrays are copied as they are.</summary>
    public static void AddSurface(ArrayMesh mesh, ReadOnlySpan<Num.Vector3> positions, ReadOnlySpan<Num.Vector3> normals,
        ReadOnlySpan<Num.Vector2> uvs, ReadOnlySpan<int> indices, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = MemoryMarshal.Cast<Num.Vector3, Vector3>(positions).ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = MemoryMarshal.Cast<Num.Vector3, Vector3>(normals).ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = MemoryMarshal.Cast<Num.Vector2, Vector2>(uvs).ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
    }
}

/// <summary>
/// Ours, for speed: each object model's arrays (<see cref="ObjectMesher.Part"/>), built once per block, orientation and
/// power state, so placing or loading a hundred thrusters meshes the thruster once. Shared by every grid of a world; a
/// different palette, atlas or texture mapping (another world) starts it over.
/// </summary>
public sealed class ObjectPartCache
{
    readonly Dictionary<(ushort Block, int Orientation, bool Active), MeshPart> _parts = new();
    BlockPalette? _palette;
    BlockTextures? _textures;
    TextureMapping _mapping;

    /// <summary>The block's model at origin zero, turned by <paramref name="o"/>, with its powered or unpowered textures.</summary>
    public MeshPart Get(BlockPalette palette, BlockTextures textures, TextureMapping mapping, ushort block, BlockShape shape,
        Orientation o, bool active)
    {
        if (!ReferenceEquals(palette, _palette) || !ReferenceEquals(textures, _textures) || mapping != _mapping)
        {
            _parts.Clear();
            _palette = palette;
            _textures = textures;
            _mapping = mapping;
        }
        var key = (block, o.Index, active);
        if (!_parts.TryGetValue(key, out var part))
            _parts[key] = part = ObjectMesher.Part(shape, Int3.Zero, o, mapping, textures, active);
        return part;
    }
}
