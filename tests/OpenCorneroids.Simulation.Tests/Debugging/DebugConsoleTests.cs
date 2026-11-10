using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Debugging;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Physics;
using OpenCorneroids.Simulation.Settings;
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

        public bool Noclip { get; set; }
        public CvarStore Cvars { get; } = new();

        public int CreatureCount { get; set; } = 3;
        public bool TeleportToAim(float stopShort, out string message) { message = ""; return false; }
        public string? Bind(string arguments) => arguments.StartsWith("Q ") ? null : "no";
        public IReadOnlyList<string> Bindings => ["interact E"];
        public int ClearCreatures() { int n = CreatureCount; CreatureCount = 0; return n; }
        public Simulation.Creatures.CreatureKind? Spawned { get; private set; }
        public string SpawnCreature(Simulation.Creatures.CreatureKind kind) { Spawned = kind; return $"Spawned {kind}."; }

        public bool Respawn()
        {
            if (Suit is not { IsDead: true } suit) return false;
            suit.Respawn();
            return true;
        }
        public Dictionary<string, string> ConfigFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int ConfigWrites { get; private set; }

        public bool TryReadConfig(string name, out string text, out string error)
        {
            var key = name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ? name : name + ".cfg";
            error = $"no config file '{key}'";
            return ConfigFiles.TryGetValue(key, out text!);
        }

        public string? WriteUserConfig()
        {
            ConfigWrites++;
            return null;
        }

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

        public string ImportShip(string path)
        {
            if (path.Contains("invalid")) return "Failed to import ship: file corrupted";
            ShipList.Add(new("ImportedShip", 50, 150f, Position, 0f));
            return $"Imported structure from {path}.";
        }

        public string ExportShip(string? selector, string path)
        {
            if (ShipList.Count == 0) return "No ship found to export.";
            return $"Exported {ShipList[0].Name} to {path}.";
        }

        public StressRequest? Stressed { get; private set; }
        public string Stress(StressRequest request)
        {
            Stressed = request;
            return $"Built {request.Kind} x{request.Count}.";
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

        var refused = console.Execute("speed 3.5", host);
        Assert.True(refused.IsError);
        Assert.Contains("sv_cheats", refused.Lines[0]);
        Assert.Equal(1f, host.Cvars.Float(Cvars.PlayerSpeedScale));

        console.Execute("sv_cheats 1; speed 3.5", host);
        Assert.Equal(3.5f, host.Cvars.Float(Cvars.PlayerSpeedScale));

        console.Execute("speed 999", host);
        Assert.Equal(50f, host.Cvars.Float(Cvars.PlayerSpeedScale));

        console.Execute("speed reset", host);
        Assert.Equal(1f, host.Cvars.Float(Cvars.PlayerSpeedScale));

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
        console.Execute("sv_cheats 1", host);

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
        console.Execute("sv_cheats 1", host);

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
        console.Execute("sv_cheats 1", host);

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
    public void Kill_and_respawn_need_no_cheats()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost();

        Assert.True(console.Execute("respawn", host).IsError);
        Assert.False(console.Execute("kill", host).IsError);
        Assert.True(host.Suit!.IsDead);
        Assert.True(console.Execute("heal", host).IsError);   // a cheat, and the dead need the Respawn button anyway

        Assert.False(console.Execute("respawn", host).IsError);
        Assert.False(host.Suit.IsDead);
        Assert.Equal(100, host.Suit.Health);
    }

    [Fact]
    public void Creature_commands_count_clear_and_spawn_behind_cheats()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost();
        Assert.Contains("3 creature", console.Execute("creatures", host).Lines[0]);
        Assert.Contains("Removed 3", console.Execute("creatures clear", host).Lines[0]);

        Assert.True(console.Execute("spawn egg", host).IsError);   // a cheat
        console.Execute("sv_cheats 1", host);
        Assert.False(console.Execute("spawn egg", host).IsError);
        Assert.Equal(Simulation.Creatures.CreatureKind.SwarmEgg, host.Spawned);
        Assert.True(console.Execute("spawn dragon", host).IsError);
    }

    [Fact]
    public void Stress_is_a_cheat_that_passes_the_parsed_request_to_the_host()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost();

        Assert.True(console.Execute("stress ship 6000", host).IsError);   // a cheat
        Assert.Null(host.Stressed);
        console.Execute("sv_cheats 1", host);

        var built = console.Execute("stress ship 6000", host);
        Assert.False(built.IsError);
        Assert.Equal(new StressRequest(StressKind.Ship, 6000), host.Stressed);
        Assert.Equal("Built Ship x6000.", built.Lines[0]);

        Assert.False(console.Execute("stress creatures scavenger 60", host).IsError);
        Assert.Equal(new StressRequest(StressKind.Creatures, 60, Simulation.Creatures.CreatureKind.Scavenger), host.Stressed);

        var wrong = console.Execute("stress guns lots", host);
        Assert.True(wrong.IsError);
        Assert.Contains("Usage", wrong.Lines[0]);
    }

    [Fact]
    public void Bind_lists_and_sets_keys_through_the_host()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost();
        var list = console.Execute("bind", host);
        Assert.False(list.IsError);
        Assert.Contains(list.Lines, l => l.Contains("interact E"));
        Assert.False(console.Execute("bind Q interact", host).IsError);
        Assert.True(console.Execute("bind Escape interact", host).IsError);
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
        Assert.Equal("spawn ", console.Autocomplete("spawn"));
        Assert.Equal("spawnship ", console.Autocomplete("spawns"));
        Assert.Equal("importship ", console.Autocomplete("imports"));
        Assert.Equal("exportship ", console.Autocomplete("exports"));
    }

    [Fact]
    public void ImportShip_and_ExportShip_execute_and_report_status()
    {
        var console = new DebugConsole();
        var host = new FakeConsoleHost();

        var resUsage = console.Execute("importship", host);
        Assert.True(resUsage.IsError);
        Assert.Contains("Usage", resUsage.Lines[0]);

        var resImport = console.Execute("importship ship.sbs", host);
        Assert.False(resImport.IsError);
        Assert.Contains("Imported", resImport.Lines[0]);
        Assert.Equal(2, host.Ships.Count);

        var resExport = console.Execute("exportship ship.sbs", host);
        Assert.False(resExport.IsError);
        Assert.Contains("Exported", resExport.Lines[0]);

        var resExportWithSelector = console.Execute("exportship #1 out.sbs", host);
        Assert.False(resExportWithSelector.IsError);
        Assert.Contains("Exported", resExportWithSelector.Lines[0]);

        var resError = console.Execute("importship invalid.sbs", host);
        Assert.True(resError.IsError);
        Assert.Contains("Failed", resError.Lines[0]);
    }
}
