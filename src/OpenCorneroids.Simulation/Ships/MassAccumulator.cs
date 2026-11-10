using System.Numerics;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// Running sums from which <see cref="MassProperties"/> follow without walking every block again: total mass, first
/// moments, second moments about the grid origin and each box's own inertia are all additive, so placing or breaking
/// a block is one <see cref="Add"/> or <see cref="Remove"/>. Gives the same body as <see cref="MassProperties.From"/>
/// over the same boxes, to rounding. Ours.
/// </summary>
/// <remarks>
/// Sums are kept in double. Removing exactly what was added leaves at most rounding behind, and when the last box goes
/// every sum is reset to exactly zero, so no drift outlives an emptied grid.
/// </remarks>
public sealed class MassAccumulator
{
    double _mass, _mx, _my, _mz;
    double _mxx, _myy, _mzz, _mxy, _mxz, _myz;
    double _ownXx, _ownYy, _ownZz;

    /// <summary>Boxes counted now (those <see cref="MassProperties.From"/> would not skip).</summary>
    public int Boxes { get; private set; }

    /// <summary>Total mass now.</summary>
    public double TotalMass => _mass;

    /// <summary>Counts a solid box of uniform density; skipped by the same rules as <see cref="MassProperties.From"/>.</summary>
    public void Add(Vector3 center, Vector3 size, float mass) => Apply(center, size, mass, 1);

    /// <summary>Takes back a box added earlier with the same values.</summary>
    public void Remove(Vector3 center, Vector3 size, float mass) => Apply(center, size, mass, -1);

    /// <summary>Forgets every box.</summary>
    public void Clear()
    {
        _mass = _mx = _my = _mz = 0;
        _mxx = _myy = _mzz = _mxy = _mxz = _myz = 0;
        _ownXx = _ownYy = _ownZz = 0;
        Boxes = 0;
    }

    void Apply(Vector3 center, Vector3 size, float mass, int sign)
    {
        if (!MassProperties.Counts(center, mass)) return;
        Boxes += sign;
        if (Boxes <= 0)
        {
            Clear();
            return;
        }
        double m = sign * (double)mass;
        double x = center.X, y = center.Y, z = center.Z;
        double sx = MassProperties.Edge(size.X), sy = MassProperties.Edge(size.Y), sz = MassProperties.Edge(size.Z);
        _mass += m;
        _mx += m * x;
        _my += m * y;
        _mz += m * z;
        _mxx += m * x * x;
        _myy += m * y * y;
        _mzz += m * z * z;
        _mxy += m * x * y;
        _mxz += m * x * z;
        _myz += m * y * z;
        double own = m / 12.0;
        _ownXx += own * (sy * sy + sz * sz);
        _ownYy += own * (sx * sx + sz * sz);
        _ownZz += own * (sx * sx + sy * sy);
    }

    /// <summary>
    /// The body the counted boxes make: centre of mass from the first moments, inertia about it from the second moments
    /// moved by the parallel-axis theorem.
    /// </summary>
    public MassProperties ToProperties()
    {
        if (Boxes == 0 || _mass < MassProperties.MinMass) return MassProperties.Empty;
        double cx = _mx / _mass, cy = _my / _mass, cz = _mz / _mass;
        // Second moments about the centre of mass: Σm·x² − M·cx² and so on.
        double xx = _mxx - _mass * cx * cx, yy = _myy - _mass * cy * cy, zz = _mzz - _mass * cz * cz;
        double xy = _mxy - _mass * cx * cy, xz = _mxz - _mass * cx * cz, yz = _myz - _mass * cy * cz;
        return MassProperties.FromTensor(_mass, cx, cy, cz,
            _ownXx + yy + zz, _ownYy + xx + zz, _ownZz + xx + yy, -xy, -xz, -yz);
    }
}
