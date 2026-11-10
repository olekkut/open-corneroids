using System.Runtime.CompilerServices;
using Godot;
using OpenCorneroids.Simulation.Physics;
using Num = System.Numerics;

namespace OpenCorneroids.Game.World;

/// <summary>The grids as the Godot-free collision rules see them (<see cref="IGridShape"/>): the player's body
/// (<see cref="RiderCollision"/>) and thrown torches (<see cref="GridSweep"/>, through
/// <see cref="Simulation.Items.Torches.Collide"/> with the shapes from <see cref="CaptureShapes"/>) move through every
/// grid with them.</summary>
public sealed partial class GridSet
{
    /// <summary>One <see cref="IGridShape"/> per grid, kept while the grid lives.</summary>
    static readonly ConditionalWeakTable<BlockGrid, GridShape> ShapeOf = new();

    /// <summary>The grid's shape, its pose read now.</summary>
    public static IGridShape Shape(BlockGrid grid)
    {
        var shape = ShapeOf.GetValue(grid, static g => new GridShape(g));
        shape.Capture();
        return shape;
    }

    /// <summary>Fills <paramref name="ships"/> with every ship's shape, where they are now, and returns the static
    /// world's. Read once per step: the many look-ups of one step then don't each ask Godot for a transform.</summary>
    public IGridShape CaptureShapes(List<IGridShape> ships)
    {
        ArgumentNullException.ThrowIfNull(ships);
        ships.Clear();
        for (int i = 0; i < _ships.Count; i++) ships.Add(Shape(_ships[i]));
        return Shape(World);
    }

    /// <summary>A grid as the collision rules see it: its pose, cells, extent and motion, the pose taken when
    /// <see cref="Capture"/> is called.</summary>
    sealed class GridShape(BlockGrid grid) : IGridShape
    {
        public Num.Matrix4x4 ToWorld { get; private set; }
        public Num.Matrix4x4 ToLocal { get; private set; }
        public Func<int, int, int, bool> Solid => grid.SolidQuery;
        public (Num.Vector3 Center, float Radius) Extent { get; private set; }

        public Num.Vector3 PointVelocity(Num.Vector3 world) =>
            grid is ShipGrid ship ? ship.PointVelocity(world.ToGodot()).ToNumerics() : Num.Vector3.Zero;

        public void Capture()
        {
            var pose = grid.GlobalTransform;
            ToWorld = ToMatrix(pose);
            ToLocal = ToMatrix(pose.AffineInverse());
            if (grid is ShipGrid ship)
            {
                var (center, radius) = ship.WorldExtent;
                Extent = (center.ToNumerics(), radius);
            }
            else Extent = (Num.Vector3.Zero, float.PositiveInfinity);
        }

        /// <summary>Godot's column basis and origin as a row-vector matrix (<c>Vector3.Transform(v, m)</c> = pose * v).</summary>
        static Num.Matrix4x4 ToMatrix(Transform3D t) => new(
            t.Basis.X.X, t.Basis.X.Y, t.Basis.X.Z, 0,
            t.Basis.Y.X, t.Basis.Y.Y, t.Basis.Y.Z, 0,
            t.Basis.Z.X, t.Basis.Z.Y, t.Basis.Z.Z, 0,
            t.Origin.X, t.Origin.Y, t.Origin.Z, 1);
    }
}
