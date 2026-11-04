using System.Numerics;
using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Shapes;

/// <summary>An axis-aligned box in world space.</summary>
public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;
    public Vector3 Center => (Min + Max) * 0.5f;
}

/// <summary>
/// The geometry of one block type, taken from its <see cref="BlockDef"/>: placement bounds, visible model box and the
/// face textures, with the rules for turning and placing it in the grid.
/// </summary>
/// <remarks>
/// <para>
/// Local block coordinates put the placement bounds at [0, <see cref="Size"/>) on every axis. The model box is centred
/// at <c>Size / 2 + ModelPlacement</c> and is <see cref="ModelSize"/> big (BYOA design doc §1.2). This matches all the
/// vanilla data: the weak hydrogen thruster (size 1×3×1, model 1×1×1, placement y −1) has its model centred at y 0.5, the
/// bottom cell, leaving the two cells above for exhaust; the blast door (2×4×1, model 2×2×1, placement y −1) fills the
/// bottom half and sliding +y by its distance 2 fills the top half.
/// </para>
/// <para>
/// In the world, a block placed at <c>origin</c> with orientation <c>o</c> maps a local point p to
/// <c>origin + o·p − m</c>, where m is the minimum corner of the rotated bounds. The rotated bounds therefore always
/// start at <c>origin</c>, so the origin is the footprint's minimum cell whatever the orientation.
/// </para>
/// </remarks>
public sealed class BlockShape
{
    /// <summary>
    /// Largest accepted edge, in cells, for bounds and model. The biggest known block (DarthNox's bay door) is 22 tall;
    /// the cap stops a broken or hostile mod from asking for footprints of billions of cells. Tunable.
    /// </summary>
    public const int MaxEdge = 64;

    /// <summary>Distance treated as "on the boundary" when turning the model box into cells.</summary>
    public const float CellEpsilon = 1e-3f;

    /// <summary>Placement bounds in cells, each at least 1.</summary>
    public Int3 Size { get; }
    /// <summary>The visible model's extent (<see cref="BlockDef.VisualSize"/>), each positive.</summary>
    public Vector3 ModelSize { get; }
    /// <summary>Offset of the model's centre from the bounds' centre, local.</summary>
    public Vector3 ModelPlacement { get; }
    /// <summary>Centre of the model box in local coordinates.</summary>
    public Vector3 ModelCenter => Size.ToVector3() * 0.5f + ModelPlacement;
    /// <summary>Textures while powered (or always, for unpowered blocks).</summary>
    public FaceSet Faces { get; }
    /// <summary>Textures while unpowered; the same as <see cref="Faces"/> when the itemset gives none.</summary>
    public FaceSet InactiveFaces { get; }
    public bool HasInactiveFaces { get; }
    /// <summary>
    /// One cell of bounds with the model filling exactly that cell, so the block can go through the ordinary per-cell
    /// chunk mesher. A 1×1×1 block whose model is smaller or shifted is not single-cell in this sense.
    /// </summary>
    public bool IsSingleCell { get; }
    /// <summary>What <see cref="Build(BlockDef)"/> had to correct, for diagnostics; empty for sane data.</summary>
    public IReadOnlyList<string> Problems { get; }

    BlockShape(Int3 size, Vector3 modelSize, Vector3 modelPlacement, FaceSet faces, FaceSet? inactive, IReadOnlyList<string> problems)
    {
        Size = size;
        ModelSize = modelSize;
        ModelPlacement = modelPlacement;
        Faces = faces;
        InactiveFaces = inactive ?? faces;
        HasInactiveFaces = inactive is not null;
        Problems = problems;
        IsSingleCell = size == Int3.One && modelSize == Vector3.One && modelPlacement == Vector3.Zero;
    }

    public static BlockShape Build(BlockDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        return Build(def.Size, def.ModelSize, def.ModelPlacement, def.Active, def.Inactive);
    }

    /// <summary>
    /// Never throws on bad numbers: mods contain typos, and one broken block should not stop an itemset from loading.
    /// Bounds are rounded to whole cells and clamped to 1..<see cref="MaxEdge"/>. A zero model size means "same as the
    /// bounds" (as in the itemset format); a zero, negative or non-finite model component falls back to the bounds on
    /// that axis. Non-finite placement becomes 0. A model larger than the bounds is shrunk to them, and one placed
    /// (partly) outside them is moved in. Each correction is recorded in <see cref="Problems"/>.
    /// </summary>
    public static BlockShape Build(Vector3 size, Vector3 modelSize, Vector3 modelPlacement, FaceSet faces, FaceSet? inactive = null)
    {
        ArgumentNullException.ThrowIfNull(faces);
        var problems = new List<string>();

        var cells = new Int3(Cells(size.X, "x"), Cells(size.Y, "y"), Cells(size.Z, "z"));
        int Cells(float value, string axis)
        {
            if (!float.IsFinite(value) || value < 0.5f)
            {
                problems.Add($"size {axis} {value} is not a positive number of cells; using 1");
                return 1;
            }
            if (value > MaxEdge)
            {
                problems.Add($"size {axis} {value} is over the {MaxEdge} cell limit; clamped");
                return MaxEdge;
            }
            var rounded = (int)MathF.Round(value, MidpointRounding.AwayFromZero);
            if (rounded != value) problems.Add($"size {axis} {value} is not a whole number of cells; rounded to {rounded}");
            return rounded;
        }

        var model = modelSize == Vector3.Zero
            ? cells.ToVector3()
            : new Vector3(Model(modelSize.X, cells.X, "x"), Model(modelSize.Y, cells.Y, "y"), Model(modelSize.Z, cells.Z, "z"));
        float Model(float value, int fallback, string axis)
        {
            if (float.IsFinite(value) && value > 0) return Math.Min(value, MaxEdge);
            problems.Add($"model size {axis} {value} is not positive; using the bounds' {fallback}");
            return fallback;
        }

        var placement = new Vector3(Place(modelPlacement.X, "x"), Place(modelPlacement.Y, "y"), Place(modelPlacement.Z, "z"));
        float Place(float value, string axis)
        {
            if (float.IsFinite(value)) return Math.Clamp(value, -MaxEdge, MaxEdge);
            problems.Add($"model placement {axis} {value} is not finite; using 0");
            return 0;
        }

        // Keep the model inside the bounds, so its cells (solid for collision) never reach into a neighbour's footprint.
        // Real data needs this: DarthNox's small and powerful ion thrusters put a 3-cell model in 7-cell bounds with
        // placement −2.5, half a cell below the bounds (the 2×7×2 ion thruster's −2.5, copied over; −2 would fit).
        var fittedModel = Vector3.Min(model, cells.ToVector3());
        if (fittedModel != model) problems.Add($"model size {model} is larger than the bounds {cells}; clamped");
        var bounds = cells.ToVector3();
        var center = bounds * 0.5f + placement;
        var half = fittedModel * 0.5f;
        var fittedCenter = Vector3.Clamp(center, half, bounds - half);
        if (Vector3.Distance(fittedCenter, center) > 1e-4f)
            problems.Add($"model placement {placement} puts the model outside the bounds; moved by {fittedCenter - center}");

        return new BlockShape(cells, fittedModel, fittedCenter - bounds * 0.5f, faces, inactive, problems);
    }

    public FaceSet FacesFor(bool active) => active ? Faces : InactiveFaces;

    // ---- placement in the grid ----

    /// <summary>Bounds after turning; always positive.</summary>
    public Int3 RotatedSize(Orientation o) => o.Apply(Size).Abs();

    /// <summary>Local point → world point for a block at <paramref name="origin"/>; see the class remarks.</summary>
    public Vector3 ToWorld(Vector3 local, Int3 origin, Orientation o) => origin + o.Apply(local) - MinCorner(o);

    /// <summary>World point → local point; the inverse of <see cref="ToWorld"/>.</summary>
    public Vector3 ToLocal(Vector3 world, Int3 origin, Orientation o) => o.Inverse.Apply(world - origin + MinCorner(o));

    /// <summary>Minimum corner of the turned bounds before they are moved to the origin.</summary>
    Vector3 MinCorner(Orientation o) => Int3.Min(Int3.Zero, o.Apply(Size)).ToVector3();

    /// <summary>Every world cell of the placement bounds; the minimum one is <paramref name="origin"/>.</summary>
    public IEnumerable<Int3> Footprint(Int3 origin, Orientation o) => CellsOf(origin, RotatedSize(o));

    /// <summary>
    /// World-space box of the visible model. <paramref name="localOffset"/> moves the model in LOCAL space before it is
    /// turned (door travel: <see cref="DoorMotion.Offset"/>), so a door slides along its own up axis however it is placed.
    /// </summary>
    public Aabb ModelBox(Int3 origin, Orientation o, Vector3 localOffset = default)
    {
        var center = ModelCenter + localOffset;
        var half = ModelSize * 0.5f;
        var a = ToWorld(center - half, origin, o);
        var b = ToWorld(center + half, origin, o);
        return new Aabb(Vector3.Min(a, b), Vector3.Max(a, b));
    }

    /// <summary>
    /// The cells the model box covers: what is solid for collision and ray casts. A box edge within
    /// <see cref="CellEpsilon"/> of a cell boundary does not spill into the next cell, so a model that exactly fills
    /// whole cells (all real data) yields exactly those cells despite float rounding.
    /// </summary>
    public IEnumerable<Int3> ModelCells(Int3 origin, Orientation o, Vector3 localOffset = default)
    {
        var box = ModelBox(origin, o, localOffset);
        var lo = Int3.Floor(box.Min + new Vector3(CellEpsilon));
        var hi = Int3.Floor(box.Max - new Vector3(CellEpsilon));
        // A model thinner than two epsilons still occupies the cell its centre is in.
        var center = Int3.Floor(box.Center);
        lo = new Int3(lo.X > hi.X ? center.X : lo.X, lo.Y > hi.Y ? center.Y : lo.Y, lo.Z > hi.Z ? center.Z : lo.Z);
        hi = Int3.Max(lo, hi);
        return CellsOf(lo, hi - lo + Int3.One);
    }

    /// <summary>
    /// Where to put the origin when the player clicks a block face and the new block should start at
    /// <paramref name="placeCell"/> (the cell in front of that face).
    /// </summary>
    /// <param name="faceNormal">Outward normal of the clicked face, pointing from the clicked block into placeCell.</param>
    /// <remarks>
    /// The rule (ours): the footprint contains <paramref name="placeCell"/>; along the normal it starts at placeCell and
    /// grows away from the clicked face, so it never reaches into the block it is placed against; across the face it is
    /// centred on placeCell. With an even rotated size there is no middle cell, so the extra cell goes to the positive
    /// side: <c>origin = placeCell − (size − 1) / 2</c> with integer division, so size 2 covers placeCell and +1,
    /// size 3 covers −1..+1, size 4 covers −1..+2.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="faceNormal"/> is not a unit axis vector.</exception>
    public Int3 AnchorFor(Int3 placeCell, Int3 faceNormal, Orientation o)
    {
        if (AxisDirections.FromInt3(faceNormal) is null)
            throw new ArgumentException($"Face normal {faceNormal} is not a unit axis vector.", nameof(faceNormal));
        var size = RotatedSize(o);
        int Axis(int axis)
        {
            int normal = faceNormal[axis], cell = placeCell[axis], extent = size[axis];
            if (normal > 0) return cell;
            if (normal < 0) return cell - extent + 1;
            return cell - (extent - 1) / 2;
        }
        return new Int3(Axis(0), Axis(1), Axis(2));
    }

    static IEnumerable<Int3> CellsOf(Int3 min, Int3 size)
    {
        for (int z = 0; z < size.Z; z++)
            for (int y = 0; y < size.Y; y++)
                for (int x = 0; x < size.X; x++)
                    yield return new Int3(min.X + x, min.Y + y, min.Z + z);
    }
}
