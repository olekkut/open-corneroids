using Godot;
using OpenCorneroids.Simulation.Grids;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Ships;
using Num = System.Numerics;

namespace OpenCorneroids.Game.World;

/// <summary>
/// A ship: a block grid of its own that is not part of the static world. Its node's transform places the local grid in
/// the world (world-aligned when launched, so every block keeps its world position); the blocks are edited in local
/// cells, so building on a turned ship follows the ship's own axes. <see cref="ShipPhysics"/> moves the node: the
/// node's transform and the two velocities are the ship's whole motion state, and the rigid body (centred on the centre
/// of mass) is derived from them each step, so building on a ship, which moves its centre of mass, never moves the ship.
/// </summary>
public partial class ShipGrid : BlockGrid
{
    MassProperties? _mass;
    int _massRevision = -1;
    /// <summary>Mass sums kept up to date by every edit, so the mass after an edit needs no walk over the blocks.</summary>
    readonly MassAccumulator _massSums = new();
    (Vector3 Center, float Radius) _extent;
    int _extentRevision = -1;
    readonly SurfaceSampler _surface;

    public ShipGrid() : this(Launch.ShipGridSize) { }

    /// <param name="size">Edge of the local grid; a positive multiple of <see cref="BlockGrid.ChunkSize"/>.</param>
    public ShipGrid(int size) : base(size) => _surface = new SurfaceSampler(CellStore);

    protected override string OutsideMessage => "No room: that would reach outside the ship's grid";

    Transform3D _stepPose, _previousStepPose, _shownPose;
    bool _posesValid;
    Vector3 _massCenter;

    /// <summary>Whether this floating grid is a static natural asteroid rather than a ship.</summary>
    public bool IsAsteroid { get; set; }

    /// <summary>Velocity of the centre of mass, world space, units per second. Saved.</summary>
    public Vector3 LinearVelocity { get; set; }

    /// <summary>World axes, radians per second, about the centre of mass. Saved.</summary>
    public Vector3 AngularVelocity { get; set; }

    /// <summary>The ship as a rigid body: the centre of mass where the node's transform puts it, and the velocities.</summary>
    public RigidBodyState BodyState()
    {
        var basis = Transform.Basis.Orthonormalized();
        return new RigidBodyState((Transform * Mass.CenterOfMass.ToGodot()).ToNumerics(), basis.GetRotationQuaternion().Normalized().ToNumerics(),
            LinearVelocity.ToNumerics(), AngularVelocity.ToNumerics());
    }

    /// <summary>Moves the node so the centre of mass lands where <paramref name="state"/> has it, turned as it says, and
    /// takes its velocities.</summary>
    public void SetBodyState(RigidBodyState state)
    {
        Transform = PoseFor(state.Position.ToGodot(), state.Orientation.ToGodot());
        LinearVelocity = state.LinearVelocity.ToGodot();
        AngularVelocity = state.AngularVelocity.ToGodot();
    }

    /// <summary>The node transform that puts the centre of mass at world <paramref name="centerOfMass"/>, turned by
    /// <paramref name="rotation"/>.</summary>
    public Transform3D PoseFor(Vector3 centerOfMass, Quaternion rotation)
    {
        var basis = new Basis(rotation.Normalized());
        return new Transform3D(basis, centerOfMass - basis * Mass.CenterOfMass.ToGodot());
    }

    /// <summary>
    /// The pose a fraction <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>: the
    /// centre of mass moved in a straight line and the rotation slerped about it, as the integrator moves it within a
    /// step.
    /// </summary>
    public Transform3D Interpolate(Transform3D from, Transform3D to, float t)
    {
        var com = Mass.CenterOfMass.ToGodot();
        var a = from.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
        var b = to.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
        return PoseFor((from * com).Lerp(to * com, t), a.Slerp(b, t));
    }

    /// <summary>
    /// Start of a frame's physics: the node goes back to the last step's pose (it showed a blend, see
    /// <see cref="ShowBetweenSteps"/>). A node moved from outside since (launched, loaded, turned by a hook) starts over
    /// from where it was put.
    /// </summary>
    public void BeginSteps()
    {
        if (!_posesValid || Transform != _shownPose)
        {
            _stepPose = _previousStepPose = Transform;
            _posesValid = true;
        }
        Transform = _stepPose;
    }

    /// <summary>After a physics step that started at <paramref name="before"/>; the node is at the step's result.</summary>
    public void EndStep(Transform3D before)
    {
        _previousStepPose = before;
        _stepPose = Transform;
    }

    /// <summary>
    /// End of a frame's physics: the node shows the pose a fraction <paramref name="blend"/> (the time left over, in
    /// steps) of the way from the step before last to the last one, so motion looks smooth whatever the frame rate.
    /// What the player sees, aims at and stands against is this pose; the next frame's steps start again from the last
    /// step's (<see cref="BeginSteps"/>).
    /// </summary>
    public void ShowBetweenSteps(float blend)
    {
        if (!_posesValid) BeginSteps();
        Transform = _previousStepPose == _stepPose ? _stepPose : Interpolate(_previousStepPose, _stepPose, Mathf.Clamp(blend, 0, 1));
        _shownPose = Transform;
    }

    /// <summary>The <see cref="RestKey"/> a resting ship was last found free of overlaps at; see <see cref="ShipPhysics"/>.</summary>
    public (int Revision, int ModelRevision, Transform3D Pose)? RestChecked { get; set; }

    /// <summary>The blocks, the doors and the pose: when any changes, a resting ship is checked for overlap again.</summary>
    public (int Revision, int ModelRevision, Transform3D Pose) RestKey => (Revision, ModelRevision, Transform);

    /// <summary>World velocity of the ship's material at world point <paramref name="world"/>: v + ω × r.</summary>
    public Vector3 PointVelocity(Vector3 world) =>
        LinearVelocity + AngularVelocity.Cross(world - Transform * Mass.CenterOfMass.ToGodot());

    /// <summary>
    /// A sphere around everything built (grid blocks and whole object footprints), local centre and radius: far
    /// tighter than <see cref="BlockGrid.Bounds"/>, which covers the whole grid, so ship-against-ship tests can skip
    /// ships that are not near each other. Recomputed after edits, scanning only the outer chunks of the blocks.
    /// </summary>
    public (Vector3 Center, float Radius) LocalExtent
    {
        get
        {
            if (_extentRevision == Revision) return _extent;
            _extentRevision = Revision;
            var min = new Vector3I(int.MaxValue, int.MaxValue, int.MaxValue);
            var max = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
            if (BlockBounds() is { } blocks) (min, max) = blocks;
            foreach (var obj in Objects)
                foreach (var cell in obj.Footprint) (min, max) = (min.Min(cell), max.Max(cell));
            if (min.X > max.X) return _extent = (Vector3.One * (GridSize * 0.5f), 0);
            var size = (Vector3)(max - min + Vector3I.One);
            return _extent = ((Vector3)min + size * 0.5f, size.Length() * 0.5f + 0.1f);
        }
    }

    /// <summary>World centre and radius of <see cref="LocalExtent"/>.</summary>
    public (Vector3 Center, float Radius) WorldExtent => (Transform * LocalExtent.Center, LocalExtent.Radius);

    /// <summary>
    /// Points that stand for the ship's solid volume in collisions, local: for every solid cell (grid block or object
    /// model cell, doors where they are now) with an open face, its centre and a point just inside each open face
    /// (0.45 of a cell out from the centre). Buried cells add nothing: a body can't reach them without first
    /// touching the surface. After edits and door moves only the chunks around what changed are sampled again
    /// (<see cref="SurfaceSampler"/>).
    /// </summary>
    public IReadOnlyList<Num.Vector3> SurfacePoints => _surface.Points;

    /// <summary>
    /// Mass, centre of mass and inertia in local coordinates, from each block's <c>BlockDef.Mass</c>: a grid block is a
    /// solid unit cube, a multi-block a solid box of its model with the block's whole mass (an itemset gives one mass
    /// per item, whatever its size). Summed as blocks come and go (<see cref="MassAccumulator"/>); the inertia is
    /// worked out from the sums after edits, on first use.
    /// </summary>
    public MassProperties Mass
    {
        get
        {
            if (_mass is null || _massRevision != Revision)
            {
                var previous = _mass;
                _mass = _massSums.ToProperties();
                _massRevision = Revision;
                // LinearVelocity is the centre of mass's: when an edit moves that centre on a turning ship, the hull
                // keeps its motion only if the stored velocity becomes the new centre's, v + ω × Δ (world axes).
                var center = _mass.CenterOfMass.ToGodot();
                if (previous is { IsMassless: false } && !_mass.IsMassless && AngularVelocity != Vector3.Zero)
                    LinearVelocity += AngularVelocity.Cross(Transform.Basis.Orthonormalized() * (center - _massCenter));
                _massCenter = center;
            }
            return _mass;
        }
    }

    /// <summary>Grid blocks plus objects, both counted as they change.</summary>
    public int BlockCount => GridBlockCount + Objects.Count;

    /// <summary>A grid block's mass box: a unit cube at the cell.</summary>
    static Num.Vector3 CellCenter(Vector3I cell) => new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

    protected override void BlockChanged(Vector3I p, ushort old, ushort now)
    {
        if (old != BlockPalette.Air) _massSums.Remove(CellCenter(p), Num.Vector3.One, Palette.Mass(old));
        if (now != BlockPalette.Air) _massSums.Add(CellCenter(p), Num.Vector3.One, Palette.Mass(now));
    }

    /// <summary>An object weighs as a solid box of its model, closed (a door's travel does not move its mass).</summary>
    protected override void ObjectChanged(WorldObject obj, bool added)
    {
        var box = obj.Shape.ModelBox(obj.Origin, obj.Orientation);
        if (added) _massSums.Add(box.Center, box.Size, Palette.Mass(obj.Block));
        else _massSums.Remove(box.Center, box.Size, Palette.Mass(obj.Block));
    }

    protected override void CellsReplaced()
    {
        _massSums.Clear();
        if (Palette is null) return;
        foreach (var (cell, block) in Blocks()) _massSums.Add(CellCenter(cell), Num.Vector3.One, Palette.Mass(block));
        foreach (var obj in Objects) ObjectChanged(obj, added: true);
    }

    /// <summary>The ship as a save stores it.</summary>
    public ShipSnapshot ToSnapshot()
    {
        var (palette, cells) = Export();
        var basis = Transform.Basis.Orthonormalized();
        var q = basis.GetRotationQuaternion().Normalized();
        return new ShipSnapshot(Position.ToNumerics(), new Num.Quaternion(q.X, q.Y, q.Z, q.W), LinearVelocity.ToNumerics(),
            AngularVelocity.ToNumerics(), GridSize, palette, cells)
        {
            Objects = ExportObjects(),
            Turned = ExportTurned(),
            Consoles = ExportConsoles(),
            Containers = ExportContainers(),
            IsAsteroid = IsAsteroid,
            Name = IsAsteroid ? "Asteroid" : Name,
        };
    }

    /// <summary>Puts the node where the save had it and restores its velocities. The cells come separately
    /// (<see cref="BlockGrid.Import"/>, <see cref="BlockGrid.ImportObjects"/>).</summary>
    public void RestoreMotion(ShipSnapshot saved)
    {
        var q = new Quaternion(saved.Rotation.X, saved.Rotation.Y, saved.Rotation.Z, saved.Rotation.W).Normalized();
        Transform = new Transform3D(new Basis(q), saved.Position.ToGodot());
        IsAsteroid = saved.IsAsteroid;
        if (saved.IsAsteroid)
        {
            LinearVelocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
        }
        else
        {
            LinearVelocity = saved.LinearVelocity.ToGodot();
            AngularVelocity = saved.AngularVelocity.ToGodot();
        }
    }

    /// <summary>The smallest ship grid edge (a multiple of <see cref="BlockGrid.ChunkSize"/>) that holds a saved
    /// grid of <paramref name="savedSize"/>, within the format's cap.</summary>
    public static int SizeFor(int savedSize) =>
        Math.Clamp((savedSize + ChunkSize - 1) / ChunkSize * ChunkSize, ChunkSize, WorldSaveFormat.MaxShipSize / ChunkSize * ChunkSize);
}
