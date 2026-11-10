using Godot;
using OpenCorneroids.Simulation.Ships;
using Num = System.Numerics;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Moves the ships: a fixed-step loop (<see cref="FixedStep"/>) driven from the frame, so flight does not depend on the
/// frame rate. Each step the piloted ship's request goes through power, console bindings or the flight assistant
/// (<see cref="GridSystems.Thrust"/>), every awake ship is integrated, and then collisions are resolved: each ship
/// against the static world, then ship against ship.
/// </summary>
public sealed class ShipPhysics
{
    /// <summary>Length of one physics step, seconds. Tunable.</summary>
    public const float FixedStep = 1f / 120f;
    /// <summary>Most time one frame may simulate; the rest of a long hitch is dropped. Tunable. Game time (<c>--hold</c>,
    /// <c>--seconds</c>) counts frames the same way, so a loading hitch does not use up a held control.</summary>
    public const double MaxFrame = 0.25;
    /// <summary>An unpiloted ship slower than this (m/s, rad/s) is stopped and left alone until something moves it.</summary>
    const float SleepSpeed = 0.02f, SleepSpin = 0.002f;

    /// <summary>Bounce after a hit (<c>sv_ship_restitution</c>, 0 as in the original).</summary>
    static float Restitution => Settings.GameSettings.Cvars.Float(Simulation.Settings.Cvars.ShipRestitution);

    /// <summary>Spin kept through a contact (<c>sv_ship_contact_spin_damping</c>, ours).</summary>
    static float SpinDamping => Settings.GameSettings.Cvars.Float(Simulation.Settings.Cvars.ShipContactSpinDamping);

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
            if (ship.IsAsteroid)
            {
                ship.LinearVelocity = Vector3.Zero;
                ship.AngularVelocity = Vector3.Zero;
                continue;
            }
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
            if (controlled && request.UseFlightAssist)
            {
                state.Step(state.DirectionToWorld(force), state.DirectionToWorld(torque), mass, FixedStep,
                    RigidBodyState.DefaultLinearDamping, RigidBodyState.DefaultAngularDamping);
            }
            else
            {
                state.StepOriginal(state.DirectionToWorld(force), state.DirectionToWorld(torque), mass, FixedStep,
                    thrustersFiring: ship.Systems.AnyThrusting, ShipLimits.From(Settings.GameSettings.Cvars));
            }
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
        ship.LinearVelocity = ShipContact.Bounce(ship.LinearVelocity.ToNumerics(), normal.ToNumerics(), Restitution).ToGodot();
        ship.AngularVelocity *= SpinDamping;
    }

    /// <summary>
    /// Two ships whose spheres touch: if their sample points meet each other's solid cells, both are taken back to the
    /// last fraction of the step where neither did, and an impulse along the contact normal is shared by their masses.
    /// </summary>
    void AgainstShip(ShipGrid a, Transform3D previousA, ShipGrid b, Transform3D previousB)
    {
        if (a.IsAsteroid && b.IsAsteroid) return;
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
            if (a.IsAsteroid)
            {
                b.Transform = currentB with { Origin = currentB.Origin - normal * ShipContact.PushOutPerStep };
            }
            else if (b.IsAsteroid)
            {
                a.Transform = currentA with { Origin = currentA.Origin + normal * ShipContact.PushOutPerStep };
            }
            else
            {
                a.Transform = currentA with { Origin = currentA.Origin + normal * (ShipContact.PushOutPerStep * 0.5f) };
                b.Transform = currentB with { Origin = currentB.Origin - normal * (ShipContact.PushOutPerStep * 0.5f) };
            }
        }
        else
        {
            float t = ShipContact.LastFree(f => Meet(
                a.IsAsteroid ? currentA : a.Interpolate(previousA, currentA, f),
                b.IsAsteroid ? currentB : b.Interpolate(previousB, currentB, f)));
            if (!a.IsAsteroid)
            {
                a.Transform = a.Interpolate(previousA, currentA, t);
                a.AngularVelocity *= SpinDamping;
            }
            if (!b.IsAsteroid)
            {
                b.Transform = b.Interpolate(previousB, currentB, t);
                b.AngularVelocity *= SpinDamping;
            }
        }
        string targetName = a.IsAsteroid ? "the asteroids" : (b.IsAsteroid ? "the asteroids" : b.Name);
        LogHit(a.IsAsteroid ? b : a, targetName, -(a.LinearVelocity - b.LinearVelocity).Dot(normal), normal);
        float invMassA = a.IsAsteroid ? 0f : a.Mass.InverseMass;
        float invMassB = b.IsAsteroid ? 0f : b.Mass.InverseMass;
        var (va, vb) = ShipContact.Bounce(a.LinearVelocity.ToNumerics(), invMassA, b.LinearVelocity.ToNumerics(), invMassB,
            normal.ToNumerics(), stuck ? 0 : Restitution);
        if (!a.IsAsteroid) a.LinearVelocity = va.ToGodot();
        if (!b.IsAsteroid) b.LinearVelocity = vb.ToGodot();
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
