using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Physics;
using OpenCorneroids.Simulation.Settings;
using OpenCorneroids.Simulation.Tests.Items.Support;

namespace OpenCorneroids.Simulation.Tests.Items;

public class TorchesTests
{
    const int TorchId = 60;
    const double Frame = 1.0 / 60;
    static readonly Rgb8 Blue = new(128, 128, 255);

    /// <summary>A made-up torch: power 25, burning 20 s, a blue light of radius 8.</summary>
    static ItemDef Torch(int powerUsage = 25, int burningMs = 20_000) =>
        new(TorchId, "torch", "torches", "Test torch", "", new Rgb8(0, 0, 0), 64, 1, powerUsage, new CellRect(0, 0, 1, 1),
            null, [new TorchComponent(burningMs), new LightComponent(new LightDef("point", Blue, 8))],
            new Dictionary<string, string>(), null);

    static ItemDef Plain() => TestItems.Item(TestItems.Plate, "ingredient", 10);

    static Torches.Tuning Original => Torches.Tuning.Original;

    /// <summary>The torch flies freely (nothing to hit) at <paramref name="frame"/> s per step for <paramref name="seconds"/>.</summary>
    static ThrownTorch Fly(ThrownTorch torch, double seconds, double frame, Torches.Tuning tuning)
    {
        for (double t = 0; t < seconds - 1e-9; t += frame)
        {
            var (delta, _) = Torches.Step(ref torch, Math.Min(frame, seconds - t), tuning);
            torch.Position += delta;
        }
        return torch;
    }

    static ThrownTorch Thrown(Vector3 forward, Vector3 carrier = default, Torches.Tuning? tuning = null) =>
        Torches.Throw(Torch(), Vector3.Zero, forward, carrier, tuning ?? Original)!.Value;

    [Fact]
    public void The_original_tuning_is_the_settings_defaults()
    {
        Assert.Equal(Torches.Tuning.Original, Torches.Tuning.From(new CvarStore()));
        Assert.Equal(new Torches.Tuning(7, 5, 0.2f, 1, 0.1, false, 0), Torches.Tuning.Original);
    }

    [Fact]
    public void A_torch_is_thrown_along_the_view_at_seven_metres_a_second_without_the_throwers_velocity()
    {
        var torch = Thrown(new Vector3(0, 0, -2), carrier: new Vector3(3, 0, 0));

        Assert.Equal(new Vector3(0, 0, -7), torch.Velocity);
        Assert.Equal(20_000, torch.RemainingMs);
        Assert.Equal(Blue, torch.Color);
        Assert.Equal(TorchId, torch.ItemId);
    }

    [Fact]
    public void Ours_the_throwers_velocity_is_added_when_the_setting_is_on()
    {
        var torch = Thrown(new Vector3(0, 0, -1), carrier: new Vector3(3, 0, 0),
            tuning: Original with { InheritVelocity = true });

        Assert.Equal(new Vector3(3, 0, -7), torch.Velocity);
    }

    [Fact]
    public void Only_a_torch_is_thrown()
    {
        Assert.Null(Torches.Throw(Plain(), Vector3.Zero, Vector3.UnitX, Vector3.Zero, Original));
        Assert.False(Torches.IsTorch(Plain()));
        Assert.True(Torches.IsTorch(Torch()));
    }

    [Fact]
    public void A_throw_needs_the_torchs_power_use_and_takes_one_torch_and_the_power()
    {
        var torch = Torch();
        var inventory = TestItems.NewInventory();
        inventory.SetSlot(0, new ItemStack(TorchId, 2));
        var suit = new Suit(TestItems.Set());

        suit.SetPowerLevel(24);
        Assert.Equal(Torches.Refusal.NoPower, Torches.TryTake(torch, inventory, 0, suit));
        Assert.Equal(2, inventory.Slots[0]?.Quantity);
        Assert.Equal(24, suit.Power);

        suit.SetPowerLevel(25);
        Assert.Equal(Torches.Refusal.None, Torches.TryTake(torch, inventory, 0, suit));
        Assert.Equal(1, inventory.Slots[0]?.Quantity);
        Assert.Equal(0, suit.Power);

        suit.SetPowerLevel(100);
        Assert.Equal(Torches.Refusal.None, Torches.TryTake(torch, inventory, 0, suit));
        Assert.Null(inventory.Slots[0]);   // the last torch empties the slot
        Assert.Equal(75, suit.Power);
        Assert.Equal(Torches.Refusal.NoTorch, Torches.TryTake(torch, inventory, 0, suit));
    }

    [Fact]
    public void A_negative_power_use_always_throws_and_gives_power_as_a_weapon_does()
    {
        var torch = Torch(powerUsage: -25);
        var inventory = TestItems.NewInventory();
        inventory.SetSlot(0, new ItemStack(TorchId, 2));
        var suit = new Suit(TestItems.Set());
        suit.SetPowerLevel(0);

        Assert.Equal(-25, Torches.Cost(torch));
        Assert.Equal(Torches.Refusal.None, Torches.TryTake(torch, inventory, 0, suit));
        Assert.Equal(25, suit.Power);
        Assert.Equal(1, inventory.Slots[0]?.Quantity);
    }

    [Fact]
    public void Ours_with_negative_power_off_a_negative_power_use_costs_its_size()
    {
        var torch = Torch(powerUsage: -25);
        var inventory = TestItems.NewInventory();
        inventory.SetSlot(0, new ItemStack(TorchId, 2));
        var suit = new Suit(TestItems.Set());

        Assert.Equal(25, Torches.Cost(torch, negativeGivesPower: false));
        suit.SetPowerLevel(24);
        Assert.Equal(Torches.Refusal.NoPower, Torches.TryTake(torch, inventory, 0, suit, negativeGivesPower: false));
        suit.SetPowerLevel(30);
        Assert.Equal(Torches.Refusal.None, Torches.TryTake(torch, inventory, 0, suit, negativeGivesPower: false));
        Assert.Equal(5, suit.Power);
    }

    [Fact]
    public void Four_throws_empty_a_full_suit()
    {
        var torch = Torch();
        var inventory = TestItems.NewInventory();
        inventory.SetSlot(0, new ItemStack(TorchId, 8));
        var suit = new Suit(TestItems.Set());

        int thrown = 0;
        while (Torches.TryTake(torch, inventory, 0, suit) == Torches.Refusal.None) thrown++;

        Assert.Equal(4, thrown);
        Assert.Equal(4, inventory.Slots[0]?.Quantity);
    }

    [Fact]
    public void A_torch_in_the_wrong_slot_or_another_item_is_not_taken()
    {
        var inventory = TestItems.NewInventory();
        inventory.SetSlot(0, new ItemStack(TestItems.Plate, 3));
        var suit = new Suit(TestItems.Set());

        Assert.Equal(Torches.Refusal.NoTorch, Torches.TryTake(Torch(), inventory, 0, suit));
        Assert.Equal(Torches.Refusal.NoTorch, Torches.TryTake(Torch(), inventory, -1, suit));
        Assert.Equal(Torches.Refusal.NoTorch, Torches.TryTake(Torch(), inventory, 999, suit));
        Assert.Equal(100, suit.Power);
    }

    [Fact]
    public void A_thrown_torch_slows_to_a_stop_about_four_point_nine_metres_away()
    {
        var torch = Fly(Thrown(Vector3.UnitX), 1.39, Frame, Original);
        Assert.NotEqual(Vector3.Zero, torch.Velocity);

        torch = Fly(torch, 0.03, Frame, Original);
        Assert.Equal(Vector3.Zero, torch.Velocity);
        Assert.InRange(torch.Position.X, 4.85f, 4.95f);

        var later = Fly(torch, 5, Frame, Original);
        Assert.Equal(torch.Position, later.Position);   // stopped, never turning back
    }

    [Theory]
    [InlineData(1.0 / 15)]
    [InlineData(1.0 / 144)]
    [InlineData(0.5)]
    public void The_path_is_the_same_at_any_frame_rate(double frame)
    {
        var reference = Fly(Thrown(Vector3.UnitY), 3, Frame, Original);
        var torch = Fly(Thrown(Vector3.UnitY), 3, frame, Original);

        Assert.Equal(reference.Position.Y, torch.Position.Y, 3);
        Assert.Equal(7 * 7 / (2 * 5f), torch.Position.Y, 3);
    }

    [Fact]
    public void A_torch_burns_for_its_burning_time_times_the_lifetime_scale()
    {
        var torch = Thrown(Vector3.UnitX);
        Assert.True(Torches.Step(ref torch, 19.99, Original).Burning);
        Assert.False(Torches.Step(ref torch, 0.01, Original).Burning);

        var longer = Thrown(Vector3.UnitX, tuning: Original with { LifetimeScale = 2 });
        Assert.Equal(40_000, longer.RemainingMs);
        Assert.True(Torches.Step(ref longer, 39.99, Original).Burning);
        Assert.False(Torches.Step(ref longer, 0.01, Original).Burning);
    }

    [Fact]
    public void The_light_radius_is_ln_of_the_milliseconds_left_with_up_to_ten_percent_flicker()
    {
        Assert.Equal(9.903, Torches.LightRadius(20_000, 0, 0.1), 3);
        Assert.Equal(6.908, Torches.LightRadius(1_000, 0, 0.1), 3);
        Assert.Equal(9.903 * 0.9, Torches.LightRadius(20_000, 1, 0.1), 3);
        Assert.Equal(9.903 * 0.95, Torches.LightRadius(20_000, 0.5, 0.1), 3);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void The_light_radius_is_never_negative_or_nan(double remainingMs)
    {
        Assert.Equal(0, Torches.LightRadius(remainingMs, 0.3, 0.1));
    }

    /// <summary>A block grid at a pose, optionally turned and moving; anything not filled is empty.</summary>
    sealed class Grid : IGridShape
    {
        readonly HashSet<(int, int, int)> _solid = new();
        readonly Vector3 _velocity;

        public Grid(Vector3 position = default, bool infinite = false, Quaternion? rotation = null, Vector3 velocity = default)
        {
            ToWorld = Matrix4x4.CreateFromQuaternion(rotation ?? Quaternion.Identity) * Matrix4x4.CreateTranslation(position);
            Matrix4x4.Invert(ToWorld, out var toLocal);
            ToLocal = toLocal;
            Solid = (x, y, z) => _solid.Contains((x, y, z));
            Extent = infinite ? (Vector3.Zero, float.PositiveInfinity) : (position, 100);
            _velocity = velocity;
        }

        public Matrix4x4 ToWorld { get; }
        public Matrix4x4 ToLocal { get; }
        public Func<int, int, int, bool> Solid { get; }
        public (Vector3 Center, float Radius) Extent { get; }
        public Vector3 PointVelocity(Vector3 world) => _velocity;

        public Grid Fill(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                _solid.Add((x, y, z));
            return this;
        }
    }

    /// <summary>Flies the torch through <paramref name="world"/> and <paramref name="ships"/> for some seconds.</summary>
    static ThrownTorch FlyThrough(ThrownTorch torch, double seconds, IGridShape world, params IGridShape[] ships)
    {
        for (double t = 0; t < seconds - 1e-9; t += Frame)
        {
            var (delta, _) = Torches.Step(ref torch, Frame, Original);
            Torches.Collide(ref torch, delta, world, ships, Original.Radius);
        }
        return torch;
    }

    [Fact]
    public void A_torch_thrown_at_a_wall_stops_against_it_without_bouncing()
    {
        // A wall whose face is at x = 3, closer than where the torch would stop.
        var world = new Grid(infinite: true).Fill(3, -5, -5, 3, 5, 5);
        var torch = Thrown(Vector3.UnitX) with { Position = new Vector3(0, 0.5f, 0.5f) };

        for (int i = 0; i < 120; i++)
        {
            var (delta, _) = Torches.Step(ref torch, Frame, Original);
            Torches.Collide(ref torch, delta, world, [], Original.Radius);
            Assert.True(torch.Velocity.X >= 0, "a torch never bounces back");
            Assert.True(torch.Position.X <= 3 - 0.2f + 1e-3f, "a torch never enters the wall");
        }

        Assert.Equal(3 - 0.2f, torch.Position.X, 2);
        Assert.Equal(Vector3.Zero, torch.Velocity);
    }

    [Fact]
    public void A_torch_thrown_slantwise_at_a_wall_slides_along_it()
    {
        var world = new Grid(infinite: true).Fill(1, -20, -20, 1, 20, 20);
        var torch = Thrown(Vector3.Normalize(new Vector3(1, 0, 1))) with { Position = new Vector3(0.5f, 0.5f, 0.5f) };

        var (delta, _) = Torches.Step(ref torch, 0.1, Original);
        Torches.Collide(ref torch, delta, world, [], Original.Radius);

        Assert.Equal(0, torch.Velocity.X);   // the part into the wall is gone
        Assert.True(torch.Velocity.Z > 4.5f, "the part along the wall is kept");
        Assert.Equal(1 - 0.2f, torch.Position.X, 3);

        torch = FlyThrough(torch, 2, world);
        Assert.Equal(1 - 0.2f, torch.Position.X, 3);
        Assert.True(torch.Position.Z > 3, "it slid on along the wall");
    }

    [Fact]
    public void A_ship_that_comes_to_overlap_a_torch_pushes_it_out_without_carrying_it()
    {
        var world = new Grid(infinite: true);
        // The ship's block spans x 0.9..1.9: it overlaps a torch at x = 0.8 by 0.1.
        var ship = new Grid(new Vector3(-0.1f, 0, 0)).Fill(1, 0, 0, 1, 0, 0);
        var torch = new ThrownTorch(TorchId, new Vector3(0.8f, 0.5f, 0.5f), Vector3.Zero, 1000, Blue);

        Torches.Collide(ref torch, Vector3.Zero, world, [ship], Original.Radius);

        Assert.True(torch.Position.X <= 0.7f + 1e-3f, $"pushed out to {torch.Position.X}");
        Assert.Equal(Vector3.Zero, torch.Velocity);   // not given the ship's motion
    }

    [Fact]
    public void A_moving_ship_pushes_a_resting_torch_out_but_does_not_give_it_its_velocity()
    {
        var world = new Grid(infinite: true);
        // Moving at 3 m/s towards -x, the ship's block (x 0.9..1.9) has come to overlap the torch at x = 0.8.
        var ship = new Grid(new Vector3(-0.1f, 0, 0), velocity: new Vector3(-3, 0, 0)).Fill(1, 0, 0, 1, 0, 0);
        var torch = new ThrownTorch(TorchId, new Vector3(0.8f, 0.5f, 0.5f), Vector3.Zero, 1000, Blue);

        for (int i = 0; i < 10; i++)
        {
            var (delta, _) = Torches.Step(ref torch, Frame, Original);
            Torches.Collide(ref torch, delta, world, [ship], Original.Radius);
        }

        Assert.True(torch.Position.X <= 0.7f + 1e-3f, $"pushed out to {torch.Position.X}");
        Assert.Equal(Vector3.Zero, torch.Velocity);
    }

    [Fact]
    public void A_ship_pressing_a_torch_against_rock_does_not_push_it_into_the_rock()
    {
        // Rock fills x 0..1; the ship's block (x 1.15..2.15) overlaps a torch at x 1.25 lying next to the rock: the
        // shortest way out of the ship (-x) would put it in the rock.
        var world = new Grid(infinite: true).Fill(0, -5, -5, 0, 5, 5);
        var ship = new Grid(new Vector3(0.15f, 0, 0)).Fill(1, 0, 0, 1, 0, 0);
        var torch = new ThrownTorch(TorchId, new Vector3(1.25f, 0.5f, 0.5f), Vector3.Zero, 1000, Blue);

        Torches.Collide(ref torch, Vector3.Zero, world, [ship], Original.Radius);

        var inRock = Vector3.Transform(torch.Position, world.ToLocal);
        Assert.False(VoxelCollider.Overlaps(inRock, new Vector3(Original.Radius), world.Solid),
            $"pushed into the rock, to {torch.Position}");
        var inShip = Vector3.Transform(torch.Position, ship.ToLocal);
        Assert.False(VoxelCollider.Overlaps(inShip, new Vector3(Original.Radius), ship.Solid),
            $"left inside the ship, at {torch.Position}");
    }

    [Fact]
    public void A_torch_slides_along_the_face_of_a_turned_ship()
    {
        // A ship turned 90 degrees about y: its local +x points along world -z. Its wall at local x = 1 (local x 1..2)
        // is a wall across world z, its face at world z = -1.
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var world = new Grid(infinite: true);
        var ship = new Grid(rotation: turn).Fill(1, -20, -20, 1, 20, 20);
        Assert.Equal(-1, Vector3.Transform(new Vector3(1, 0, 0), ship.ToWorld).Z, 4);

        var torch = Thrown(Vector3.Normalize(new Vector3(1, 0, -1))) with { Position = new Vector3(0.5f, 0.5f, -0.5f) };
        torch = FlyThrough(torch, 2, world, ship);

        Assert.Equal(-1 + 0.2f, torch.Position.Z, 3);   // stopped at the turned face
        Assert.True(torch.Position.X > 2.5f, $"it slid along the face, to x = {torch.Position.X}");
        Assert.Equal(0, torch.Velocity.Z, 4);
    }

    [Fact]
    public void Over_the_limit_the_oldest_torches_go_out_and_zero_means_no_limit()
    {
        Assert.Equal(1, Torches.OverLimit(3, 2));
        Assert.Equal(0, Torches.OverLimit(2, 2));
        Assert.Equal(0, Torches.OverLimit(5000, 0));
        Assert.Equal(0, Torches.OverLimit(0, 3));
    }
}
