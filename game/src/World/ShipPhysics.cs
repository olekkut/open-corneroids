using Godot;
using OpenCorneroids.Simulation.Ships;
using Num = System.Numerics;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Moves the ships: a fixed-step loop (<see cref="FixedStep"/>) driven from the frame, so flight does not depend on the
/// frame rate. Each step the piloted ship's request goes through power, the flight assistant and the thrust allocator
/// (<see cref="GridSystems.Thrust"/>), every awake ship is integrated (<see cref="RigidBodyState.Step"/>), and then
/// collisions are resolved: each ship against the static world, then ship against ship. Ours; original behaviour
/// unknown.
/// </summary>
/// <remarks>
/// <para>
/// Collision is approximate (all tunable): a ship is represented by <see cref="ShipGrid.SurfacePoints"/>, and a step
/// that leaves any of them inside a solid cell of the other grid is undone back to the last free fraction of the step
/// (<see cref="ShipContact.LastFree"/>); the velocity into the contact is reversed and scaled by
/// <see cref="ShipContact.Restitution"/>, and the spin is damped (<see cref="ShipContact.ContactSpinDamping"/>).
/// The normal comes from which faces of the penetrated cells are open (<see cref="ShipContact.Normal"/>).
/// </para>
/// <para>
/// Limits: steps are not swept, so a thin wall can be skipped by a ship faster than a cell per step (120 m/s); the
/// sample points are a cell apart, so a sharp rock corner can poke up to half a cell between them; contacts give no
/// torque (a ship hitting with a wingtip does not spin about it) and no friction; a ship pressed into rock by its
/// thrusters simply stops there. A ship that starts a step already overlapping (built into rock, loaded so) is pushed
/// out along the normal a little each step (<see cref="ShipContact.PushOutPerStep"/>) instead. A resting (asleep) ship
/// is checked once whenever its blocks, its doors or its pose changed since it was last found free, and pushed out the
/// same way until it is.
/// </para>
/// <para>
/// Drawing: the steps do not line up with frames, so each frame the ships are shown at the blend between their last two
/// step poses that the leftover time gives (<see cref="ShipGrid.ShowBetweenSteps"/>), up to a step behind the
/// simulation, and the camera follows that. Without it a ship would move 0, 1 or 2 steps per frame and judder.
/// </para>
/// </remarks>
public sealed class ShipPhysics
{
    /// <summary>Length of one physics step, seconds. Tunable.</summary>
    public const float FixedStep = 1f / 120f;
    /// <summary>Most time one frame may simulate; the rest of a long hitch is dropped. Tunable. Game time (<c>--hold</c>,
    /// <c>--seconds</c>) counts frames the same way, so a loading hitch does not use up a held control.</summary>
    public const double MaxFrame = 0.25;
    /// <summary>An unpiloted ship slower than this (m/s, rad/s) is stopped and left alone until something moves it.</summary>
    const float SleepSpeed = 0.02f, SleepSpin = 0.002f;

    double _accumulator;
    readonly List<Transform3D> _previous = [];
    readonly List<bool> _moved = [];
    readonly List<Num.Vector3> _inside = [];

    /// <summary>Simulates <paramref name="delta"/> seconds of every ship, then updates their textures and flames.</summary>
    /// <param name="piloted">The ship under control, or null.</param>
    public void Advance(double delta, GridSet grids, ShipGrid? piloted, PilotRequest request)
    {
        var ships = grids.Ships;
        for (int i = 0; i < ships.Count; i++) ships[i].BeginSteps();
        _accumulator = Math.Min(_accumulator + Math.Max(0, delta), MaxFrame);
        while (_accumulator >= FixedStep)
        {
            Step(grids, piloted, request);
            _accumulator -= FixedStep;
        }
        float blend = (float)(_accumulator / FixedStep);
        grids.World.Systems.UpdateDisplay();
        for (int i = 0; i < ships.Count; i++)
        {
            ships[i].ShowBetweenSteps(blend);
            ships[i].Systems.UpdateDisplay();
        }
    }

    void Step(GridSet grids, ShipGrid? piloted, PilotRequest request)
    {
        var ships = grids.Ships;
        _previous.Clear();
        _moved.Clear();
        for (int i = 0; i < ships.Count; i++)
        {
            var ship = ships[i];
            _previous.Add(ship.Transform);
            _moved.Add(false);
            var mass = ship.Mass;
            bool controlled = ship == piloted;
            if (mass.IsMassless || (!controlled && Asleep(ship)))
            {
                ship.Systems.Idle();
                // Resting: checked for overlap only after a change (an edit, a door, a pose set from outside), and then
                // collided like a ship that moved, which pushes it out when it is inside something.
                _moved[i] = !mass.IsMassless && ship.RestChecked != ship.RestKey;
                continue;
            }
            var state = ship.BodyState();
            var (force, torque) = ship.Systems.Thrust(state, mass, controlled ? request : null, FixedStep);
            state.Step(state.DirectionToWorld(force), state.DirectionToWorld(torque), mass, FixedStep,
                RigidBodyState.DefaultLinearDamping, RigidBodyState.DefaultAngularDamping);
            ship.SetBodyState(state);
            _moved[i] = true;
        }

        for (int i = 0; i < ships.Count; i++)
            if (_moved[i]) AgainstWorld(grids.World, ships[i], _previous[i]);

        for (int i = 0; i < ships.Count; i++)
        for (int j = i + 1; j < ships.Count; j++)
            if (_moved[i] || _moved[j]) AgainstShip(ships[i], _previous[i], ships[j], _previous[j]);

        for (int i = 0; i < ships.Count; i++)
        {
            var ship = ships[i];
            // A resting ship that nothing pushed is free where it is; one that was pushed is checked again next step.
            if (ship.Transform == _previous[i] && ship.LinearVelocity == Vector3.Zero && ship.AngularVelocity == Vector3.Zero)
                ship.RestChecked = ship.RestKey;
            ship.EndStep(_previous[i]);
        }
    }

    /// <summary>Too slow to matter without a pilot: stopped exactly, so it costs nothing until something pushes it.</summary>
    static bool Asleep(ShipGrid ship)
    {
        if (ship.LinearVelocity.Length() >= SleepSpeed || ship.AngularVelocity.Length() >= SleepSpin) return false;
        ship.LinearVelocity = Vector3.Zero;
        ship.AngularVelocity = Vector3.Zero;
        return true;
    }

    /// <summary>
    /// Whether any of <paramref name="points"/> (local to a body at <paramref name="pose"/>) is inside a solid cell of
    /// <paramref name="other"/> placed at <paramref name="otherPose"/>. With <paramref name="inside"/> every such point
    /// is collected, in the other grid's local space; without, the first one ends the search.
    /// </summary>
    static bool Overlaps(IReadOnlyList<Num.Vector3> points, Transform3D pose, BlockGrid other, Transform3D otherPose, List<Num.Vector3>? inside)
    {
        var toOther = otherPose.AffineInverse() * pose;
        bool any = false;
        for (int i = 0; i < points.Count; i++)
        {
            var p = toOther * points[i].ToGodot();
            if (!other.IsSolid(new Vector3I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z)))) continue;
            any = true;
            if (inside is null) return true;
            inside.Add(p.ToNumerics());
        }
        return any;
    }

    void AgainstWorld(VoxelWorld world, ShipGrid ship, Transform3D previous)
    {
        var points = ship.SurfacePoints;
        var worldPose = world.Transform;
        var current = ship.Transform;
        _inside.Clear();
        if (!Overlaps(points, current, world, worldPose, _inside)) return;
        var normal = ContactNormal(world, worldPose, ship.LinearVelocity);

        if (Overlaps(points, previous, world, worldPose, null))
        {
            // Already inside before this step: no free pose to go back to, so push out a little along the normal.
            ship.Transform = current with { Origin = current.Origin + normal * ShipContact.PushOutPerStep };
            ship.LinearVelocity = ShipContact.Bounce(ship.LinearVelocity.ToNumerics(), normal.ToNumerics(), 0).ToGodot();
            return;
        }
        float t = ShipContact.LastFree(f => Overlaps(points, ship.Interpolate(previous, current, f), world, worldPose, null));
        ship.Transform = ship.Interpolate(previous, current, t);
        LogHit(ship, "the asteroids", -ship.LinearVelocity.Dot(normal), normal);
        ship.LinearVelocity = ShipContact.Bounce(ship.LinearVelocity.ToNumerics(), normal.ToNumerics()).ToGodot();
        ship.AngularVelocity *= ShipContact.ContactSpinDamping;
    }

    /// <summary>
    /// Two ships whose spheres touch: if their sample points meet each other's solid cells, both are taken back to the
    /// last fraction of the step where neither did, and an impulse along the contact normal is shared by their masses.
    /// </summary>
    void AgainstShip(ShipGrid a, Transform3D previousA, ShipGrid b, Transform3D previousB)
    {
        var (centerA, radiusA) = a.WorldExtent;
        var (centerB, radiusB) = b.WorldExtent;
        if (centerA.DistanceTo(centerB) > radiusA + radiusB + 1) return;
        var pointsA = a.SurfacePoints;
        var pointsB = b.SurfacePoints;
        bool Meet(Transform3D poseA, Transform3D poseB) =>
            Overlaps(pointsA, poseA, b, poseB, null) || Overlaps(pointsB, poseB, a, poseA, null);

        var currentA = a.Transform;
        var currentB = b.Transform;
        if (!Meet(currentA, currentB)) return;

        // The normal points from B towards A: out of B where A's points are inside B, or into A for B's points in A.
        _inside.Clear();
        Vector3 normal;
        if (Overlaps(pointsA, currentA, b, currentB, _inside)) normal = ContactNormal(b, currentB, a.LinearVelocity - b.LinearVelocity);
        else
        {
            Overlaps(pointsB, currentB, a, currentA, _inside);
            normal = -ContactNormal(a, currentA, b.LinearVelocity - a.LinearVelocity);
        }

        bool stuck = Meet(previousA, previousB);
        if (stuck)
        {
            a.Transform = currentA with { Origin = currentA.Origin + normal * (ShipContact.PushOutPerStep * 0.5f) };
            b.Transform = currentB with { Origin = currentB.Origin - normal * (ShipContact.PushOutPerStep * 0.5f) };
        }
        else
        {
            float t = ShipContact.LastFree(f => Meet(a.Interpolate(previousA, currentA, f), b.Interpolate(previousB, currentB, f)));
            a.Transform = a.Interpolate(previousA, currentA, t);
            b.Transform = b.Interpolate(previousB, currentB, t);
            a.AngularVelocity *= ShipContact.ContactSpinDamping;
            b.AngularVelocity *= ShipContact.ContactSpinDamping;
        }
        LogHit(a, b.Name, -(a.LinearVelocity - b.LinearVelocity).Dot(normal), normal);
        var (va, vb) = ShipContact.Bounce(a.LinearVelocity.ToNumerics(), a.Mass.InverseMass, b.LinearVelocity.ToNumerics(), b.Mass.InverseMass,
            normal.ToNumerics(), stuck ? 0 : ShipContact.Restitution);
        a.LinearVelocity = va.ToGodot();
        b.LinearVelocity = vb.ToGodot();
    }

    /// <summary>A hit worth noting in the log: closing faster than <see cref="LoggedSpeed"/>, so a ship resting against
    /// rock under thrust (closing at a hair each step) stays quiet.</summary>
    static void LogHit(ShipGrid ship, string what, float closing, Vector3 normal)
    {
        if (closing < LoggedSpeed) return;
        GD.Print(FormattableString.Invariant(
            $"{ship.Name} hit {what} at {closing:0.0} m/s (normal {normal.X:0.##}, {normal.Y:0.##}, {normal.Z:0.##}), at {ship.Position.X:0.#}, {ship.Position.Y:0.#}, {ship.Position.Z:0.#}"));
    }

    const float LoggedSpeed = 0.5f;

    /// <summary>
    /// The world-space way out of <paramref name="grid"/> for the points in <see cref="_inside"/> (its local space), or,
    /// when the cells don't say, against the motion <paramref name="velocity"/> (or up, for a body at rest).
    /// </summary>
    Vector3 ContactNormal(BlockGrid grid, Transform3D gridPose, Vector3 velocity)
    {
        if (ShipContact.Normal(_inside, grid.SolidQuery) is { } local) return (gridPose.Basis * local.ToGodot()).Normalized();
        return velocity.LengthSquared() > 1e-8f ? -velocity.Normalized() : Vector3.Up;
    }
}
