using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Debugging;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Physics;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;

namespace OpenCorneroids.Simulation.Tests.Debugging;

public class DebugConsoleTests
{
    sealed class FakeConsoleHost : IDebugConsoleHost
    {
        public FakeConsoleHost(bool creative = false)
        {
            Itemset = Set();
            Inventory = Inventory.ForItemset(Itemset);
            Suit = new Suit(Itemset);
            Creative = creative;
        }

        public ItemsetDef Itemset { get; }
        public Inventory Inventory { get; }
        public Suit? Suit { get; set; }
        public bool Creative { get; private set; }
        public void SetCreative(bool creative) => Creative = creative;

        public Vector3 Position { get; set; } = new(64f, 71f, 110f);
        public Vector3 Velocity { get; set; } = new(2f, 0f, -3f);
        public Vector3 SpawnPosition { get; set; } = new(64f, 71f, 110f);

        public void Teleport(Vector3 position)
        {
            Position = position;
            Velocity = Vector3.Zero;
        }

        public float SpeedMultiplier { get; set; } = 1f;
        public bool Noclip { get; set; }
        public bool FlightAssist { get; set; }
        public bool FixedCamera { get; set; }
        public float Fov { get; set; } = DebugConsole.DefaultFov;

        public List<DebugShipSummary> ShipList { get; } =
        [
            new("Ship_1", 118, 364f, new Vector3(40f, 50f, 60f), 12.5f)
        ];

        public IReadOnlyList<DebugShipSummary> Ships => ShipList;

        public bool TryTeleportToShip(string? selector, out string message)
        {
            if (ShipList.Count == 0)
            {
                message = "No ships.";
                return false;
            }
            Teleport(ShipList[0].Position + new Vector3(0, 4, 0));
            message = $"Teleported beside {ShipList[0].Name}.";
            return true;
        }

        public string SpawnDemoShip()
        {
            ShipList.Add(new("Ship_2", 42, 120f, Position + new Vector3(0, 0, -10), 0f));
            return "Spawned Ship_2.";
        }

        public string LaunchTargetedOrNearestShip() => "Launched Ship_1.";

        public int ClearShips()
        {
            int n = ShipList.Count;
            ShipList.Clear();
            return n;
        }

        public string? SaveError { get; set; }
        public int SaveCount { get; private set; }
        public string? SaveWorld()
        {
            SaveCount++;
            return SaveError;
        }
    }

    [Fact]
    public void Mode_switches_between_creative_and_survival_and_toggles()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost(creative: false);

        var r1 = console.Execute("mode creative", host);
        Assert.False(r1.IsError);
        Assert.True(host.Creative);

        var r2 = console.Execute("survival", host);
        Assert.False(r2.IsError);
        Assert.False(host.Creative);

        var r3 = console.Execute("gm toggle", host);
        Assert.False(r3.IsError);
        Assert.True(host.Creative);
    }

    [Fact]
    public void Speed_sets_clamps_and_resets_multiplier_and_scales_astronaut_motion()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost();

        console.Execute("speed 3.5", host);
        Assert.Equal(3.5f, host.SpeedMultiplier);

        console.Execute("speed 999", host);
        Assert.Equal(DebugConsole.MaxSpeedMultiplier, host.SpeedMultiplier);

        console.Execute("speed reset", host);
        Assert.Equal(1f, host.SpeedMultiplier);

        var input = new AstronautMotion.Input(Vector3.UnitZ, Vector3.UnitZ, true, false);
        var normal = AstronautMotion.Step(Vector3.Zero, input, 1f, 1f);
        var fast = AstronautMotion.Step(Vector3.Zero, input, 1f, 3f);
        Assert.Equal(4f, normal.Length(), 3);
        Assert.Equal(12f, fast.Length(), 3);
    }

    [Fact]
    public void Teleport_supports_absolute_relative_spawn_center_and_ship_targets()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost { Position = new(10f, 20f, 30f) };

        console.Execute("tp 100 50 -25", host);
        Assert.Equal(new Vector3(100f, 50f, -25f), host.Position);
        Assert.Equal(Vector3.Zero, host.Velocity);

        console.Execute("tp ~ ~10 ~-5", host);
        Assert.Equal(new Vector3(100f, 60f, -30f), host.Position);

        console.Execute("tp spawn", host);
        Assert.Equal(host.SpawnPosition, host.Position);

        console.Execute("tp ship", host);
        Assert.Equal(new Vector3(40f, 54f, 60f), host.Position);
    }

    [Fact]
    public void Give_adds_items_by_id_colon_syntax_and_name_search()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost();

        var r1 = console.Execute($"give {Ore}:12", host);
        Assert.False(r1.IsError);
        Assert.Equal(12, host.Inventory.Count(Ore));

        var r2 = console.Execute("give Item_30 1", host);
        Assert.False(r2.IsError);
        Assert.Equal(1, host.Inventory.Count(Helmet));

        var r3 = console.Execute("clearinv", host);
        Assert.False(r3.IsError);
        Assert.Equal(0, host.Inventory.Count(Ore));
    }

    [Fact]
    public void Suit_commands_adjust_health_power_and_god_mode()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost();

        console.Execute("health 40; power 25", host);
        Assert.Equal(40, host.Suit!.Health);
        Assert.Equal(25, host.Suit.Power);

        console.Execute("heal", host);
        Assert.Equal(100, host.Suit.Health);
        Assert.Equal(100, host.Suit.Power);

        console.Execute("god on", host);
        Assert.True(host.Suit.GodMode);
        Assert.True(host.Suit.TrySpend(999));
        Assert.Equal(100, host.Suit.Power);
    }

    [Fact]
    public void History_and_autocomplete_navigate_commands()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost();

        console.Execute("noclip on", host);
        console.Execute("speed 2", host);

        Assert.Equal("speed 2", console.HistoryUp("draft"));
        Assert.Equal("noclip on", console.HistoryUp("draft"));
        Assert.Equal("speed 2", console.HistoryDown("draft"));
        Assert.Equal("draft", console.HistoryDown("draft"));

        Assert.Equal("noclip ", console.Autocomplete("noc"));
        Assert.Equal("spawnship ", console.Autocomplete("spawn"));
    }
}
