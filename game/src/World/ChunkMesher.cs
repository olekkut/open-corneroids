using Godot;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Builds one chunk's mesh: a quad for every block face that touches air or a see-through block. Opaque and
/// cutout (window) faces go into separate surfaces so they can use different materials.
/// </summary>
public static class ChunkMesher
{
    /// <param name="Normal">Outward normal.</param>
    /// <param name="Right">Screen-right when looking at the face from outside.</param>
    /// <param name="Up">Screen-up when looking at the face from outside; +Y on walls, so textures stand upright.</param>
    /// <param name="Uv">0 = walls, 1 = top, 2 = bottom (the itemset's three face textures).</param>
    readonly record struct Face(Vector3I Normal, Vector3 Right, Vector3 Up, int Uv);

    // Right x Up = Normal for every face, so the corners below are clockwise as seen from outside,
    // which is Godot's front-face winding.
    static readonly Face[] Faces =
    [
        new(Vector3I.Right, new(0, 0, -1), Vector3.Up, 0),
        new(Vector3I.Left, new(0, 0, 1), Vector3.Up, 0),
        new(Vector3I.Back, new(1, 0, 0), Vector3.Up, 0),      // +Z
        new(Vector3I.Forward, new(-1, 0, 0), Vector3.Up, 0),  // -Z
        new(Vector3I.Up, new(1, 0, 0), new(0, 0, -1), 1),
        new(Vector3I.Down, new(1, 0, 0), new(0, 0, 1), 2),
    ];

    sealed class Surface
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<int> Indices = new();

        public void AddQuad(Vector3 center, Face face, Rect2 uv)
        {
            int start = Vertices.Count;
            Vector3 r = face.Right * 0.5f, u = face.Up * 0.5f;
            Vertices.Add(center - r + u);   // top left
            Vertices.Add(center + r + u);   // top right
            Vertices.Add(center + r - u);   // bottom right
            Vertices.Add(center - r - u);   // bottom left
            Uvs.Add(uv.Position);
            Uvs.Add(new Vector2(uv.End.X, uv.Position.Y));
            Uvs.Add(uv.End);
            Uvs.Add(new Vector2(uv.Position.X, uv.End.Y));
            for (int i = 0; i < 4; i++) Normals.Add((Vector3)face.Normal);
            Indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }

        public void AppendTo(ArrayMesh mesh, Material material)
        {
            if (Vertices.Count == 0) return;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = Normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = Uvs.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = Indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
    }

    public static ArrayMesh? Build(VoxelWorld world, Vector3I origin, int size, Material opaque, Material cutout)
    {
        var palette = world.Palette;
        var solid = new Surface();
        var glass = new Surface();
        for (int z = 0; z < size; z++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p = origin + new Vector3I(x, y, z);
            var block = world.Get(p);
            if (block == BlockPalette.Air) continue;
            bool isCutout = palette.IsCutout(block);
            var center = new Vector3(p.X + 0.5f, p.Y + 0.5f, p.Z + 0.5f);
            foreach (var face in Faces)
            {
                var neighbour = world.Get(p + face.Normal);
                // Hidden behind an opaque block, or between two panes of the same glass.
                if (neighbour != BlockPalette.Air && (!palette.IsCutout(neighbour) || neighbour == block)) continue;
                (isCutout ? glass : solid).AddQuad(center + (Vector3)face.Normal * 0.5f, face, palette.Uv(block, face.Uv));
            }
        }

        if (solid.Vertices.Count == 0 && glass.Vertices.Count == 0) return null;
        var mesh = new ArrayMesh();
        solid.AppendTo(mesh, opaque);
        glass.AppendTo(mesh, cutout);
        return mesh;
    }
}
