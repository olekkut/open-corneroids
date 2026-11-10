using System.Numerics;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// The pieces of our approximate ship collision: whether sample points of a body are inside a block grid, which way
/// is out, how far back along a step the body was still free, and the velocity change at the contact. Bodies are
/// not swept (see the remarks of each member). As in the original, a contact has no bounce
/// (<c>sv_ship_restitution 0</c>) and does no damage to blocks. The original's contacts also give both bodies spin from
/// the contact point; ours carry none and damp the spin instead (<c>sv_ship_contact_spin_damping</c>, ours).
/// </summary>
public static class ShipContact
{
    /// <summary>Fraction of the closing speed a ship keeps, bounced back, after hitting something: none, as in the
    /// original (<c>sv_ship_restitution</c>).</summary>
    public const float Restitution = 0f;

    /// <summary>Each contact multiplies the ship's spin by this, so a ship scraping along rock settles instead of
    /// spinning on: contacts give no torque of their own. Ours (<c>sv_ship_contact_spin_damping</c>).</summary>
    public const float ContactSpinDamping = 0.5f;

    /// <summary>How far a ship that is already inside something (built into rock, loaded overlapping) is pushed out
    /// per physics step. Tunable.</summary>
    public const float PushOutPerStep = 0.05f;

    /// <summary>Halvings <see cref="LastFree"/> makes: 2⁻⁸ of a step, well under a millimetre at flying speeds.</summary>
    public const int BisectSteps = 8;

    /// <summary>Whether the grid cell holding <paramref name="point"/> (grid-local) is solid.</summary>
    public static bool InSolid(Vector3 point, Func<int, int, int, bool> isSolid)
    {
        ArgumentNullException.ThrowIfNull(isSolid);
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) return false;
        return isSolid((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y), (int)MathF.Floor(point.Z));
    }

    /// <summary>
    /// Which way is out of the grid for points that are inside it (grid-local), as a unit vector in the grid's axes,
    /// or null when nothing says (no point inside, or all of them buried with no open side).
    /// </summary>
    /// <remarks>
    /// Each point inside a solid cell votes for one face of that cell: the face it is nearest to among those whose
    /// neighbour cell is open, since a point that just came in crossed a face it is still close to. A face against
    /// another solid cell can't be the way in. The votes are summed, so a body touching a floor and a wall gets the
    /// diagonal between them. Coarse by design: a cell is the finest detail it knows.
    /// </remarks>
    public static Vector3? Normal(IEnumerable<Vector3> points, Func<int, int, int, bool> isSolid)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(isSolid);
        var sum = Vector3.Zero;
        foreach (var p in points)
        {
            if (!InSolid(p, isSolid)) continue;
            int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
            var d = p - new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
            float best = -1;
            var vote = Vector3.Zero;
            for (int axis = 0; axis < 3; axis++)
            {
                float component = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
                int sign = component >= 0 ? 1 : -1;
                int nx = x + (axis == 0 ? sign : 0), ny = y + (axis == 1 ? sign : 0), nz = z + (axis == 2 ? sign : 0);
                if (isSolid(nx, ny, nz) || MathF.Abs(component) <= best) continue;
                best = MathF.Abs(component);
                vote = new Vector3(axis == 0 ? sign : 0, axis == 1 ? sign : 0, axis == 2 ? sign : 0);
            }
            sum += vote;
        }
        float length = sum.Length();
        return length > 1e-6f ? sum / length : null;
    }

    /// <summary>
    /// The largest fraction t in [0, 1] of a step at which the body is still free, by bisection, given that it was
    /// free at 0 and <paramref name="overlapsAt"/> says whether it overlaps at t. Returns 1 when it does not overlap at
    /// 1 (nothing to undo) and 0 when it is not free at 0 either.
    /// </summary>
    /// <param name="steps">Halvings; see <see cref="BisectSteps"/>.</param>
    public static float LastFree(Func<float, bool> overlapsAt, int steps = BisectSteps)
    {
        ArgumentNullException.ThrowIfNull(overlapsAt);
        if (!overlapsAt(1)) return 1;
        if (overlapsAt(0)) return 0;
        float free = 0, hit = 1;
        for (int i = 0; i < steps; i++)
        {
            float mid = (free + hit) * 0.5f;
            if (overlapsAt(mid)) hit = mid;
            else free = mid;
        }
        return free;
    }

    /// <summary>
    /// A body hitting something immovable: the velocity component into the contact (against <paramref name="normal"/>,
    /// which points out of the obstacle) is reversed and scaled by <paramref name="restitution"/>; the rest, sliding
    /// along the surface, is kept (no friction). A body already moving away is left alone.
    /// </summary>
    public static Vector3 Bounce(Vector3 velocity, Vector3 normal, float restitution = Restitution)
    {
        float into = Vector3.Dot(velocity, normal);
        if (!float.IsFinite(into) || into >= 0) return velocity;
        restitution = float.IsFinite(restitution) ? Math.Clamp(restitution, 0, 1) : 0;
        return velocity - (1 + restitution) * into * normal;
    }

    /// <summary>
    /// Two bodies meeting: an impulse along <paramref name="normal"/> (pointing from B towards A) that reverses their
    /// closing speed scaled by <paramref name="restitution"/>, shared by inverse mass so momentum is kept. A massless
    /// body (inverse mass 0) counts as immovable; two of them do not change. Bodies moving apart are left alone.
    /// </summary>
    public static (Vector3 A, Vector3 B) Bounce(Vector3 velocityA, float inverseMassA, Vector3 velocityB, float inverseMassB,
        Vector3 normal, float restitution = Restitution)
    {
        float closing = Vector3.Dot(velocityA - velocityB, normal);
        float inverseSum = Math.Max(0, inverseMassA) + Math.Max(0, inverseMassB);
        if (!float.IsFinite(closing) || closing >= 0 || inverseSum <= 0 || !float.IsFinite(inverseSum)) return (velocityA, velocityB);
        restitution = float.IsFinite(restitution) ? Math.Clamp(restitution, 0, 1) : 0;
        float impulse = -(1 + restitution) * closing / inverseSum;
        return (velocityA + normal * (impulse * Math.Max(0, inverseMassA)), velocityB - normal * (impulse * Math.Max(0, inverseMassB)));
    }
}
