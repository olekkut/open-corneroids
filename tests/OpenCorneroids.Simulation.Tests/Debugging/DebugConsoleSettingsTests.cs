using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Debugging;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Settings;
using static OpenCorneroids.Simulation.Tests.Items.Support.TestItems;

namespace OpenCorneroids.Simulation.Tests.Debugging;

/// <summary>The console's settings commands: show/set, find, cvarlist, differences, toggle, reset, preset, exec and
/// cheat gating.</summary>
public class DebugConsoleSettingsTests
{
    sealed class Host : IDebugConsoleHost
    {
        public ItemsetDef Itemset { get; } = Set();
        public Inventory Inventory { get; }
        public Suit? Suit { get; }
        public Host()
        {
            Inventory = Inventory.ForItemset(Itemset);
            Suit = new Suit(Itemset);
        }
        public bool Creative => false;
        public void SetCreative(bool creative) { }
        public Vector3 Position => Vector3.Zero;
        public Vector3 Velocity => Vector3.Zero;
        public Vector3 SpawnPosition => Vector3.Zero;
        public void Teleport(Vector3 position) { }
        public bool Noclip { get; set; }
        public bool Respawn() => false;
        public int CreatureCount => 0;
        public bool TeleportToAim(float stopShort, out string message) { message = ""; return false; }
        public string? Bind(string arguments) => arguments.StartsWith("Q ") ? null : "no";
        public IReadOnlyList<string> Bindings => ["interact E"];
        public int ClearCreatures() => 0;
        public string SpawnCreature(Simulation.Creatures.CreatureKind kind) => "";
        public CvarStore Cvars { get; } = new();
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Writes;

        public bool TryReadConfig(string name, out string text, out string error)
        {
            var key = name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ? name : name + ".cfg";
            error = $"no config file '{key}'";
            return Files.TryGetValue(key, out text!);
        }

        public string? WriteUserConfig()
        {
            Writes++;
            return null;
        }

        public IReadOnlyList<DebugShipSummary> Ships => [];
        public bool TryTeleportToShip(string? selector, out string message) { message = ""; return false; }
        public string SpawnDemoShip() => "";
        public string LaunchTargetedOrNearestShip() => "";
        public int ClearShips() => 0;
        public string ImportShip(string path) => "";
        public string ExportShip(string? selector, string path) => "";
        public string? SaveWorld() => null;
    }

    readonly DebugConsole console = new();
    readonly Host host = new();

    DebugConsoleResult Run(string line) => console.Execute(line, host);

    [Fact]
    public void Stress_needs_a_world_that_can_build_it()
    {
        // This host does not build stress scenes, so the interface's own answer is given.
        var result = Run("sv_cheats 1; stress objects 5");
        Assert.Contains("running world", result.Lines[^1]);
    }

    [Fact]
    public void A_setting_name_shows_it_and_a_value_sets_it()
    {
        var shown = Run("sv_ricochet");
        Assert.False(shown.IsError);
        Assert.StartsWith("sv_ricochet = 0", shown.Lines[0]);
        Assert.Contains("default 0", shown.Lines[0]);
        Assert.Contains("world rule", shown.Lines[0]);

        var set = Run("sv_ricochet 1");
        Assert.False(set.IsError);
        Assert.Equal("sv_ricochet = 1", set.Lines[0]);
        Assert.True(host.Cvars.Bool(Cvars.Ricochet));

        var bad = Run("sv_ship_max_speed lots");
        Assert.True(bad.IsError);
        Assert.Equal(20, host.Cvars.Get(Cvars.ShipMaxSpeed));

        Run("sv_ricochet default");
        Assert.False(host.Cvars.Bool(Cvars.Ricochet));
    }

    [Fact]
    public void A_setting_read_when_the_world_loads_says_so_when_changed()
    {
        Assert.Contains("on load", Run("sv_inventory_slots").Lines[0]);

        var set = Run("sv_inventory_slots 36");
        Assert.False(set.IsError);
        Assert.Equal("sv_inventory_slots = 36 (takes effect when the world is loaded again)", set.Lines[0]);
        Assert.Equal(Inventory.DefaultSlotCount, host.Inventory.Slots.Count);   // the open inventory keeps its size

        Assert.Equal("sv_ricochet = 1", Run("sv_ricochet 1").Lines[0]);
    }

    [Fact]
    public void Resetting_rules_names_the_settings_read_when_the_world_loads()
    {
        Run("sv_inventory_slots 32; sv_ricochet 1");
        var reset = Run("reset rules");
        Assert.Equal(["World rules (sv_) reset to the original game's.",
            "sv_inventory_slots = 30 (default) (takes effect when the world is loaded again)"], reset.Lines);

        Assert.Single(Run("reset rules").Lines);   // nothing read on load changed this time

        Run("sv_inventory_slots 40");
        Assert.Contains(Run("preset original").Lines, l => l.StartsWith("sv_inventory_slots = 30") && l.Contains("loaded again"));
        Assert.Contains(Run("sv_inventory_slots 40; reset all").Lines, l => l.StartsWith("sv_inventory_slots = 30"));
    }

    [Fact]
    public void Help_on_a_setting_describes_it()
    {
        var help = Run("help cl_fov");
        Assert.StartsWith("cl_fov = 90", help.Lines[0]);
        Assert.Contains("Field of view", help.Lines[1]);
    }

    [Fact]
    public void Find_cvarlist_and_differences_list_settings()
    {
        var found = Run("find ricochet");
        Assert.Contains(found.Lines, l => l.Contains("sv_ricochet "));
        Assert.Contains(found.Lines, l => l.Contains("sv_ricochet_bounces"));

        var listed = Run("cvarlist sv_ship");
        Assert.Equal(7, listed.Lines.Count);   // the header and six ship settings

        Assert.Contains("original preset", Run("differences").Lines[0]);
        Run("sv_ship_max_speed 30");
        var diff = Run("diff");
        Assert.Contains(diff.Lines, l => l.Contains("sv_ship_max_speed = 30") && l.Contains("default 20"));
    }

    [Fact]
    public void Toggle_flips_only_on_off_settings()
    {
        Run("toggle sv_recoil_push");
        Assert.True(host.Cvars.Bool(Cvars.RecoilPush));
        Run("toggle sv_recoil_push");
        Assert.False(host.Cvars.Bool(Cvars.RecoilPush));
        Assert.True(Run("toggle cl_fov").IsError);
        Assert.True(Run("toggle sv_nothing").IsError);
    }

    [Fact]
    public void Reset_handles_one_setting_rules_prefs_and_all()
    {
        Run("sv_ricochet 1; cl_fov 100; sv_boost_cost 0");
        Run("reset sv_boost_cost");
        Assert.Equal(20, host.Cvars.Get(Cvars.BoostCost));

        Run("reset rules");
        Assert.False(host.Cvars.Bool(Cvars.Ricochet));
        Assert.Equal(100, host.Cvars.Get(Cvars.Fov));

        Run("reset prefs");
        Assert.Equal(90, host.Cvars.Get(Cvars.Fov));

        Run("sv_ricochet 1; cl_fov 100; reset all");
        Assert.Empty(host.Cvars.Differences());
    }

    [Fact]
    public void Presets_restore_the_original_or_turn_on_our_assists()
    {
        Assert.Contains(Run("preset").Lines, l => l.Contains("original"));

        Run("preset assisted");
        Assert.True(host.Cvars.Bool(Cvars.FlightAssist));
        Assert.True(host.Cvars.Bool(Cvars.FixedCamera));

        Run("sv_ricochet 1; cl_fov 100");
        var original = Run("preset original");
        Assert.False(original.IsError);
        Assert.False(host.Cvars.Bool(Cvars.Ricochet));
        Assert.False(host.Cvars.Bool(Cvars.FlightAssist));
        Assert.Equal(100, host.Cvars.Get(Cvars.Fov));   // a viewing preference, not part of the game's rules

        Assert.True(Run("preset nonsense").IsError);
    }

    [Fact]
    public void Exec_runs_a_config_file_with_comments_and_reports_bad_lines()
    {
        host.Files["arcade.cfg"] = """
            // Fast and loose
            sv_ship_max_speed 60   // twice the original
            sv_ricochet 1; sv_ricochet_bounces 5
            sv_recoil_push "1"
            sv_nonsense 3
            """;
        var result = Run("exec arcade");
        Assert.True(result.IsError);   // the unknown line
        Assert.Contains(result.Lines, l => l.Contains("line 5"));
        Assert.Equal(60, host.Cvars.Get(Cvars.ShipMaxSpeed));
        Assert.Equal(5, host.Cvars.Int(Cvars.RicochetBounces));
        Assert.True(host.Cvars.Bool(Cvars.RecoilPush));

        Assert.True(Run("exec missing").IsError);
    }

    [Fact]
    public void Only_the_lines_of_a_failed_command_are_marked_as_errors()
    {
        var result = Run("sv_ricochet 1; noclip; cl_fov 90");
        Assert.True(result.IsError);
        Assert.Equal(3, result.Lines.Count);
        Assert.False(result.IsErrorLine(0));
        Assert.True(result.IsErrorLine(1));
        Assert.False(result.IsErrorLine(2));

        host.Files["mixed.cfg"] = "sv_boost_cost 5\nsv_nonsense 1";
        var exec = Run("exec mixed");
        Assert.False(exec.IsErrorLine(0));                              // sv_boost_cost = 5
        Assert.True(exec.IsErrorLine(1));                               // unknown command
        Assert.True(exec.IsErrorLine(2));                               // (in mixed, line 2)
        Assert.False(exec.IsErrorLine(exec.Lines.Count - 1));           // Executed mixed.
    }

    [Fact]
    public void Exec_stops_a_config_that_runs_itself()
    {
        host.Files["loop.cfg"] = "sv_boost_cost 5\nexec loop";
        var result = Run("exec loop");
        Assert.True(result.IsError);
        Assert.Contains(result.Lines, l => l.Contains("nest more than"));
        Assert.Equal(5, host.Cvars.Get(Cvars.BoostCost));
    }

    [Fact]
    public void Cheat_commands_need_sv_cheats_and_turning_it_off_undoes_them()
    {
        var refused = Run("noclip on");
        Assert.True(refused.IsError);
        Assert.Contains("sv_cheats 1", refused.Lines[0]);
        Assert.False(host.Noclip);
        Assert.True(Run($"give {Ore} 5").IsError);
        Assert.Equal(0, host.Inventory.Count(Ore));

        Run("sv_cheats 1; noclip on; god on; speed 4");
        Assert.True(host.Noclip);
        Assert.True(host.Suit!.GodMode);
        Assert.True(host.Cvars.CheatsUsed);

        var off = Run("sv_cheats 0");
        Assert.False(host.Noclip);
        Assert.False(host.Suit.GodMode);
        Assert.Equal(1, host.Cvars.Get(Cvars.PlayerSpeedScale));
        Assert.Contains(off.Lines, l => l.Contains("noclip ended"));
        Assert.Contains(Run("differences").Lines, l => l.Contains("cheats were used", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Old_shortcut_commands_drive_the_settings()
    {
        Run("assist on; fixedcam on; fov 200");
        Assert.True(host.Cvars.Bool(Cvars.FlightAssist));
        Assert.True(host.Cvars.Bool(Cvars.FixedCamera));
        Assert.Equal(135, host.Cvars.Get(Cvars.Fov));   // the shortcut clamps
        Run("fov reset");
        Assert.Equal(90, host.Cvars.Get(Cvars.Fov));
    }

    [Fact]
    public void Writeconfig_asks_the_host_to_save_preferences()
    {
        Assert.False(Run("writeconfig").IsError);
        Assert.Equal(1, host.Writes);
    }

    [Fact]
    public void Tab_completes_setting_names_as_far_as_they_agree()
    {
        Assert.Equal("sv_ricochet", console.Autocomplete("sv_ric", host.Cvars));
        Assert.Equal("sv_ricochet_bounces ", console.Autocomplete("sv_ricochet_", host.Cvars));
        Assert.Equal("cl_fov ", console.Autocomplete("cl_fo", host.Cvars));
        Assert.Equal("sv_ship_", console.Autocomplete("sv_shi", host.Cvars));
        Assert.Equal("noclip ", console.Autocomplete("noc", host.Cvars));
    }
}
