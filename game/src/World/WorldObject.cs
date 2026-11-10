using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Shapes;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.World;

/// <summary>
/// One placed block that is not in the chunk grid: a door, thruster, gun, big window... Its footprint (the placement
/// bounds) is reserved against other placements; only its model cells are solid, and for a door those move with
/// <see cref="Door"/>'s travel. A <see cref="BlockGrid"/> owns these and keeps its occupancy maps in step with them.
/// </summary>
public sealed class WorldObject
{
    internal WorldObject(int id, ushort block, BlockShape shape, Int3 origin, Orientation orientation, DoorComponent? door,
        bool opaque, MeshInstance3D? node)
    {
        Id = id;
        Block = block;
        Shape = shape;
        Origin = origin;
        Orientation = orientation;
        Door = door;
        Opaque = opaque;
        Node = node;
        RotatedSize = shape.RotatedSize(orientation);
    }

    /// <summary>Runtime id, unique within one world; never saved (objects are saved by position).</summary>
    public int Id { get; }
    /// <summary>Palette index.</summary>
    public ushort Block { get; }
    public BlockShape Shape { get; }
    /// <summary>Minimum cell of the footprint.</summary>
    public Int3 Origin { get; }
    public Orientation Orientation { get; }
    public Int3 RotatedSize { get; }
    /// <summary>Set for blocks with a <see cref="DoorComponent"/>.</summary>
    public DoorComponent? Door { get; }
    public DoorState DoorState { get; internal set; }
    /// <summary>False when the model's textures have see-through pixels, or for a camera block (see-through as in the
    /// original); such a model never hides a grid face.</summary>
    public bool Opaque { get; }
    /// <summary>The model's own mesh node, built at door offset 0 and moved by its <see cref="BlockGrid"/> as the door
    /// travels; null when the object is drawn as part of its chunk's merged object mesh (<c>cl_batch_objects</c>, every
    /// object but doors).</summary>
    public MeshInstance3D? Node { get; internal set; }

    /// <summary>Drawn with its active (powered) textures; false shows the unpowered ones. Set through
    /// <see cref="BlockGrid.SetInactive"/>.</summary>
    public bool Active { get; internal set; } = true;

    /// <summary>Cells the model covers now: solid for collision, ray casts and mining.</summary>
    internal HashSet<Vector3I> ModelCells { get; set; } = [];
    /// <summary>Model cells the model fills completely (opaque objects only): these hide the grid faces against them.</summary>
    internal HashSet<Vector3I> FullCells { get; set; } = [];

    /// <summary>The model's local displacement from door travel; zero for everything that is not a door.</summary>
    public System.Numerics.Vector3 LocalOffset => Door is { } door ? DoorMotion.Offset(DoorState.Open, door) : default;

    /// <summary>Grid-local box of the visible model, where it is now.</summary>
    public Godot.Aabb ModelBox
    {
        get
        {
            var box = Shape.ModelBox(Origin, Orientation, LocalOffset);
            return new Godot.Aabb(box.Min.ToGodot(), box.Size.ToGodot());
        }
    }

    /// <summary>Every cell of the placement bounds.</summary>
    public IEnumerable<Vector3I> Footprint => Shape.Footprint(Origin, Orientation).Select(c => c.ToGodot());

    public bool FootprintContains(Vector3I p) =>
        p.X >= Origin.X && p.Y >= Origin.Y && p.Z >= Origin.Z &&
        p.X < Origin.X + RotatedSize.X && p.Y < Origin.Y + RotatedSize.Y && p.Z < Origin.Z + RotatedSize.Z;

    /// <summary>
    /// The model cells for a door open fraction, cut to the footprint. <see cref="BlockShape.Build(BlockDef)"/> keeps the
    /// closed model inside the bounds, but a door's travel comes from separate numbers (distance, direction) that a mod
    /// could set past them; clipping keeps one object's solid cells from ever reaching into a neighbour's footprint.
    /// </summary>
    internal HashSet<Vector3I> CellsAt(float open)
    {
        var offset = Door is { } door ? DoorMotion.Offset(open, door) : default;
        return [.. Shape.ModelCells(Origin, Orientation, offset).Select(c => c.ToGodot()).Where(FootprintContains)];
    }

    /// <summary>The cell span (<see cref="BlockShape.CellSpan"/>) the model has where <see cref="ModelCells"/> were last
    /// worked out: while a moving door keeps it, its cells stay the same.</summary>
    internal ModelCellSpan CellSpan { get; set; }

    /// <summary>The model's cell span at door open fraction <paramref name="open"/>.</summary>
    internal ModelCellSpan SpanAt(float open) =>
        Shape.CellSpan(Origin, Orientation, Door is { } door ? DoorMotion.Offset(open, door) : default);

    /// <summary>Of <paramref name="cells"/>, those the model with <paramref name="span"/> covers entirely (a cell
    /// within <see cref="BlockShape.CellEpsilon"/> of full counts).</summary>
    internal HashSet<Vector3I> FullCellsAt(HashSet<Vector3I> cells, ModelCellSpan span)
    {
        var full = new HashSet<Vector3I>();
        if (!Opaque) return full;
        foreach (var c in cells)
            if (span.Fills(c.ToInt3())) full.Add(c);
        return full;
    }
}
