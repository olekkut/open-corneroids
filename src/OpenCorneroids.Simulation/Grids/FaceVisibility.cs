namespace OpenCorneroids.Simulation.Grids;

/// <summary>How a grid block takes part in hiding faces, for <see cref="FaceVisibility.Drawn"/>.</summary>
/// <param name="Block">The block's palette index; 0 is air.</param>
/// <param name="Cutout">Its texture has see-through pixels (a window).</param>
/// <param name="SeeThrough">It is see-through for its neighbours whatever its texture: a camera block, as in the
/// original.</param>
public readonly record struct FaceBlock(ushort Block, bool Cutout = false, bool SeeThrough = false)
{
    public bool IsAir => Block == 0;
}

/// <summary>
/// Which faces of a grid block the chunk mesh draws. A face against an opaque block is hidden, and so is one between two
/// panes of the same window. As in the original, a camera block is see-through: it draws all six of its faces, and its
/// neighbours keep their faces against it, so the view from a camera at its block's centre looks out between the hull
/// blocks around it instead of through them.
/// </summary>
public static class FaceVisibility
{
    /// <summary>
    /// Whether blocks of an item kind are see-through for face hiding (<see cref="FaceBlock.SeeThrough"/>): camera
    /// blocks, as in the original, so the view from a camera at its block's centre is not walled in by the hull around
    /// it. (The original treats guns, doors and lights the same way; ours keeps those opaque for now.)
    /// </summary>
    public static bool IsSeeThroughKind(string? kind) => kind == "camera";

    /// <summary>
    /// Whether a block placed as an object model hides the grid faces in the cells it fills: only an opaque one, so not a
    /// window (cutout) and not a see-through block such as a camera.
    /// </summary>
    public static bool ObjectHidesFaces(bool cutout, bool seeThrough) => !cutout && !seeThrough;

    /// <summary>
    /// Whether <paramref name="block"/>'s face towards <paramref name="neighbour"/> is drawn.
    /// </summary>
    /// <param name="neighbourHiddenByObject">When the neighbouring cell is air in the grid: an opaque object model fills
    /// it.</param>
    public static bool Drawn(FaceBlock block, FaceBlock neighbour, bool neighbourHiddenByObject = false)
    {
        if (block.IsAir) return false;
        if (block.SeeThrough) return true;
        if (neighbour.IsAir) return !neighbourHiddenByObject;
        if (neighbour.SeeThrough) return true;
        return neighbour.Cutout && neighbour.Block != block.Block;
    }
}
