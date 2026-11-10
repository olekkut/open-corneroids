using Godot;
using OpenCorneroids.Simulation.Grids;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Builds one chunk's mesh: a quad for every block face that touches air or a see-through block (the rule is
/// <see cref="FaceVisibility.Drawn"/>; camera blocks are see-through as in the original). Opaque and
/// cutout (window) faces go into separate surfaces so they can use different materials. Blocks the grid marks inactive
/// (<see cref="BlockGrid.IsInactive"/>, unpowered on a ship) get their unpowered textures.
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

    /// <summary>
    /// The face of a turned block that looks along <paramref name="normal"/>: the block's own face that the turn brings
    /// there, keeping its texture (top, bottom or walls) and its texture's upright direction, carried round by the turn.
    /// </summary>
    static Face Turned(Vector3I normal, Orientation turn)
    {
        foreach (var own in Faces)
        {
            var n = turn.Apply(own.Normal.ToInt3()).ToGodot();
            if (n != normal) continue;
            var up = turn.Apply(own.Up.ToNumerics()).ToGodot();
            // Right x Up = Normal, as for every face in Faces, so the winding stays front-facing.
            return new Face(normal, up.Cross(normal), up, own.Uv);
        }
        throw new InvalidOperationException($"no face of a turned block looks along {normal}");
    }

    /// <summary>
    /// One grid block on its own, every face drawn, filling the cell from 0 to 1 and turned by <paramref name="turn"/>:
    /// the placement preview of a single-cell block.
    /// </summary>
    public static ArrayMesh SingleBlock(BlockPalette palette, ushort block, Orientation turn, Material material)
    {
        var surface = new Surface();
        var center = new Vector3(0.5f, 0.5f, 0.5f);
        foreach (var gridFace in Faces)
        {
            var face = turn == Orientation.Default ? gridFace : Turned(gridFace.Normal, turn);
            surface.AddQuad(center + (Vector3)face.Normal * 0.5f, face, palette.Uv(block, face.Uv));
        }
        var mesh = new ArrayMesh();
        surface.AppendTo(mesh, material);
        return mesh;
    }

    public static ArrayMesh? Build(BlockGrid world, Vector3I origin, int size, Material opaque, Material cutout)
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
            var self = palette.FaceBlockOf(block);
            bool active = !world.IsInactive(p);
            var center = new Vector3(p.X + 0.5f, p.Y + 0.5f, p.Z + 0.5f);
            var turn = world.TurnOf(p);
            foreach (var gridFace in Faces)
            {
                var face = turn == Orientation.Default ? gridFace : Turned(gridFace.Normal, turn);
                var next = p + face.Normal;
                var neighbour = world.Get(next);
                // Hidden behind an opaque block, between two panes of the same glass, or inside an opaque object model
                // that fills the neighbouring cell; never against a camera block, nor a camera's own faces.
                if (!FaceVisibility.Drawn(self, palette.FaceBlockOf(neighbour), neighbour == BlockPalette.Air && world.HidesFaces(next))) continue;
                (isCutout ? glass : solid).AddQuad(center + (Vector3)face.Normal * 0.5f, face, palette.Uv(block, face.Uv, active));
            }
        }

        if (solid.Vertices.Count == 0 && glass.Vertices.Count == 0) return null;
        var mesh = new ArrayMesh();
        solid.AppendTo(mesh, opaque);
        glass.AppendTo(mesh, cutout);
        return mesh;
    }
}
