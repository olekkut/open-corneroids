using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Shapes;

/// <summary>Which of the itemset's three face textures a face uses; decided by the face's LOCAL axis.</summary>
public enum FaceKind { Walls, Top, Bottom }

/// <summary>How a face texture region is laid over a face whose size differs from the region's.</summary>
public enum TextureMapping
{
    /// <summary>The whole region once over the whole face, stretched if needed. UVs run 0..1.</summary>
    Stretch,
    /// <summary>
    /// The region repeats every <c>W × H</c> cells of face, starting at corner A, so UVs run 0..faceSize/regionSize and
    /// may exceed 1. An atlas sampler cannot wrap inside one region, so the renderer must apply <c>fract()</c> to the UV
    /// before mapping it into the region (or slice the region into its own texture).
    /// </summary>
    Tile,
}

/// <summary>
/// One face of a block's model box in world space. Corners go A (top left) → B (top right) → C (bottom right) →
/// D (bottom left) as seen from outside, which is clockwise and Godot's front-face winding. "Right" is B − A and "up"
/// is A − D, and right × up = <see cref="Normal"/>, the same convention as the game's chunk mesher.
/// </summary>
/// <param name="UvA">
/// Normalised within <see cref="Region"/>: (0,0) is the region's top-left, +v down as in the atlas. Corner A gets (0,0),
/// B (u,0), C (u,v), D (0,v); u = v = 1 for <see cref="TextureMapping.Stretch"/>.
/// </param>
/// <param name="Region">The atlas cells this face's texture lives in.</param>
public readonly record struct ShapeQuad(
    Vector3 A, Vector3 B, Vector3 C, Vector3 D, FaceKind Face,
    Vector2 UvA, Vector2 UvB, Vector2 UvC, Vector2 UvD, Vector3 Normal, CellRect Region);

public static class ShapeQuads
{
    /// <param name="Right">Local screen-right of the face seen from outside.</param>
    /// <param name="Up">Local screen-up: +Y on walls so wall textures stand upright in the block's own frame.</param>
    readonly record struct LocalFace(Int3 Normal, Int3 Right, Int3 Up, FaceKind Kind);

    // The game's chunk mesher table, in the block's local frame: a single-cell block in the default orientation gets
    // exactly the faces and UVs the chunk mesher gives it. Top and bottom have local +X as right; top has −Z as up and
    // bottom +Z, so seen from above (top) or below (bottom) the texture is not mirrored.
    static readonly LocalFace[] Faces =
    [
        new(Int3.UnitX, -Int3.UnitZ, Int3.UnitY, FaceKind.Walls),
        new(-Int3.UnitX, Int3.UnitZ, Int3.UnitY, FaceKind.Walls),
        new(Int3.UnitZ, Int3.UnitX, Int3.UnitY, FaceKind.Walls),
        new(-Int3.UnitZ, -Int3.UnitX, Int3.UnitY, FaceKind.Walls),
        new(Int3.UnitY, Int3.UnitX, -Int3.UnitZ, FaceKind.Top),
        new(-Int3.UnitY, Int3.UnitX, Int3.UnitZ, FaceKind.Bottom),
    ];

    /// <summary>
    /// The six faces of the model box, in world space. Faces are never culled here: a multi-cell model's faces don't
    /// line up with cell faces, so hiding them against neighbours is the renderer's call.
    /// </summary>
    /// <param name="localOffset">Moves the model in local space first (door travel), as in <see cref="BlockShape.ModelBox"/>.</param>
    /// <param name="active">Powered textures, or <see cref="BlockShape.InactiveFaces"/>.</param>
    public static ShapeQuad[] Quads(BlockShape shape, Int3 origin, Orientation o, Vector3 localOffset = default,
        TextureMapping mapping = TextureMapping.Stretch, bool active = true)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var faceSet = shape.FacesFor(active);
        var center = shape.ModelCenter + localOffset;
        var half = shape.ModelSize * 0.5f;
        var quads = new ShapeQuad[Faces.Length];

        for (int i = 0; i < Faces.Length; i++)
        {
            var f = Faces[i];
            Vector3 normal = f.Normal, right = f.Right, up = f.Up;
            var faceCenter = center + normal * half;
            float halfWidth = Vector3.Dot(Vector3.Abs(right), half), halfHeight = Vector3.Dot(Vector3.Abs(up), half);
            Vector3 r = right * halfWidth, u = up * halfHeight;

            var region = f.Kind switch { FaceKind.Top => faceSet.Top, FaceKind.Bottom => faceSet.Bottom, _ => faceSet.Walls };
            var uvEnd = mapping == TextureMapping.Tile
                ? new Vector2(2 * halfWidth / Math.Max(region.W, 1), 2 * halfHeight / Math.Max(region.H, 1))
                : Vector2.One;

            quads[i] = new ShapeQuad(
                shape.ToWorld(faceCenter - r + u, origin, o),
                shape.ToWorld(faceCenter + r + u, origin, o),
                shape.ToWorld(faceCenter + r - u, origin, o),
                shape.ToWorld(faceCenter - r - u, origin, o),
                f.Kind,
                Vector2.Zero, new Vector2(uvEnd.X, 0), uvEnd, new Vector2(0, uvEnd.Y),
                o.Apply(normal),
                region);
        }
        return quads;
    }
}
