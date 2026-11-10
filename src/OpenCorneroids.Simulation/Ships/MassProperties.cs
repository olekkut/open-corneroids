using System.Numerics;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// Mass, centre of mass and inertia tensor of a rigid body made of solid boxes (a ship's blocks), in the body's local
/// frame (the ship's block grid). Our own model; how the original game weighed ships is unknown.
/// </summary>
/// <remarks>
/// Tensors are 3×3 symmetric matrices stored in the upper-left of a <see cref="Matrix4x4"/> (M11..M33), with
/// <c>M44 = 1</c> and the rest zero. Being symmetric, they read the same in row- and column-vector conventions; use
/// <see cref="Multiply"/> to apply one.
/// </remarks>
public sealed class MassProperties
{
    /// <summary>
    /// Below this total mass the body counts as massless: forces do nothing and every inverse is zero. Tunable.
    /// </summary>
    public const float MinMass = 1e-6f;

    /// <summary>
    /// Principal moments smaller than this fraction of the largest one are treated as zero when inverting, so a
    /// degenerate body (a zero-thickness rod or plate) gets a pseudo-inverse instead of infinities. Tunable.
    /// </summary>
    public const float SingularRatio = 1e-6f;

    public static MassProperties Empty { get; } = new(0, Vector3.Zero, Zero3(), Zero3());

    /// <summary>Total mass; 0 when the body is massless.</summary>
    public float Mass { get; }
    /// <summary>1 / <see cref="Mass"/>, or 0 for a massless body.</summary>
    public float InverseMass { get; }
    /// <summary>Centre of mass in local coordinates; the origin for a massless body.</summary>
    public Vector3 CenterOfMass { get; }
    /// <summary>Inertia tensor about <see cref="CenterOfMass"/>, local axes.</summary>
    public Matrix4x4 Inertia { get; }
    /// <summary>
    /// Inverse of <see cref="Inertia"/>; along a principal axis with (near-)zero moment it is zero (pseudo-inverse),
    /// so torque about that axis does nothing rather than producing infinite spin.
    /// </summary>
    public Matrix4x4 InverseInertia { get; }

    public bool IsMassless => Mass <= 0;

    MassProperties(float mass, Vector3 com, Matrix4x4 inertia, Matrix4x4 inverseInertia)
    {
        Mass = mass;
        InverseMass = mass > 0 ? 1f / mass : 0f;
        CenterOfMass = com;
        Inertia = inertia;
        InverseInertia = inverseInertia;
    }

    /// <summary>
    /// Sums solid boxes of uniform density. Each box contributes <c>m/12 · (b² + c²)</c> about its own centre on each
    /// axis, moved to the common centre of mass with the parallel-axis theorem. Boxes with a non-positive or
    /// non-finite mass, or a non-finite centre, are skipped; a negative or non-finite size counts as zero (a point
    /// mass). Accumulates in double so a big ship far from the origin keeps its precision.
    /// </summary>
    public static MassProperties From(IEnumerable<(Vector3 center, Vector3 size, float mass)> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var list = new List<(double X, double Y, double Z, double Sx, double Sy, double Sz, double M)>();
        double total = 0, cx = 0, cy = 0, cz = 0;
        foreach (var (center, size, mass) in boxes)
        {
            if (!Counts(center, mass)) continue;
            list.Add((center.X, center.Y, center.Z, Edge(size.X), Edge(size.Y), Edge(size.Z), mass));
            total += mass;
            cx += mass * center.X;
            cy += mass * center.Y;
            cz += mass * center.Z;
        }
        if (total < MinMass) return Empty;
        cx /= total;
        cy /= total;
        cz /= total;

        // Second pass about the centre of mass, so no large terms cancel.
        double ixx = 0, iyy = 0, izz = 0, ixy = 0, ixz = 0, iyz = 0;
        foreach (var b in list)
        {
            double dx = b.X - cx, dy = b.Y - cy, dz = b.Z - cz;
            double own = b.M / 12.0;
            ixx += own * (b.Sy * b.Sy + b.Sz * b.Sz) + b.M * (dy * dy + dz * dz);
            iyy += own * (b.Sx * b.Sx + b.Sz * b.Sz) + b.M * (dx * dx + dz * dz);
            izz += own * (b.Sx * b.Sx + b.Sy * b.Sy) + b.M * (dx * dx + dy * dy);
            ixy -= b.M * dx * dy;
            ixz -= b.M * dx * dz;
            iyz -= b.M * dy * dz;
        }
        return FromTensor(total, cx, cy, cz, ixx, iyy, izz, ixy, ixz, iyz);
    }

    /// <summary>The edge length a box size counts with: a negative or non-finite edge is zero (a point mass).</summary>
    internal static double Edge(float e) => float.IsFinite(e) && e > 0 ? e : 0;

    /// <summary>Whether <see cref="From"/> counts a box at all: a positive finite mass at a finite centre.</summary>
    internal static bool Counts(Vector3 center, float mass) =>
        float.IsFinite(mass) && mass > 0 && float.IsFinite(center.X) && float.IsFinite(center.Y) && float.IsFinite(center.Z);

    /// <summary>The body for a total mass, centre of mass and inertia tensor about that centre (summed in double).</summary>
    internal static MassProperties FromTensor(double total, double cx, double cy, double cz,
        double ixx, double iyy, double izz, double ixy, double ixz, double iyz)
    {
        if (total < MinMass) return Empty;
        double[,] inertia = { { ixx, ixy, ixz }, { ixy, iyy, iyz }, { ixz, iyz, izz } };
        return new MassProperties((float)total, new Vector3((float)cx, (float)cy, (float)cz), ToMatrix(inertia),
            ToMatrix(PseudoInverse(inertia)));
    }

    /// <summary>Applies a 3×3 tensor stored as described in the remarks to <paramref name="v"/>.</summary>
    public static Vector3 Multiply(in Matrix4x4 m, Vector3 v) => new(
        m.M11 * v.X + m.M12 * v.Y + m.M13 * v.Z,
        m.M21 * v.X + m.M22 * v.Y + m.M23 * v.Z,
        m.M31 * v.X + m.M32 * v.Y + m.M33 * v.Z);

    /// <summary>Inertia about the centre of mass with world axes, for a body turned by <paramref name="orientation"/>.</summary>
    public Vector3 ApplyWorldInertia(Quaternion orientation, Vector3 worldVector) =>
        Vector3.Transform(Multiply(Inertia, Vector3.Transform(worldVector, Quaternion.Conjugate(orientation))), orientation);

    /// <summary>Inverse inertia with world axes: angular acceleration produced by a world torque.</summary>
    public Vector3 ApplyWorldInverseInertia(Quaternion orientation, Vector3 worldVector) =>
        Vector3.Transform(Multiply(InverseInertia, Vector3.Transform(worldVector, Quaternion.Conjugate(orientation))), orientation);

    // ---- 3×3 helpers ----

    static Matrix4x4 Zero3() => new() { M44 = 1 };

    static Matrix4x4 ToMatrix(double[,] a) => new(
        (float)a[0, 0], (float)a[0, 1], (float)a[0, 2], 0,
        (float)a[1, 0], (float)a[1, 1], (float)a[1, 2], 0,
        (float)a[2, 0], (float)a[2, 1], (float)a[2, 2], 0,
        0, 0, 0, 1);

    /// <summary>
    /// Eigen-decomposes the symmetric matrix (cyclic Jacobi, fixed sweep cap) and inverts the eigenvalues that are
    /// not negligible, leaving the others at zero.
    /// </summary>
    static double[,] PseudoInverse(double[,] input)
    {
        var a = (double[,])input.Clone();
        var v = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 32; sweep++)
        {
            double off = a[0, 1] * a[0, 1] + a[0, 2] * a[0, 2] + a[1, 2] * a[1, 2];
            double diag = a[0, 0] * a[0, 0] + a[1, 1] * a[1, 1] + a[2, 2] * a[2, 2];
            if (off <= 1e-30 * Math.Max(diag, 1e-300)) break;
            for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                    Rotate(a, v, p, q);
        }

        double largest = Math.Max(Math.Abs(a[0, 0]), Math.Max(Math.Abs(a[1, 1]), Math.Abs(a[2, 2])));
        var inverse = new double[3, 3];
        if (largest <= 0) return inverse;
        for (int k = 0; k < 3; k++)
        {
            double lambda = a[k, k];
            if (lambda <= largest * SingularRatio) continue;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    inverse[i, j] += v[i, k] * v[j, k] / lambda;
        }
        return inverse;
    }

    /// <summary>One Jacobi rotation zeroing a[p,q]; accumulates the eigenvectors as columns of v.</summary>
    static void Rotate(double[,] a, double[,] v, int p, int q)
    {
        double apq = a[p, q];
        if (Math.Abs(apq) < 1e-300) return;
        double theta = (a[q, q] - a[p, p]) / (2 * apq);
        double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
        double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
        for (int k = 0; k < 3; k++)
        {
            double akp = a[k, p], akq = a[k, q];
            a[k, p] = c * akp - s * akq;
            a[k, q] = s * akp + c * akq;
        }
        for (int k = 0; k < 3; k++)
        {
            double apk = a[p, k], aqk = a[q, k];
            a[p, k] = c * apk - s * aqk;
            a[q, k] = s * apk + c * aqk;
        }
        for (int k = 0; k < 3; k++)
        {
            double vkp = v[k, p], vkq = v[k, q];
            v[k, p] = c * vkp - s * vkq;
            v[k, q] = s * vkp + c * vkq;
        }
    }
}
