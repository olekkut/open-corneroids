namespace OpenCorneroids.Game.World;

/// <summary>
/// The static world: the asteroids and everything built onto them, in one 128³ grid whose node sits at the world origin
/// unturned, so its local cells are world cells. Ships are separate grids (<see cref="ShipGrid"/>).
/// </summary>
public partial class VoxelWorld : BlockGrid
{
    public const int Size = 128;

    public VoxelWorld() : base(Size) { }

    protected override string OutsideMessage => "No room: that would reach outside the world";

    /// <summary>Inside the world's cells. The world grid is not moved, so this works on world positions too.</summary>
    public static bool InBounds(Godot.Vector3I p) =>
        p.X >= 0 && p.Y >= 0 && p.Z >= 0 && p.X < Size && p.Y < Size && p.Z < Size;
}
