using System.Numerics;
using OpenCorneroids.Simulation.Creatures;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Creatures;

public class CreatureTests
{
    /// <summary>One grid (id 0) at the world origin, unturned: a slab of food blocks at y &lt; 0.</summary>
    sealed class World : ICreatureWorld
    {
        public readonly Dictionary<Int3, int> Blocks = new();
        public readonly List<Creature> Added = [];
        public readonly List<(Vector3 From, Vector3 Dir)> Bolts = [];
        public readonly List<CreatureEffect> Effects = [];
        public readonly List<Int3> Eaten = [];
        public Random Random { get; } = new(7);
        public Vector3? Player { get; set; }
        public bool PlayerPiloting { get; set; }

        public World(int size = 8)
        {
            for (int x = -size; x < size; x++)
            for (int y = -size; y < 0; y++)
            for (int z = -size; z < size; z++)
                Blocks[new Int3(x, y, z)] = 1;
        }

        public bool GridExists(int grid) => grid == 0;
        public bool IsSolid(int grid, Int3 cell) => Blocks.ContainsKey(cell);
        public bool IsFood(int grid, Int3 cell) => Blocks.TryGetValue(cell, out var b) && b == 1;
        public int BlockAt(int grid, Int3 cell) => Blocks.GetValueOrDefault(cell);
        public void EatBlock(int grid, Int3 cell) { Blocks.Remove(cell); Eaten.Add(cell); }
        public Vector3 ToWorld(int grid, Vector3 local) => local;
        public Vector3 DirectionToWorld(int grid, Vector3 local) => local;
        public Vector3 ToLocal(int grid, Vector3 world) => world;
        public void Add(Creature creature) => Added.Add(creature);
        public void FireBolt(Vector3 from, Vector3 direction) => Bolts.Add((from, direction));
        public void Emit(CreatureEffect effect, Vector3 world, Vector3 velocity) => Effects.Add(effect);
    }

    static void Run(Creature c, ICreatureWorld w, double seconds, double dt = 1 / 60.0)
    {
        for (double t = 0; t < seconds; t += dt) c.Update(dt, w);
    }

    static readonly Int3 Cell = new(0, 0, 0), Floor = new(0, -1, 0);

    [Fact]
    public void A_parasite_eats_its_block_in_twenty_seconds_and_drops_nothing()
    {
        var w = new World();
        var p = new MineralParasite(0, Cell, Floor, w);
        Run(p, w, 19.5);
        Assert.Empty(w.Eaten);
        // The timer runs on frames it moves, not on the frames it thinks, so a little past 20 s.
        Run(p, w, 2);
        Assert.Equal([Floor], w.Eaten);
        Assert.Equal(0, p.TouchDamage);   // harmless until it attacks
    }

    [Fact]
    public void After_eating_a_parasite_crawls_to_more_food()
    {
        var w = new World();
        var p = new MineralParasite(0, Cell, Floor, w, eatSeconds: 0.5);
        Run(p, w, 8);
        Assert.True(w.Eaten.Count >= 2, $"ate {w.Eaten.Count}");
        Assert.True(p.Position.Y < 0.6f);   // working its way down into the rock
    }

    [Fact]
    public void A_player_within_four_makes_a_parasite_charge_and_bite()
    {
        var w = new World { Player = new Vector3(0.5f, 0.5f, 3.5f) };
        var p = new MineralParasite(0, Cell, Floor, w);
        Run(p, w, 0.6);
        Assert.True(p.Attacking);
        Assert.Equal(MineralParasite.Bite, p.TouchDamage);
        Run(p, w, 0.6);
        Assert.True(p.Velocity.Z > 3, $"velocity {p.Velocity}");
        Assert.True(p.Velocity.Length() < 5.5f);
    }

    [Fact]
    public void A_shot_from_close_by_angers_a_parasite_but_one_from_far_does_not()
    {
        var w = new World { Player = new Vector3(0, 0, 10) };
        var p = new MineralParasite(0, Cell, Floor, w);
        Assert.Equal(5, p.Absorb(5, byPlayer: true, shooterDistance: 30, w));
        Assert.False(p.Attacking);
        p.Absorb(5, byPlayer: true, shooterDistance: 10, w);
        Assert.True(p.Attacking);
        Assert.Equal(10, p.Health);
    }

    [Fact]
    public void A_kill_absorbs_only_the_health_left_and_shrinks_away()
    {
        var w = new World();
        var p = new MineralParasite(0, Cell, Floor, w);
        Assert.Equal(20, p.Absorb(55, byPlayer: true, shooterDistance: 1, w));
        Assert.True(p.Removing);
        Assert.Equal(1, p.Scale);
        p.Update(0.1, w);
        Assert.Equal(0.5f, p.Scale, 3);
        p.Update(0.11, w);
        Assert.True(p.Gone);
    }

    [Fact]
    public void Health_comes_back_a_point_every_two_seconds()
    {
        var w = new World { Player = null };
        var p = new MineralParasite(0, Cell, Floor, w);
        p.Absorb(6, byPlayer: false, shooterDistance: float.PositiveInfinity, w);
        Run(p, w, 4.05);
        Assert.Equal(16, p.Health);
    }

    [Fact]
    public void An_egg_hatches_three_needles_a_second_apart_then_goes()
    {
        var w = new World { Player = new Vector3(0.5f, 3, 0.5f) };
        var egg = new SwarmEgg(0, Cell, Floor);
        Run(egg, w, 0.6);
        Assert.True(egg.Hatching);
        Run(egg, w, 1.2);
        Assert.Single(w.Added);
        Run(egg, w, 2.2);
        Assert.Equal(3, w.Added.Count);
        Assert.Contains(CreatureEffect.Goo, w.Effects);
        Assert.All(w.Added, n => Assert.Equal(SwarmEgg.NeedleSpeed, n.Velocity.Length(), 3));
        Assert.All(w.Added, n => Assert.True(n.Velocity.Y > 0));   // out along the surface normal
        Run(egg, w, 2);
        Assert.True(egg.Removing);
    }

    [Fact]
    public void A_shot_egg_hatches_at_once_but_without_an_owner_its_needles_give_up()
    {
        var w = new World { Player = new Vector3(0, 0, 40) };
        var egg = new SwarmEgg(0, Cell, Floor);
        egg.Absorb(1, byPlayer: false, shooterDistance: float.PositiveInfinity, w);
        Assert.True(egg.Hatching);
        Run(egg, w, 1.1);
        var needle = Assert.Single(w.Added);
        needle.Update(1 / 60.0, w);
        Assert.True(needle.Removing);
    }

    [Fact]
    public void A_needle_homes_on_the_player_and_expires_after_four_seconds()
    {
        var w = new World { Player = new Vector3(0, 30, 0) };
        var needle = new Needle(Vector3.Zero, new Vector3(7, 0, 0), target: true);
        Run(needle, w, 1);
        // Steering toward 7 u/s at the player takes about a second to turn a sideways throw.
        Assert.True(needle.Velocity.Y > 4 && needle.Velocity.X < 3, $"velocity {needle.Velocity}");
        Assert.Equal(Needle.Sting, needle.TouchDamage);
        Assert.Contains(CreatureEffect.Trail, w.Effects);
        Run(needle, w, 3.1);
        Assert.True(needle.Removing);
    }

    [Fact]
    public void A_scavenger_ignores_a_pilot_but_attacks_a_player_on_foot_and_fires_when_on_target()
    {
        var w = new World { Player = new Vector3(0, 0, 20), PlayerPiloting = true };
        var s = new Scavenger(Vector3.Zero, new Random(1));
        Run(s, w, 2);
        Assert.False(s.Attacking);
        w.PlayerPiloting = false;
        Run(s, w, 6);
        Assert.True(s.Attacking);
        Assert.NotEmpty(w.Bolts);
        var bolt = w.Bolts[0];
        Assert.True(Vector3.Dot(bolt.Dir, Vector3.Normalize(w.Player!.Value - bolt.From)) > MathF.Cos(MathF.PI / 8) - 0.01f);
    }

    [Fact]
    public void Turning_is_limited_to_the_given_angle()
    {
        var turned = Scavenger.TurnToward(Vector3.UnitZ, Vector3.UnitX, 0.1f);
        Assert.Equal(0.1f, MathF.Acos(Vector3.Dot(turned, Vector3.UnitZ)), 3);
        Assert.Equal(Vector3.UnitX, Scavenger.TurnToward(Vector3.UnitZ, Vector3.UnitX, 4f));
    }

    [Fact]
    public void Trial_points_lie_at_least_sixteen_from_the_player()
    {
        var player = new Vector3(5, 5, 5);
        for (int i = 0; i < 200; i++)
        {
            var r = new Random(i);
            var p = Spawning.TrialPoint(player, r.NextDouble(), r.NextDouble(), r.NextDouble());
            Assert.True(Vector3.Distance(p, player) >= Spawning.MinPlayerDistance - 1e-3f);
            Assert.True(Vector3.Distance(p, player) <= Spawning.TrialHalfSize * MathF.Sqrt(3) + 2.1f);
        }
    }

    [Fact]
    public void The_surface_is_found_by_walking_out_of_the_rock()
    {
        var w = new World();
        bool Solid(Int3 c) => w.IsSolid(0, c);
        bool Food(Int3 c) => w.IsFood(0, c);
        // Two cells under the surface at y = 0, deep inside sideways: up (+Y) reaches it after two steps.
        var spot = Spawning.FindSurface(new Vector3(0.5f, -2.5f, 0.5f), Solid, Food);
        Assert.NotNull(spot);
        Assert.Equal(new Int3(0, 1, 0), spot!.Value.Normal);
        Assert.Equal(new Int3(0, 0, 0), spot.Value.Cell);
        Assert.Equal(new Int3(0, -1, 0), spot.Value.Floor);
        // Near the slab's +X edge, +X comes first in the original's order.
        Assert.Equal(new Int3(1, 0, 0), Spawning.FindSurface(new Vector3(6.5f, -2.5f, 0.5f), Solid, Food)!.Value.Normal);
        // Not in rock, or too deep: nothing.
        Assert.Null(Spawning.FindSurface(new Vector3(0.5f, 3.5f, 0.5f), Solid, Food));
        Assert.Null(Spawning.FindSurface(new Vector3(0.5f, -7.5f, 0.5f), c => w.IsSolid(0, c) || c.Y < 0, Food));
    }

    [Fact]
    public void Caps_touch_and_stagger_follow_the_original()
    {
        Assert.Equal(3, Spawning.ParasiteCap(3));
        Assert.Equal(0, Spawning.ParasiteCap(0));
        Assert.Equal(6, Spawning.ParasiteCap(3, 2));

        var touch = Spawning.Touch(new Vector3(0, 0, 0.6f), 0.49f, Vector3.Zero, 0.4f);
        Assert.NotNull(touch);
        Assert.Equal(0.29f, touch!.Value.Depth, 3);
        Assert.Equal(Vector3.UnitZ, touch.Value.Normal);
        Assert.Null(Spawning.Touch(new Vector3(0, 0, 2), 0.49f, Vector3.Zero, 0.4f));

        Assert.Equal(397, Spawning.StaggeredClock(1, 500));
        Assert.Equal(294, Spawning.StaggeredClock(2, 500));
    }

    [Fact]
    public void A_creature_on_a_vanished_grid_is_removed()
    {
        var w = new World();
        var p = new MineralParasite(5, Cell, Floor, w);
        p.Update(0.016, w);
        Assert.True(p.Removing);
    }
}
