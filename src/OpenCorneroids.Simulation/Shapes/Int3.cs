using System.Numerics;

namespace OpenCorneroids.Simulation.Shapes;

/// <summary>
/// An integer grid vector: a cell coordinate, a cell count or an axis direction. Godot-free so shapes, saves and
/// tests can share it; the game converts to <c>Vector3I</c> at its edge.
/// </summary>
public readonly record struct Int3(int X, int Y, int Z)
{
    public static readonly Int3 Zero = new(0, 0, 0);
    public static readonly Int3 One = new(1, 1, 1);
    public static readonly Int3 UnitX = new(1, 0, 0);
    public static readonly Int3 UnitY = new(0, 1, 0);
    public static readonly Int3 UnitZ = new(0, 0, 1);

    /// <summary>Component by axis: 0 = X, 1 = Y, 2 = Z.</summary>
    public int this[int axis] => axis switch
    {
        0 => X,
        1 => Y,
        2 => Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Axis must be 0, 1 or 2."),
    };

    /// <summary>Product of the components; the cell count of a box of this size.</summary>
    public long Volume => (long)X * Y * Z;

    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator -(Int3 a) => new(-a.X, -a.Y, -a.Z);
    public static Int3 operator *(Int3 a, int k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Int3 operator *(int k, Int3 a) => a * k;

    public static int Dot(Int3 a, Int3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Int3 Cross(Int3 a, Int3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static Int3 Min(Int3 a, Int3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Int3 Max(Int3 a, Int3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    public Int3 Abs() => new(Math.Abs(X), Math.Abs(Y), Math.Abs(Z));

    /// <summary>The cell containing <paramref name="v"/>; cell (x,y,z) spans [x,x+1) on each axis.</summary>
    public static Int3 Floor(Vector3 v) => new((int)MathF.Floor(v.X), (int)MathF.Floor(v.Y), (int)MathF.Floor(v.Z));

    public Vector3 ToVector3() => new(X, Y, Z);

    /// <summary>Exact for every coordinate a world can hold (|v| ≤ 2^24), so implicit is safe.</summary>
    public static implicit operator Vector3(Int3 v) => v.ToVector3();

    public override string ToString() => $"({X}, {Y}, {Z})";
}
