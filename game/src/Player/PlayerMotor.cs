using Godot;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Physics;
using Num = System.Numerics;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// Zero-g astronaut movement, with the original's rules (see <see cref="AstronautMotion"/>): keys
/// push at 5 m/s², the speed settles at 4 m/s (8 boosting), and with no key held the player coasts to a stop. The body is
/// the original's sphere of radius 0.49, approximated here by the cube around it, with the eye at its centre. While the
/// player rides a ship (<see cref="FlyCamera"/> boards them when they come close, as the original does), velocities are
/// relative to that ship and it does not shove them. Collision is <see cref="RiderCollision"/>'s, this class only
/// adapts the grids to it. Plain C# rather than a Node so the camera stays in charge of input
/// and a save can read and restore <see cref="Velocity"/> and <see cref="Noclip"/>.
/// </summary>
public sealed class PlayerMotor
{
    /// <summary>A long frame (loading hitch, window drag) is treated as several short ones' worth at most,
    /// so a stall cannot fling the player across the asteroid in one step.</summary>
    const float MaxStep = 0.1f;

    /// <summary>The original's body is a sphere of radius 0.49; collision uses the cube that bounds it.</summary>
    public const float BodyRadius = RiderCollision.BodyRadius;
    public static readonly Num.Vector3 HalfExtents = RiderCollision.HalfExtents;
    /// <summary>The eye is at the body's centre.</summary>
    public const float EyeHeight = 0f;

    bool _needsFreeSpot = true;
    bool _noclip;
    /// <summary>The ships as <see cref="RiderCollision"/> sees them, refilled every step (kept to avoid garbage).</summary>
    readonly List<IGridShape> _ships = new();

    /// <summary>World space, m/s; relative to the ridden ship while riding one (see <see cref="Step"/>).</summary>
    public Vector3 Velocity { get; set; }


    /// <summary>
    /// Kept for saves written before boarding existed, which stored momentum taken from a ship here. The original
    /// coasts every motion to a stop, so the motor no longer keeps any: it is always zero after a step.
    /// </summary>
    public Vector3 Drift { get; set; }

    /// <summary>Flies through rock. Turning it off re-checks for overlap, so the player is pushed out of
    /// whatever they were inside instead of being trapped.</summary>
    public bool Noclip
    {
        get => _noclip;
        set
        {
            if (_noclip && !value) _needsFreeSpot = true;
            _noclip = value;
        }
    }

    /// <summary>The velocity that blocks stopped in the last <see cref="Step"/>: its length is the speed the player hit
    /// a surface with (only the part straight into it), its direction points into the surface. Zero without a hit.</summary>
    public Vector3 Impact { get; private set; }

    /// <summary>Advances one frame and returns the new eye position.</summary>
    /// <param name="input">The movement keys this frame, in world space (see <see cref="AstronautMotion.Input"/>).</param>
    /// <param name="riding">The ship the player is riding, if any. It has already carried the player along this frame,
    /// so it is treated as standing still: no shove, but its walls still stop the player and push them out.</param>
    /// <remarks>The collision rules are <see cref="RiderCollision.Step"/>'s: push out of every grid the body overlaps,
    /// the ridden ship included, as in the original; sweep; check the end again.</remarks>
    public Vector3 Step(Vector3 eye, AstronautMotion.Input input, float delta, GridSet grids, ShipGrid? riding = null)
    {
        Impact = Vector3.Zero;
        float dt = Mathf.Clamp(delta, 0f, MaxStep);
        // The sv_player_* rules, and the sv_player_speed_scale cheat on top.
        var cvars = Settings.GameSettings.Cvars;
        var velocity = AstronautMotion.Step(ToNumerics(Velocity), input, dt, AstronautMotion.Tuning.From(cvars),
            cvars.Float(Simulation.Settings.Cvars.PlayerSpeedScale));

        var body = ToNumerics(eye) - new Num.Vector3(0, EyeHeight, 0);
        if (_noclip)
        {
            body += velocity * dt;
        }
        else
        {
            if (_needsFreeSpot)
            {
                _needsFreeSpot = false;
                if (VoxelCollider.FindFreeSpot(body, HalfExtents, grids.World.SolidQuery) is { } free) body = free;
            }
            var world = Shapes(grids);
            var result = RiderCollision.Step(body, velocity, dt, world, _ships, riding is null ? null : Shape(riding),
                cvars.Float(Simulation.Settings.Cvars.PlayerPushoutMax));
            if (result.FreedFromRock) GD.Print("Freed from rock");
            body = result.Center;
            velocity = result.Velocity;
            Impact = ToGodot(result.Impact);
        }

        Velocity = ToGodot(velocity);
        Drift = Vector3.Zero;
        return new Vector3(body.X, body.Y + EyeHeight, body.Z);
    }

    /// <summary>
    /// Boards <paramref name="ship"/>: pushes the player out of it first, along its motion, so a ship that closed
    /// faster than the boarding reach in one frame (up to <c>sv_player_pushout_max</c> into the body) shoves them ahead
    /// of it instead of swallowing them (see
    /// <see cref="RiderCollision.Board"/>), then makes <see cref="Velocity"/> relative to it. Returns the new eye.
    /// </summary>
    public Vector3 Board(Vector3 eye, ShipGrid ship, GridSet grids)
    {
        if (_noclip)
        {
            Velocity -= ship.PointVelocity(eye);
            return eye;
        }
        var lift = new Num.Vector3(0, EyeHeight, 0);
        var world = Shapes(grids);
        var (body, velocity) = RiderCollision.Board(ToNumerics(eye) - lift, ToNumerics(Velocity), Shape(ship), world, _ships,
            Settings.GameSettings.Cvars.Float(Simulation.Settings.Cvars.PlayerPushoutMax));
        Velocity = ToGodot(velocity);
        return ToGodot(body + lift);
    }

    static IGridShape Shape(BlockGrid grid) => GridSet.Shape(grid);

    /// <summary>Fills <see cref="_ships"/> with the ships' shapes, where they are now, and returns the world's.</summary>
    IGridShape Shapes(GridSet grids) => grids.CaptureShapes(_ships);

    /// <summary>
    /// The body (centre <paramref name="bodyCenter"/>, world space) in a grid's local space: its centre there, and the
    /// half extents of its stand-in cube. The body is the original's sphere, which no turn changes; its stand-in cube
    /// is taken in each grid's own axes rather than bounded after turning, so it fits a one-wide corridor of a ship at
    /// any angle.
    /// </summary>
    static (Num.Vector3 Center, Num.Vector3 HalfExtents) LocalBody(BlockGrid grid, Vector3 bodyCenter) =>
        (ToNumerics(grid.ToLocal(bodyCenter)), HalfExtents);

    /// <summary>Whether <paramref name="grid"/> has a solid cell within <paramref name="reach"/> of the eye (a cube of
    /// that half size, as the body is): the boarding test.</summary>
    public static bool Touches(BlockGrid grid, Vector3 eye, float reach)
    {
        // A sphere of radius reach around the eye against each solid cell's cube.
        var p = grid.ToLocal(eye);
        var solid = grid.SolidQuery;
        int x0 = Mathf.FloorToInt(p.X - reach), x1 = Mathf.FloorToInt(p.X + reach);
        int y0 = Mathf.FloorToInt(p.Y - reach), y1 = Mathf.FloorToInt(p.Y + reach);
        int z0 = Mathf.FloorToInt(p.Z - reach), z1 = Mathf.FloorToInt(p.Z + reach);
        float r2 = reach * reach;
        for (int z = z0; z <= z1; z++)
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            if (!solid(x, y, z)) continue;
            float dx = Math.Max(0, Math.Max(x - p.X, p.X - (x + 1)));
            float dy = Math.Max(0, Math.Max(y - p.Y, p.Y - (y + 1)));
            float dz = Math.Max(0, Math.Max(z - p.Z, p.Z - (z + 1)));
            if (dx * dx + dy * dy + dz * dz <= r2) return true;
        }
        return false;
    }

    /// <summary>Whether any cell of <paramref name="grid"/> that <paramref name="contains"/> accepts (grid-local cells)
    /// intersects the body of a player whose eye is at world <paramref name="eye"/>; see <see cref="LocalBody"/>.</summary>
    public static bool BodyOverlaps(BlockGrid grid, Vector3 eye, Func<Vector3I, bool> contains)
    {
        var (center, half) = LocalBody(grid, eye - new Vector3(0, EyeHeight, 0));
        return VoxelCollider.Overlaps(center, half, (x, y, z) => contains(new Vector3I(x, y, z)));
    }

    /// <summary>Whether a player with the eye at world <paramref name="eye"/> would be inside a block of the world or
    /// any ship: a respawn point has to be free.</summary>
    public static bool Blocked(Vector3 eye, GridSet grids) =>
        RiderCollision.Blocked(ToNumerics(eye - new Vector3(0, EyeHeight, 0)), Shape(grids.World),
            grids.Ships.Select(s => (IGridShape)Shape(s)).ToList());

    /// <summary>Whether a block at grid-local <paramref name="cell"/> of <paramref name="grid"/> would be in the body.</summary>
    public static bool BodyOccupies(BlockGrid grid, Vector3 eye, Vector3I cell) => BodyOverlaps(grid, eye, p => p == cell);

    /// <summary>True when a block at <paramref name="cell"/> would intersect the body of a player whose eye is
    /// at <paramref name="eye"/>. Meant for block placing: refusing such a placement keeps the player from
    /// being embedded, which <see cref="VoxelCollider.Move"/> lets them leave but never resolves by itself.</summary>
    public static bool BodyOccupies(Vector3 eye, Vector3I cell) => BodyOverlaps(eye, p => p == cell);

    /// <summary>True when any cell <paramref name="contains"/> accepts intersects the body: placing a multi-block, or a
    /// door about to move into new cells.</summary>
    public static bool BodyOverlaps(Vector3 eye, Func<Vector3I, bool> contains) =>
        VoxelCollider.Overlaps(ToNumerics(eye) - new Num.Vector3(0, EyeHeight, 0), HalfExtents,
            (x, y, z) => contains(new Vector3I(x, y, z)));

    static Num.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
    static Vector3 ToGodot(Num.Vector3 v) => new(v.X, v.Y, v.Z);
}
