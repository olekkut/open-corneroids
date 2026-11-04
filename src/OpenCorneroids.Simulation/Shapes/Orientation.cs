using System.Numerics;

namespace OpenCorneroids.Simulation.Shapes;

/// <summary>One of the six axis directions. Declared +Y first so <c>default</c> is "up".</summary>
public enum AxisDirection : byte { PosY, NegY, PosX, NegX, PosZ, NegZ }

public static class AxisDirections
{
    public static Int3 ToInt3(this AxisDirection d) => d switch
    {
        AxisDirection.PosY => Int3.UnitY,
        AxisDirection.NegY => -Int3.UnitY,
        AxisDirection.PosX => Int3.UnitX,
        AxisDirection.NegX => -Int3.UnitX,
        AxisDirection.PosZ => Int3.UnitZ,
        AxisDirection.NegZ => -Int3.UnitZ,
        _ => throw new ArgumentOutOfRangeException(nameof(d), d, null),
    };

    /// <summary>The direction of a unit axis vector, or null for anything else (zero, diagonal, longer).</summary>
    public static AxisDirection? FromInt3(Int3 v) => v switch
    {
        (0, 1, 0) => AxisDirection.PosY,
        (0, -1, 0) => AxisDirection.NegY,
        (1, 0, 0) => AxisDirection.PosX,
        (-1, 0, 0) => AxisDirection.NegX,
        (0, 0, 1) => AxisDirection.PosZ,
        (0, 0, -1) => AxisDirection.NegZ,
        _ => null,
    };
}

/// <summary>
/// How a placed block is turned: one of the 24 proper rotations of a cube (no mirroring, so textures never flip).
/// Our own scheme; how the original game oriented blocks is unknown.
/// </summary>
/// <remarks>
/// <para>
/// Described as <see cref="Up"/> (the world direction the block's local +Y points to) and <see cref="Spin"/> (quarter
/// turns about that up axis). The rotation is "tip the block over so +Y points to <see cref="Up"/>, then spin it".
/// The tipping is the shortest one: about X for ±Z and −Y (−Y turns 180° about X), about Z for ±X. A spin turns
/// counter-clockwise when looking down the up axis at the block (right-hand rule about <see cref="Up"/>).
/// </para>
/// <para>
/// <see cref="Index"/> = up × 4 + spin, so 0..23 round-trips through saves and <c>default</c> is
/// <see cref="Default"/> (up +Y, spin 0), the identity.
/// </para>
/// </remarks>
public readonly record struct Orientation
{
    public const int Count = 24;

    readonly byte _index;

    Orientation(int index) => _index = (byte)index;

    public static Orientation Default => default;

    /// <summary>All 24, in index order.</summary>
    public static IReadOnlyList<Orientation> All { get; } = [.. Enumerable.Range(0, Count).Select(i => new Orientation(i))];

    public int Index => _index;
    public AxisDirection Up => (AxisDirection)(_index / 4);
    public int Spin => _index % 4;

    public static Orientation FromIndex(int index) => IsValidIndex(index)
        ? new Orientation(index)
        : throw new ArgumentOutOfRangeException(nameof(index), index, $"Orientation index must be 0..{Count - 1}.");

    public static bool IsValidIndex(int index) => index is >= 0 and < Count;

    /// <param name="spin">Any integer; taken modulo 4, so −1 is three quarter turns.</param>
    public static Orientation From(AxisDirection up, int spin)
    {
        if ((int)up > (int)AxisDirection.NegZ) throw new ArgumentOutOfRangeException(nameof(up), up, null);
        return new Orientation((int)up * 4 + ((spin % 4) + 4) % 4);
    }

    /// <summary>A quarter turn further about the same up axis.</summary>
    public Orientation NextSpin() => From(Up, Spin + 1);

    /// <summary>The next up direction in <see cref="AxisDirection"/> order (wrapping), keeping the spin count.</summary>
    public Orientation NextUp() => From((AxisDirection)(((int)Up + 1) % 6), Spin);

    // ---- matrix ----

    /// <summary>Element of the rotation matrix; world = M · local with vectors as columns.</summary>
    public int this[int row, int column] => Matrices[_index][row * 3 + column];

    /// <summary>A copy of the 3×3 rotation matrix, [row, column].</summary>
    public int[,] ToMatrix()
    {
        var m = new int[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++) m[r, c] = this[r, c];
        return m;
    }

    /// <summary>Where the local axis <paramref name="axis"/> (0 = X, 1 = Y, 2 = Z) points in the world.</summary>
    public Int3 Column(int axis) => new(this[0, axis], this[1, axis], this[2, axis]);

    public Int3 Apply(Int3 v)
    {
        var m = Matrices[_index];
        return new(m[0] * v.X + m[1] * v.Y + m[2] * v.Z, m[3] * v.X + m[4] * v.Y + m[5] * v.Z, m[6] * v.X + m[7] * v.Y + m[8] * v.Z);
    }

    public Vector3 Apply(Vector3 v)
    {
        var m = Matrices[_index];
        return new(m[0] * v.X + m[1] * v.Y + m[2] * v.Z, m[3] * v.X + m[4] * v.Y + m[5] * v.Z, m[6] * v.X + m[7] * v.Y + m[8] * v.Z);
    }

    /// <summary>The rotation that undoes this one.</summary>
    public Orientation Inverse => new(InverseTable[_index]);

    /// <summary>This rotation followed by <paramref name="next"/>: <c>a.Then(b).Apply(v) == b.Apply(a.Apply(v))</c>.</summary>
    public Orientation Then(Orientation next) => new(ComposeTable[next._index * Count + _index]);

    public override string ToString() => $"up {Up} spin {Spin}";

    // ---- tables, built once ----

    static readonly int[][] Matrices = BuildMatrices();
    static readonly byte[] InverseTable = BuildInverse();
    static readonly byte[] ComposeTable = BuildCompose();

    static int[][] BuildMatrices()
    {
        var result = new int[Count][];
        for (int up = 0; up < 6; up++)
        {
            var tip = Tip((AxisDirection)up);
            var axis = ((AxisDirection)up).ToInt3();
            var quarter = QuarterTurn(axis);
            var m = tip;
            for (int spin = 0; spin < 4; spin++)
            {
                result[up * 4 + spin] = m;
                m = Multiply(quarter, m);
            }
        }
        return result;
    }

    /// <summary>Columns are the images of X, Y, Z; see the remarks on <see cref="Orientation"/>.</summary>
    static int[] Tip(AxisDirection up) => up switch
    {
        AxisDirection.PosY => FromColumns(Int3.UnitX, Int3.UnitY, Int3.UnitZ),
        AxisDirection.NegY => FromColumns(Int3.UnitX, -Int3.UnitY, -Int3.UnitZ),
        AxisDirection.PosX => FromColumns(-Int3.UnitY, Int3.UnitX, Int3.UnitZ),
        AxisDirection.NegX => FromColumns(Int3.UnitY, -Int3.UnitX, Int3.UnitZ),
        AxisDirection.PosZ => FromColumns(Int3.UnitX, Int3.UnitZ, -Int3.UnitY),
        AxisDirection.NegZ => FromColumns(Int3.UnitX, -Int3.UnitZ, Int3.UnitY),
        _ => throw new ArgumentOutOfRangeException(nameof(up)),
    };

    /// <summary>Rodrigues' formula at 90°, which stays integral: v ↦ u×v + u(u·v).</summary>
    static int[] QuarterTurn(Int3 u)
    {
        Int3 Turn(Int3 v) => Int3.Cross(u, v) + u * Int3.Dot(u, v);
        return FromColumns(Turn(Int3.UnitX), Turn(Int3.UnitY), Turn(Int3.UnitZ));
    }

    static int[] FromColumns(Int3 x, Int3 y, Int3 z) => [x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z];

    static int[] Multiply(int[] a, int[] b)
    {
        var m = new int[9];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                m[r * 3 + c] = a[r * 3] * b[c] + a[r * 3 + 1] * b[3 + c] + a[r * 3 + 2] * b[6 + c];
        return m;
    }

    static int Find(int[] m)
    {
        for (int i = 0; i < Count; i++)
            if (Matrices[i].AsSpan().SequenceEqual(m)) return i;
        throw new InvalidOperationException("Not a rotation of the cube.");
    }

    static byte[] BuildInverse()
    {
        var table = new byte[Count];
        for (int i = 0; i < Count; i++)
        {
            var m = Matrices[i];
            table[i] = (byte)Find([m[0], m[3], m[6], m[1], m[4], m[7], m[2], m[5], m[8]]);   // transpose
        }
        return table;
    }

    /// <summary>[second * Count + first] = second ∘ first.</summary>
    static byte[] BuildCompose()
    {
        var table = new byte[Count * Count];
        for (int second = 0; second < Count; second++)
            for (int first = 0; first < Count; first++)
                table[second * Count + first] = (byte)Find(Multiply(Matrices[second], Matrices[first]));
        return table;
    }
}
