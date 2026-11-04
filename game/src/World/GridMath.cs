using Godot;
using OpenCorneroids.Simulation.Shapes;
using Num = System.Numerics;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Conversions between the Godot-free Simulation types and Godot's. The shapes library works in
/// <see cref="Int3"/> and System.Numerics so it can be tested without the engine; the game converts at its edge.
/// </summary>
public static class GridMath
{
    public static Int3 ToInt3(this Vector3I v) => new(v.X, v.Y, v.Z);
    public static Vector3I ToGodot(this Int3 v) => new(v.X, v.Y, v.Z);
    public static Vector3 ToGodot(this Num.Vector3 v) => new(v.X, v.Y, v.Z);
    public static Num.Vector3 ToNumerics(this Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>"+Y", "-X"...: how the HUD names an up axis.</summary>
    public static string Name(this AxisDirection d) => d switch
    {
        AxisDirection.PosY => "+Y",
        AxisDirection.NegY => "-Y",
        AxisDirection.PosX => "+X",
        AxisDirection.NegX => "-X",
        AxisDirection.PosZ => "+Z",
        _ => "-Z",
    };

    /// <summary>Orientation as the HUD shows it, e.g. "#5 up +Y spin 1".</summary>
    public static string Describe(this OpenCorneroids.Simulation.Shapes.Orientation o) => $"#{o.Index} up {o.Up.Name()} spin {o.Spin}";
}
