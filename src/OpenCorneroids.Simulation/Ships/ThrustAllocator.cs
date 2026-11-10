using System.Numerics;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// Turns a requested wrench (force and torque about the centre of mass, ship-local) into a throttle 0..1 for each
/// thruster, so a ship can only move and turn where it really has thrust. Ours; original behaviour unknown.
/// </summary>
/// <remarks>
/// <para>
/// Solves the bounded least-squares problem <c>min ‖W(A·t − w)‖² + λ‖t‖²</c> with <c>0 ≤ t ≤ 1</c>, where column i of
/// the 6×N matrix A is thruster i's wrench at full throttle (force, then torque) and w the request. W divides the
/// torque rows by the thrusters' RMS lever arm (at least 1 cell), so a torque error is in the same units as a force
/// error, and then multiplies them by <see cref="TorquePriority"/>: turning comes first. When the request cannot be
/// met (the flight assistant usually asks for more push than the ship has), the error goes into the force, not into a
/// torque nobody asked for, so holding "forward" on a lopsided ship throttles down the stronger side and flies
/// straight instead of spinning. Only when that leaves almost none of the push (no zero-torque way to push at all)
/// does it give way: see <see cref="FallbackShare"/>. λ is a tiny "fuel" term that makes the answer unique when thrusters are redundant
/// (opposed pairs never both fire to cancel out) and nudges idle thrusters to exactly 0.
/// </para>
/// <para>
/// The solver warm-starts with <see cref="Sweeps"/> passes of projected coordinate descent, then finishes with a
/// primal active-set method: the thrusters strictly between 0 and 1 are solved exactly together (a 6×6 system via the
/// identity <c>(AᵀA + λI)⁻¹Aᵀ = Aᵀ(AAᵀ + λI)⁻¹</c>, so each step costs O(N)), a thruster that would leave the box
/// stops at its bound, and a thruster at a bound whose gradient points into the box is released, until the optimality
/// conditions hold or the step budget (2·N + <see cref="MaxActiveSetSteps"/>) runs out. No step raises the error, so a
/// cut-off still returns the best answer found so far. Deterministic: the same input always gives the same throttles.
/// </para>
/// </remarks>
public static class ThrustAllocator
{
    /// <summary>Coordinate-descent passes that warm-start the active-set finish. Tunable.</summary>
    public const int Sweeps = 8;

    /// <summary>Active-set steps allowed beyond 2·N (each frees or fixes thrusters). Tunable; usually only a few are
    /// needed.</summary>
    public const int MaxActiveSetSteps = 32;

    /// <summary>
    /// How much more a torque error weighs than a force error of the same size at the RMS lever arm. Large, so turning
    /// effectively comes first. Tunable; original behaviour unknown.
    /// </summary>
    public const double TorquePriority = 1000;

    /// <summary>
    /// When the turn-first answer delivers less than this share of the push the ship could give with push and turn
    /// weighted equally (a "thrusters on the back" ship whose rear thrust sits off the centre of mass and has nothing to
    /// trim the pitch it makes), the throttles blend toward that push-first answer: fully at zero push, not at all at
    /// this share. Such a ship then accelerates and pitches (and the assistant can't stop the pitch) instead of hardly
    /// moving. Tunable; original behaviour unknown.
    /// </summary>
    public const float FallbackShare = 0.5f;

    /// <summary>The fuel weight λ, relative to the mean squared (unprioritised) column norm. Tunable; must stay
    /// small.</summary>
    public const float Regularization = 1e-5f;

    /// <summary>Allocates a new array; see <see cref="Allocate(IReadOnlyList{ThrusterMount}, Vector3, Vector3, Vector3, float, Span{float})"/>.</summary>
    public static float[] Allocate(IReadOnlyList<ThrusterMount> thrusters, Vector3 comLocal, Vector3 desiredForceLocal,
        Vector3 desiredTorqueLocal, float availability)
    {
        ArgumentNullException.ThrowIfNull(thrusters);
        var throttles = new float[thrusters.Count];
        Allocate(thrusters, comLocal, desiredForceLocal, desiredTorqueLocal, availability, throttles);
        return throttles;
    }

    /// <summary>
    /// Writes the throttle for <c>thrusters[i]</c> to <c>throttles[i]</c>.
    /// </summary>
    /// <param name="comLocal">Centre of mass, ship-local; torques are about it.</param>
    /// <param name="desiredForceLocal">Requested force on the ship, ship-local axes.</param>
    /// <param name="desiredTorqueLocal">Requested torque about the centre of mass, ship-local axes.</param>
    /// <param name="availability">
    /// Fraction of each thruster's force that is really there (power satisfaction), 0..1. Lower availability means
    /// higher throttles for the same request, up to 1. Clamped; non-finite counts as 0.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="throttles"/> is shorter than the thruster list.</exception>
    public static void Allocate(IReadOnlyList<ThrusterMount> thrusters, Vector3 comLocal, Vector3 desiredForceLocal,
        Vector3 desiredTorqueLocal, float availability, Span<float> throttles) =>
        Allocate(thrusters, comLocal, desiredForceLocal, desiredTorqueLocal, availability, throttles, new Workspace());

    /// <summary>
    /// As <see cref="Allocate(IReadOnlyList{ThrusterMount}, Vector3, Vector3, Vector3, float, Span{float})"/>, with its
    /// scratch arrays taken from <paramref name="workspace"/>, so a ship solving twice per physics step makes no garbage.
    /// The throttles are exactly those of a fresh workspace.
    /// </summary>
    public static void Allocate(IReadOnlyList<ThrusterMount> thrusters, Vector3 comLocal, Vector3 desiredForceLocal,
        Vector3 desiredTorqueLocal, float availability, Span<float> throttles, Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(thrusters);
        ArgumentNullException.ThrowIfNull(workspace);
        int n = thrusters.Count;
        if (throttles.Length < n) throw new ArgumentException("One throttle slot per thruster is needed.", nameof(throttles));
        throttles[..n].Clear();
        availability = float.IsFinite(availability) ? Math.Clamp(availability, 0, 1) : 0;
        if (n == 0 || availability <= 0 || !Finite(comLocal) || !Finite(desiredForceLocal) || !Finite(desiredTorqueLocal)) return;

        workspace.Fit(n);
        Solve(thrusters, comLocal, desiredForceLocal, desiredTorqueLocal, availability, TorquePriority, throttles[..n], workspace);
        float requested = desiredForceLocal.Length();
        if (!(requested > 0)) return;
        var direction = desiredForceLocal / requested;
        float turnFirst = Vector3.Dot(Wrench(thrusters, comLocal, throttles[..n], availability).Force, direction);
        if (turnFirst >= FallbackShare * requested) return;

        // Little of the push survives the torque priority: compare with what the ship gives when push and turn weigh
        // the same, and lean toward that the less the turn-first answer delivers of it.
        var pushFirst = workspace.PushFirst.AsSpan(0, n);
        Solve(thrusters, comLocal, desiredForceLocal, desiredTorqueLocal, availability, 1, pushFirst, workspace);
        float push = Vector3.Dot(Wrench(thrusters, comLocal, pushFirst, availability).Force, direction);
        if (!(push > 0) || turnFirst >= FallbackShare * push) return;
        float blend = Math.Clamp(1 - Math.Max(0, turnFirst) / (FallbackShare * push), 0, 1);
        for (int i = 0; i < n; i++) throttles[i] = Math.Clamp(throttles[i] + blend * (pushFirst[i] - throttles[i]), 0, 1);
    }

    /// <summary>The bounded least-squares allocation (see the remarks) with torque rows weighted by
    /// <paramref name="torquePriority"/>; <paramref name="throttles"/> is cleared on entry.</summary>
    static void Solve(IReadOnlyList<ThrusterMount> thrusters, Vector3 comLocal, Vector3 desiredForceLocal,
        Vector3 desiredTorqueLocal, float availability, double torquePriority, Span<float> throttles, Workspace workspace)
    {
        int n = thrusters.Count;
        throttles.Clear();
        double leverSquares = 0;
        for (int i = 0; i < n; i++) leverSquares += (thrusters[i].LocalPosition - comLocal).LengthSquared();
        double lever = Math.Max(1, Math.Sqrt(leverSquares / n));
        if (!double.IsFinite(lever)) return;
        double torqueWeight = torquePriority / lever;

        // Weighted columns (force, torque · weight) at full throttle and current availability.
        var columns = workspace.Columns.AsSpan(0, 6 * n);
        var norms = workspace.Norms.AsSpan(0, n);
        norms.Clear();
        double normSum = 0;
        for (int i = 0; i < n; i++)
        {
            var t = thrusters[i];
            float magnitude = float.IsFinite(t.MaxForce) ? Math.Max(0, t.MaxForce) * availability : 0;
            var dir = Finite(t.LocalForceDirection) ? t.LocalForceDirection : Vector3.Zero;
            var force = dir * magnitude;
            var arm = t.LocalPosition - comLocal;
            var torque = Finite(arm) ? Vector3.Cross(arm, force) : Vector3.Zero;
            var c = columns.Slice(6 * i, 6);
            c[0] = force.X; c[1] = force.Y; c[2] = force.Z;
            c[3] = torque.X * torqueWeight; c[4] = torque.Y * torqueWeight; c[5] = torque.Z * torqueWeight;
            for (int k = 0; k < 6; k++) norms[i] += c[k] * c[k];
            // λ follows the unprioritised size, so the torque priority does not inflate the fuel term.
            normSum += force.LengthSquared() + torque.LengthSquared() / (lever * lever);
        }
        if (!(normSum > 0) || !double.IsFinite(normSum)) return;
        double lambda = Regularization * normSum / n;

        Span<double> request =
        [
            desiredForceLocal.X, desiredForceLocal.Y, desiredForceLocal.Z,
            desiredTorqueLocal.X * torqueWeight, desiredTorqueLocal.Y * torqueWeight, desiredTorqueLocal.Z * torqueWeight,
        ];
        var current = workspace.Current.AsSpan(0, n);
        current.Clear();
        CoordinateDescent(columns, norms, request, lambda, current);
        ActiveSet(columns, norms, request, lambda, current, 2 * n + MaxActiveSetSteps, workspace.Free.AsSpan(0, n), workspace.Target.AsSpan(0, n));
        for (int i = 0; i < n; i++) throttles[i] = (float)Math.Clamp(current[i], 0, 1);
    }

    /// <summary>Projected coordinate descent from <paramref name="t"/>: exact minimisation along one throttle at a
    /// time, clamped to the box.</summary>
    static void CoordinateDescent(ReadOnlySpan<double> columns, ReadOnlySpan<double> norms, ReadOnlySpan<double> request, double lambda, Span<double> t)
    {
        Span<double> r = stackalloc double[6];
        Residual(columns, request, t, r);
        for (int sweep = 0; sweep < Sweeps; sweep++)
        {
            for (int i = 0; i < t.Length; i++)
            {
                if (norms[i] <= 0) continue;
                var c = columns.Slice(6 * i, 6);
                double gradient = lambda * t[i];
                for (int k = 0; k < 6; k++) gradient += c[k] * r[k];
                double next = Math.Clamp(t[i] - gradient / (norms[i] + lambda), 0, 1);
                double delta = next - t[i];
                if (delta == 0) continue;
                t[i] = next;
                for (int k = 0; k < 6; k++) r[k] += c[k] * delta;
            }
        }
    }

    /// <summary>
    /// Primal active-set finish for <c>min ½‖A·t − w‖² + ½λ‖t‖²</c>, <c>0 ≤ t ≤ 1</c>, from the feasible
    /// <paramref name="t"/>. Every step keeps <paramref name="t"/> feasible and never raises the objective.
    /// </summary>
    static void ActiveSet(ReadOnlySpan<double> columns, ReadOnlySpan<double> norms, ReadOnlySpan<double> request, double lambda,
        Span<double> t, int maxSteps, Span<bool> free, Span<double> target)
    {
        int n = t.Length;
        for (int i = 0; i < n; i++) free[i] = norms[i] > 0 && t[i] > 0 && t[i] < 1;
        target.Clear();
        Span<double> b = stackalloc double[6];
        Span<double> m = stackalloc double[36];
        Span<double> y = stackalloc double[6];
        Span<double> r = stackalloc double[6];
        double scale = 0;
        for (int k = 0; k < 6; k++) scale += request[k] * request[k];
        for (int i = 0; i < n; i++) scale += norms[i] / n;
        double tolerance = 1e-12 * Math.Sqrt(scale);   // well above round-off, far below any real gain
        int justReleased = -1;

        for (int step = 0; step < maxSteps; step++)
        {
            // Best throttles for the free set with the bound ones held: z_F = A_Fᵀ (A_F A_Fᵀ + λI)⁻¹ (w − A_B t_B).
            request.CopyTo(b);
            m.Clear();
            for (int k = 0; k < 6; k++) m[7 * k] = lambda;
            for (int i = 0; i < n; i++)
            {
                var c = columns.Slice(6 * i, 6);
                if (free[i])
                {
                    for (int row = 0; row < 6; row++)
                        for (int col = 0; col <= row; col++) m[6 * row + col] += c[row] * c[col];
                }
                else if (t[i] != 0)
                {
                    for (int k = 0; k < 6; k++) b[k] -= c[k] * t[i];
                }
            }
            if (!CholeskySolve(m, b, y)) return;

            double alpha = 1;
            int blocking = -1;
            for (int i = 0; i < n; i++)
            {
                if (!free[i]) continue;
                var c = columns.Slice(6 * i, 6);
                double z = 0;
                for (int k = 0; k < 6; k++) z += c[k] * y[k];
                target[i] = z;
                double limit = z < 0 ? t[i] / (t[i] - z) : z > 1 ? (1 - t[i]) / (z - t[i]) : 1;
                if (limit < alpha) { alpha = limit; blocking = i; }
            }

            if (blocking >= 0)
            {
                // Round-off: the thruster just released cannot move inward after all, so this is as good as it gets.
                if (blocking == justReleased && alpha <= 0) return;
                // Go as far toward z as the box allows; thrusters that reach a bound stop there.
                for (int i = 0; i < n; i++)
                {
                    if (!free[i]) continue;
                    t[i] += alpha * (target[i] - t[i]);
                    if (i == blocking) t[i] = target[i] < 0 ? 0 : 1;
                    else if (t[i] <= 1e-12) t[i] = 0;
                    else if (t[i] >= 1 - 1e-12) t[i] = 1;
                    else continue;
                    free[i] = false;
                }
                justReleased = -1;
                continue;
            }

            for (int i = 0; i < n; i++) if (free[i]) t[i] = target[i];

            // Optimal for this free set: release the bound thruster whose gradient points furthest into the box.
            Residual(columns, request, t, r);
            int release = -1;
            double worst = tolerance;
            for (int i = 0; i < n; i++)
            {
                if (free[i] || norms[i] <= 0) continue;
                var c = columns.Slice(6 * i, 6);
                double gradient = lambda * t[i];
                for (int k = 0; k < 6; k++) gradient += c[k] * r[k];
                double inward = (t[i] == 0 ? -gradient : gradient) / Math.Sqrt(norms[i]);
                if (inward > worst) { worst = inward; release = i; }
            }
            if (release < 0) return;
            free[release] = true;
            justReleased = release;
        }
    }

    /// <summary>r = A·t − w.</summary>
    static void Residual(ReadOnlySpan<double> columns, ReadOnlySpan<double> request, ReadOnlySpan<double> t, Span<double> r)
    {
        for (int k = 0; k < 6; k++) r[k] = -request[k];
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == 0) continue;
            for (int k = 0; k < 6; k++) r[k] += columns[6 * i + k] * t[i];
        }
    }

    /// <summary>
    /// Solves M·y = b for a symmetric positive definite 6×6 M given by its lower triangle (row-major, overwritten by
    /// its Cholesky factor), with two rounds of iterative refinement: the torque priority makes M badly conditioned, and
    /// refinement recovers the force rows' precision. False if M is not numerically positive definite.
    /// </summary>
    static bool CholeskySolve(Span<double> m, ReadOnlySpan<double> b, Span<double> y)
    {
        Span<double> original = stackalloc double[36];
        m.CopyTo(original);
        for (int j = 0; j < 6; j++)
        {
            double d = m[7 * j];
            for (int k = 0; k < j; k++) d -= m[6 * j + k] * m[6 * j + k];
            if (!(d > 0) || !double.IsFinite(d)) return false;
            d = Math.Sqrt(d);
            m[7 * j] = d;
            for (int i = j + 1; i < 6; i++)
            {
                double s = m[6 * i + j];
                for (int k = 0; k < j; k++) s -= m[6 * i + k] * m[6 * j + k];
                m[6 * i + j] = s / d;
            }
        }
        Substitute(m, b, y);
        Span<double> residual = stackalloc double[6];
        Span<double> correction = stackalloc double[6];
        for (int round = 0; round < 2; round++)
        {
            for (int i = 0; i < 6; i++)
            {
                double s = b[i];
                for (int k = 0; k < 6; k++) s -= original[i >= k ? 6 * i + k : 6 * k + i] * y[k];
                residual[i] = s;
            }
            Substitute(m, residual, correction);
            for (int i = 0; i < 6; i++) y[i] += correction[i];
        }
        return true;
    }

    /// <summary>y = (L·Lᵀ)⁻¹·b for the lower-triangular Cholesky factor L.</summary>
    static void Substitute(ReadOnlySpan<double> l, ReadOnlySpan<double> b, Span<double> y)
    {
        for (int i = 0; i < 6; i++)
        {
            double s = b[i];
            for (int k = 0; k < i; k++) s -= l[6 * i + k] * y[k];
            y[i] = s / l[7 * i];
        }
        for (int i = 5; i >= 0; i--)
        {
            double s = y[i];
            for (int k = i + 1; k < 6; k++) s -= l[6 * k + i] * y[k];
            y[i] = s / l[7 * i];
        }
    }

    /// <summary>
    /// The force and torque (about <paramref name="comLocal"/>, ship-local) the thrusters give at these throttles.
    /// Throttles are clamped to 0..1 and missing ones count as 0.
    /// </summary>
    public static (Vector3 Force, Vector3 Torque) Wrench(IReadOnlyList<ThrusterMount> thrusters, Vector3 comLocal,
        ReadOnlySpan<float> throttles, float availability = 1)
    {
        ArgumentNullException.ThrowIfNull(thrusters);
        availability = float.IsFinite(availability) ? Math.Clamp(availability, 0, 1) : 0;
        Vector3 force = Vector3.Zero, torque = Vector3.Zero;
        for (int i = 0; i < thrusters.Count && i < throttles.Length; i++)
        {
            var t = thrusters[i];
            float throttle = float.IsFinite(throttles[i]) ? Math.Clamp(throttles[i], 0, 1) : 0;
            float magnitude = float.IsFinite(t.MaxForce) ? Math.Max(0, t.MaxForce) : 0;
            var f = t.LocalForceDirection * (magnitude * throttle * availability);
            if (!Finite(f)) continue;
            force += f;
            torque += Vector3.Cross(t.LocalPosition - comLocal, f);
        }
        return (force, torque);
    }

    /// <summary>
    /// Scratch arrays for <see cref="Allocate(IReadOnlyList{ThrusterMount}, Vector3, Vector3, Vector3, float, Span{float}, Workspace)"/>,
    /// grown to the largest ship seen and reused. One per caller; not for use by two solves at once.
    /// </summary>
    public sealed class Workspace
    {
        internal double[] Columns = [];
        internal double[] Norms = [];
        internal double[] Current = [];
        internal double[] Target = [];
        internal bool[] Free = [];
        internal float[] PushFirst = [];

        /// <summary>Makes room for <paramref name="n"/> thrusters.</summary>
        internal void Fit(int n)
        {
            if (Norms.Length >= n) return;
            Columns = new double[6 * n];
            Norms = new double[n];
            Current = new double[n];
            Target = new double[n];
            Free = new bool[n];
            PushFirst = new float[n];
        }
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
