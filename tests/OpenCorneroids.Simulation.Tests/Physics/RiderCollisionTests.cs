using System.Numerics;
using OpenCorneroids.Simulation.Physics;
using OpenCorneroids.Simulation.Settings;

namespace OpenCorneroids.Simulation.Tests.Physics;

public class RiderCollisionTests
{
    const float Dt = 1f / 15f;
    const float Close = 1e-4f;

    /// <summary>A block grid with a pose and a constant velocity; anything not filled is empty.</summary>
    sealed class Grid : IGridShape
    {
        readonly HashSet<(int, int, int)> _solid = new();

        public Grid(Vector3 position = default, Quaternion? turn = null, Vector3 velocity = default)
        {
            ToWorld = Matrix4x4.CreateFromQuaternion(turn ?? Quaternion.Identity) * Matrix4x4.CreateTranslation(position);
            Matrix4x4.Invert(ToWorld, out var inverse);
            ToLocal = inverse;
            Velocity = velocity;
            Solid = (x, y, z) => _solid.Contains((x, y, z));
        }

        public Vector3 Velocity { get; }
        public Matrix4x4 ToWorld { get; }
        public Matrix4x4 ToLocal { get; }
        public Func<int, int, int, bool> Solid { get; }
        public bool Infinite { get; init; }

        public (Vector3 Center, float Radius) Extent
        {
            get
            {
                if (Infinite || _solid.Count == 0) return (Vector3.Zero, float.PositiveInfinity);
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                foreach (var (x, y, z) in _solid)
                {
                    min = Vector3.Min(min, new Vector3(x, y, z));
                    max = Vector3.Max(max, new Vector3(x + 1, y + 1, z + 1));
                }
                return (Vector3.Transform((min + max) * 0.5f, ToWorld), (max - min).Length() * 0.5f + 0.1f);
            }
        }

        public Vector3 PointVelocity(Vector3 world) => Velocity;

        public Grid Fill(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                _solid.Add((x, y, z));
            return this;
        }

        /// <summary>The body's stand-in cube against this grid, checked directly (not through the extent).</summary>
        public bool Holds(Vector3 center) =>
            VoxelCollider.Overlaps(Vector3.Transform(center, ToLocal), RiderCollision.HalfExtents, Solid);
    }

    static Grid EmptyWorld() => new() { Infinite = true };

    static bool FreeEverywhere(Vector3 center, Grid world, IReadOnlyList<Grid> ships) =>
        !world.Holds(center) && ships.All(s => !s.Holds(center));

    [Fact]
    public void The_push_out_limit_is_ours_and_saved_with_the_world()
    {
        Assert.Equal(2.5, Cvars.PlayerPushoutMax.Default);
        Assert.Equal(RiderCollision.DefaultMaxPush, (float)Cvars.PlayerPushoutMax.Default);
        Assert.Equal("sv_player_pushout_max", Cvars.PlayerPushoutMax.Name);
        Assert.True(Cvars.PlayerPushoutMax.Flags.HasFlag(CvarFlags.World));
        Assert.StartsWith("Ours", Cvars.PlayerPushoutMax.Description);
        Assert.Contains(Cvars.PlayerPushoutMax, Cvars.All);
    }

    // (a)
    [Fact]
    public void A_ship_closing_a_whole_metre_in_one_step_shoves_the_boarding_player_ahead_instead_of_swallowing_them()
    {
        // A wall one block thick at local x = 0, moving +x at 1 m per step; it has just moved from x = 0 to x = 1, into
        // a body that was 0.2 m clear of it.
        var world = EmptyWorld();
        var ship = new Grid(new Vector3(1, 0, 0), velocity: new Vector3(1 / Dt, 0, 0)).Fill(0, -5, -5, 0, 4, 4);
        IReadOnlyList<IGridShape> ships = [ship];
        var body = new Vector3(1.69f, 0.5f, 0.5f);
        Assert.True(ship.Holds(body));

        var (boarded, relative) = RiderCollision.Board(body, Vector3.Zero, ship, world, ships);
        var result = RiderCollision.Step(boarded, relative, Dt, world, ships, riding: ship);

        Assert.False(ship.Holds(result.Center));
        Assert.True(result.Center.X >= 2 + RiderCollision.BodyRadius - Close, $"pushed back through the hull to {result.Center}");
        // Shoved to the ship's speed, so aboard it the player is at rest against the wall.
        Assert.True(Vector3.Distance(relative, Vector3.Zero) < Close, $"relative velocity {relative}");
    }

    [Fact]
    public void A_ship_that_closed_past_the_bodys_centre_still_shoves_the_boarding_player_ahead()
    {
        // The wall has closed 1.5 m in one step, from [-0.5, 0.5] to [1, 2], past the body's centre at 1.4: the way
        // back through the hull (0.89 m) is shorter than the way ahead of it (1.09 m), but the ship's motion wins.
        var world = EmptyWorld();
        var ship = new Grid(new Vector3(1, 0, 0), velocity: new Vector3(1.5f / Dt, 0, 0)).Fill(0, -5, -5, 0, 4, 4);
        IReadOnlyList<IGridShape> ships = [ship];
        var body = new Vector3(1.4f, 0.5f, 0.5f);
        Assert.True(ship.Holds(body));

        var (boarded, relative) = RiderCollision.Board(body, Vector3.Zero, ship, world, ships);

        Assert.False(ship.Holds(boarded));
        Assert.True(boarded.X >= 2 + RiderCollision.BodyRadius - Close, $"pushed back through the hull to {boarded}");
        Assert.True(relative.Length() < Close, $"relative velocity {relative}");
    }

    [Fact]
    public void A_ridden_ship_that_overlaps_the_body_pushes_it_out_too()
    {
        var world = EmptyWorld();
        var ship = new Grid(new Vector3(1, 0, 0), velocity: new Vector3(1 / Dt, 0, 0)).Fill(0, -5, -5, 0, 4, 4);
        var body = new Vector3(1.69f, 0.5f, 0.5f);

        var result = RiderCollision.Step(body, Vector3.Zero, Dt, world, [ship], riding: ship);

        Assert.False(ship.Holds(result.Center));
        Assert.True(result.Center.X > 2, $"{result.Center}");
    }

    // (b)
    [Fact]
    public void A_rider_carried_into_rock_is_pushed_out_and_only_the_velocity_into_the_rock_changes()
    {
        var world = new Grid { Infinite = true }.Fill(10, -5, -5, 12, 4, 4);
        // The ridden ship (a floor under the body) moves +x at 5 m/s and has carried the body 0.3 m into the rock.
        var ship = new Grid(velocity: new Vector3(5, 0, 0)).Fill(5, -1, -2, 9, -1, 2);
        var body = new Vector3(10 - RiderCollision.BodyRadius + 0.3f, 0.5f, 0.5f);
        var velocity = new Vector3(0, 1, 0.5f);
        Assert.True(world.Holds(body));

        var result = RiderCollision.Step(body, velocity, 1 / 60f, world, [ship], riding: ship);

        Assert.False(world.Holds(result.Center));
        Assert.False(ship.Holds(result.Center));
        Assert.False(result.FreedFromRock);
        Assert.Equal(velocity.Y, result.Velocity.Y, 5);
        Assert.Equal(velocity.Z, result.Velocity.Z, 5);
        // The body was not moving into the rock relative to its ship, so it keeps its relative velocity: the rock does
        // not hand it the ship's speed.
        Assert.Equal(0, result.Velocity.X, 5);
    }

    [Fact]
    public void A_rider_pushed_out_of_rock_is_not_thrown_back_once_the_ship_stops()
    {
        var world = new Grid { Infinite = true }.Fill(10, -5, -5, 12, 4, 4);
        // The ridden ship: a floor and, 3 m behind the body, the back wall of its bay. It scrapes the rock at 15 m/s.
        var moving = new Grid(velocity: new Vector3(15, 0, 0)).Fill(4, -1, -2, 9, -1, 2).Fill(5, 0, -2, 5, 3, 2);
        var body = new Vector3(10 - RiderCollision.BodyRadius + 0.3f, 0.5f, 0.5f);
        var result = RiderCollision.Step(body, Vector3.Zero, 1 / 60f, world, [moving], riding: moving);
        Assert.False(world.Holds(result.Center));
        Assert.Equal(Vector3.Zero, result.Impact);

        // The ship's own collision stops it; the rider, with nothing pushing them, stays where they are.
        var stopped = new Grid().Fill(4, -1, -2, 9, -1, 2).Fill(5, 0, -2, 5, 3, 2);
        for (int frame = 0; frame < 120; frame++)
        {
            result = RiderCollision.Step(result.Center, result.Velocity, 1 / 60f, world, [stopped], riding: stopped);
            Assert.Equal(Vector3.Zero, result.Impact);
        }
        Assert.Equal(Vector3.Zero, result.Velocity);
        Assert.True(result.Center.X > 9, $"thrown back to {result.Center}");
    }

    // (c)
    [Fact]
    public void A_diagonal_move_into_the_corner_of_a_turned_ship_and_a_world_wall_ends_in_neither()
    {
        var world = new Grid { Infinite = true }.Fill(10, -5, -12, 11, 4, 12);
        var turned = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        var ship = new Grid(new Vector3(6, 0, -3), turned).Fill(0, -5, -12, 0, 4, 11);
        var random = new Random(1234);
        int tried = 0, hit = 0;
        for (int i = 0; i < 2000; i++)
        {
            var start = new Vector3(6 + 4 * random.NextSingle(), 0.5f, -8 + 9 * random.NextSingle());
            if (!FreeEverywhere(start, world, [ship])) continue;
            var velocity = new Vector3(5 + 25 * random.NextSingle(), 4 * random.NextSingle() - 2, 5 + 25 * random.NextSingle());
            tried++;

            var result = RiderCollision.Step(start, velocity, 0.1f, world, [ship]);

            Assert.False(world.Holds(result.Center), $"in the wall: {start} + {velocity} -> {result.Center}");
            Assert.False(ship.Holds(result.Center), $"in the ship: {start} + {velocity} -> {result.Center}");
            if (result.Impact != Vector3.Zero) hit++;
        }
        Assert.True(tried > 200, $"only {tried} free starts");
        Assert.True(hit > 100, $"only {hit} moves hit something");
    }

    // (d)
    [Fact]
    public void Ten_thousand_random_moves_that_start_free_end_free_in_every_grid()
    {
        var random = new Random(24_09_2004);
        int checkedCases = 0;
        for (int i = 0; i < 10_000; i++)
        {
            var world = RandomCells(new Grid { Infinite = true }, random, 0.12);
            var ships = new List<Grid>();
            int shipCount = random.Next(0, 3);
            for (int s = 0; s < shipCount; s++)
            {
                var axis = Vector3.Normalize(new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f) + new Vector3(1e-3f));
                var turn = Quaternion.CreateFromAxisAngle(axis, random.NextSingle() * MathF.Tau);
                var position = new Vector3(random.NextSingle() * 8 - 4, random.NextSingle() * 8 - 4, random.NextSingle() * 8 - 4);
                var velocity = new Vector3(random.NextSingle() * 20 - 10, random.NextSingle() * 20 - 10, random.NextSingle() * 20 - 10);
                ships.Add(RandomCells(new Grid(position, turn, velocity), random, 0.15));
            }

            Vector3 start = default;
            bool free = false;
            for (int attempt = 0; attempt < 20 && !free; attempt++)
            {
                start = new Vector3(random.NextSingle() * 10 - 5, random.NextSingle() * 10 - 5, random.NextSingle() * 10 - 5);
                free = FreeEverywhere(start, world, ships);
            }
            if (!free) continue;
            checkedCases++;

            var move = new Vector3(random.NextSingle() * 40 - 20, random.NextSingle() * 40 - 20, random.NextSingle() * 40 - 20);
            float dt = random.NextSingle() * 0.1f;
            IGridShape? riding = ships.Count > 0 && random.Next(2) == 0 ? ships[random.Next(ships.Count)] : null;

            var result = RiderCollision.Step(start, move, dt, world, ships, riding);

            Assert.True(float.IsFinite(result.Center.X) && float.IsFinite(result.Center.Y) && float.IsFinite(result.Center.Z));
            Assert.True(FreeEverywhere(result.Center, world, ships),
                $"case {i}: {start} + {move} × {dt} -> {result.Center} overlaps a grid");
        }
        Assert.True(checkedCases > 9_000, $"only {checkedCases} cases started free");
    }

    static Grid RandomCells(Grid grid, Random random, double density)
    {
        for (int z = -3; z < 3; z++)
        for (int y = -3; y < 3; y++)
        for (int x = -3; x < 3; x++)
            if (random.NextDouble() < density) grid.Fill(x, y, z, x, y, z);
        return grid;
    }

    // (e)
    [Fact]
    public void Riding_with_no_overlap_moves_exactly_as_the_plain_sweep()
    {
        var world = new Grid { Infinite = true }.Fill(-5, -1, -5, 5, -1, 5);
        // The ridden ship is far off: it carried the body, but none of its blocks are near.
        var ship = new Grid(new Vector3(100, 0, 0), velocity: new Vector3(3, 0, 0)).Fill(0, 0, 0, 2, 2, 2);
        var body = new Vector3(0.5f, 0.6f, 0.5f);
        var velocity = new Vector3(1, -2, 0.5f);
        var expected = VoxelCollider.Move(body, RiderCollision.HalfExtents, velocity * 0.1f, world.Solid);

        var result = RiderCollision.Step(body, velocity, 0.1f, world, [ship], riding: ship);

        AssertClose(expected.Position, result.Center);
        Assert.True(expected.HitY);
        Assert.Equal(new Vector3(1, 0, 0.5f), result.Velocity);
        Assert.Equal(new Vector3(0, -2, 0), result.Impact);
        Assert.False(result.FreedFromRock);
    }

    [Fact]
    public void Walking_on_the_ridden_ships_own_floor_moves_exactly_as_the_plain_sweep()
    {
        var world = EmptyWorld();
        var ship = new Grid(velocity: new Vector3(0, 0, 7)).Fill(-5, -1, -5, 5, -1, 5);
        var body = new Vector3(0.5f, 0.6f, 0.5f);
        var velocity = new Vector3(1, -2, 0.5f);
        var expected = VoxelCollider.Move(body, RiderCollision.HalfExtents, velocity * 0.1f, ship.Solid);

        var result = RiderCollision.Step(body, velocity, 0.1f, world, [ship], riding: ship);

        AssertClose(expected.Position, result.Center);
        Assert.Equal(new Vector3(1, 0, 0.5f), result.Velocity);
    }

    /// <summary>Equal but for float rounding (the sweep's delta is the end minus the start).</summary>
    static void AssertClose(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"expected {expected}, got {actual}");

    [Fact]
    public void A_ship_ramming_a_free_player_shoves_them_at_its_speed()
    {
        var world = EmptyWorld();
        var ship = new Grid(new Vector3(1, 0, 0), velocity: new Vector3(6, 0, 0)).Fill(0, -5, -5, 0, 4, 4);
        var body = new Vector3(2.3f, 0.5f, 0.5f);
        Assert.True(ship.Holds(body));

        var result = RiderCollision.Step(body, new Vector3(-1, 0.5f, 0), Dt, world, [ship]);

        Assert.False(ship.Holds(result.Center));
        Assert.True(result.Center.X > 2);
        Assert.Equal(6, result.Velocity.X, 4);
        Assert.Equal(0.5f, result.Velocity.Y, 5);
    }

    [Fact]
    public void A_player_caught_between_a_ship_and_rock_is_never_pushed_into_the_rock()
    {
        // Rock at x >= 3; a ship wall moving +x has come to overlap the body, which has no room ahead of it.
        var world = new Grid { Infinite = true }.Fill(3, -5, -5, 5, 4, 4);
        var ship = new Grid(new Vector3(1, 0, 0), velocity: new Vector3(6, 0, 0)).Fill(0, -5, -5, 0, 4, 4);
        var body = new Vector3(2.3f, 0.5f, 0.5f);
        Assert.True(ship.Holds(body));
        Assert.False(world.Holds(body));

        var result = RiderCollision.Step(body, Vector3.Zero, Dt, world, [ship]);

        Assert.False(world.Holds(result.Center));
    }

    [Fact]
    public void A_body_deeper_in_a_ship_than_the_limit_is_left_to_climb_out()
    {
        var world = EmptyWorld();
        var ship = new Grid().Fill(-4, -4, -4, 4, 4, 4);
        var body = new Vector3(0.5f, 0.5f, 0.5f);

        var result = RiderCollision.Step(body, Vector3.Zero, Dt, world, [ship], riding: ship);

        Assert.Equal(body, result.Center);
        Assert.False(result.FreedFromRock);
    }

    [Fact]
    public void With_the_limit_at_zero_nothing_is_pushed()
    {
        var world = EmptyWorld();
        var ship = new Grid(new Vector3(1, 0, 0), velocity: new Vector3(6, 0, 0)).Fill(0, -5, -5, 0, 4, 4);
        var body = new Vector3(2.3f, 0.5f, 0.5f);

        var result = RiderCollision.Step(body, Vector3.Zero, Dt, world, [ship], maxPush: 0);

        Assert.Equal(body, result.Center);
    }

    [Fact]
    public void With_the_limit_at_zero_a_body_grazing_rock_is_not_moved_either()
    {
        var world = new Grid { Infinite = true }.Fill(-5, -5, -5, 5, -1, 5);
        var body = new Vector3(0.5f, 0.45f, 0.5f);
        Assert.True(world.Holds(body));

        var result = RiderCollision.Step(body, Vector3.Zero, Dt, world, [], maxPush: 0);

        Assert.Equal(body, result.Center);
        Assert.False(result.FreedFromRock);
    }

    [Fact]
    public void A_move_that_only_enters_a_corner_as_a_whole_keeps_its_slide_and_counts_only_the_speed_into_the_face()
    {
        // One block at [0,1]³. A move of (10, 0, 1) × 0.12 s crosses its -x and -z faces' planes at the same moment, so
        // neither axis alone takes the body in; it went in 0.2 m along x but only 0.02 m along z.
        var world = new Grid { Infinite = true }.Fill(0, 0, 0, 0, 0, 0);
        var start = new Vector3(-1.489f, 0.5f, -0.589f);
        var velocity = new Vector3(10, 0, 1);
        var end = start + velocity * 0.12f;
        Assert.False(world.Holds(start));
        Assert.True(world.Holds(end));

        var after = velocity;
        var center = RiderCollision.Recheck(start, end, ref after, world, []);

        Assert.False(world.Holds(center));
        Assert.Equal(10, after.X, 5);
        Assert.Equal(0, after.Z, 5);
        Assert.Equal(1, (velocity - after).Length(), 4);
    }

    [Fact]
    public void A_body_deep_in_rock_is_moved_to_the_nearest_free_spot_as_a_last_resort()
    {
        var world = new Grid { Infinite = true }.Fill(-4, -4, -4, 4, 4, 4);
        var body = new Vector3(0.5f, 0.5f, 0.5f);

        var result = RiderCollision.Step(body, Vector3.Zero, Dt, world, []);

        Assert.True(result.FreedFromRock);
        Assert.False(world.Holds(result.Center));
    }

    [Fact]
    public void A_shallow_overlap_with_rock_takes_the_shortest_way_out()
    {
        var world = new Grid { Infinite = true }.Fill(-5, -5, -5, 5, -1, 5);
        var body = new Vector3(0.5f, 0.4f, 0.5f);
        Assert.True(world.Holds(body));

        var result = RiderCollision.Step(body, new Vector3(0, -3, 0), Dt, world, []);

        Assert.False(result.FreedFromRock);
        Assert.False(world.Holds(result.Center));
        Assert.InRange(result.Center.Y, RiderCollision.BodyRadius, 0.55f);
        Assert.Equal(0, result.Velocity.Y);
    }

    [Fact]
    public void Blocked_sees_the_world_and_every_ship_but_the_one_excepted()
    {
        var world = new Grid { Infinite = true }.Fill(10, 0, 0, 10, 0, 0);
        var ship = new Grid(new Vector3(0, 0, 0)).Fill(0, 0, 0, 0, 0, 0);
        IReadOnlyList<IGridShape> ships = [ship];

        Assert.True(RiderCollision.Blocked(new Vector3(0.5f, 0.5f, 0.5f), world, ships));
        Assert.False(RiderCollision.Blocked(new Vector3(0.5f, 0.5f, 0.5f), world, ships, except: ship));
        Assert.True(RiderCollision.Blocked(new Vector3(10.5f, 0.5f, 0.5f), world, ships));
        Assert.False(RiderCollision.Blocked(new Vector3(5.5f, 0.5f, 0.5f), world, ships));
    }
}
