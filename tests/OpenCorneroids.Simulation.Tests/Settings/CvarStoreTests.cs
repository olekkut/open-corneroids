using System.Numerics;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Physics;
using OpenCorneroids.Simulation.Settings;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Settings;

public class CvarStoreTests
{
    [Fact]
    public void Every_setting_starts_at_its_default_and_names_are_unique_and_prefixed()
    {
        var store = new CvarStore();
        Assert.Empty(store.Differences());
        Assert.Equal(Cvars.All.Count, Cvars.All.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var cvar in Cvars.All)
        {
            Assert.Equal(cvar.Default, store.Get(cvar));
            if (cvar.Flags.HasFlag(CvarFlags.World)) Assert.StartsWith("sv_", cvar.Name);
            if (cvar.Flags.HasFlag(CvarFlags.User)) Assert.StartsWith("cl_", cvar.Name);
            Assert.False(string.IsNullOrWhiteSpace(cvar.Description));
        }
    }

    [Fact]
    public void Defaults_are_the_original_games_numbers()
    {
        var store = new CvarStore();
        Assert.Equal(AstronautMotion.Tuning.Original, AstronautMotion.Tuning.From(store));
        Assert.Equal(ShipLimits.Original, ShipLimits.From(store));
        Assert.Equal(Mining.MaxSecondsToMine, store.Get(Cvars.MineTimeMax), 6);
        Assert.Equal(Mining.MinSecondsToMine, store.Get(Cvars.MineTimeMin), 6);
        Assert.Equal(Mining.SecondsPerSparePower, store.Get(Cvars.MineTimePerPower), 6);
        Assert.Equal(AstronautMotion.BoostPowerPerSecond, store.Float(Cvars.BoostCost));
        Assert.Equal(ConsoleBinding.MaxAttachmentDistance, store.Float(Cvars.AttachDistance));
        Assert.Equal(ConsoleBinding.MaxBindDistance, store.Float(Cvars.BindDistance));
        Assert.Equal(ThrusterMount.DefaultThrustScale, store.Float(Cvars.AssistForce));
        Assert.Equal(Simulation.Combat.Weapons.RecoilScale, store.Float(Cvars.RecoilScale));
        // The original has no ricochets, no physical recoil and shots that do not take the shooter's speed.
        Assert.False(store.Bool(Cvars.Ricochet));
        Assert.False(store.Bool(Cvars.RecoilPush));
        Assert.False(store.Bool(Cvars.ProjectileInheritVelocity));
        Assert.Equal(512, store.Int(Cvars.ProjectileLimit));
        // The original's spread list, shots 1 m ahead of the gun, power from a negative powerUsage, no ship bounce.
        Assert.Equal(0, store.Int(Cvars.WeaponSpreadMode));
        Assert.Equal(1f, store.Float(Cvars.ShotForwardOffset));
        Assert.True(store.Bool(Cvars.WeaponNegativePower));
        Assert.Equal(ShipContact.Restitution, store.Float(Cvars.ShipRestitution));
        Assert.Equal(0f, store.Float(Cvars.ShipRestitution));
        Assert.Equal(ShipContact.ContactSpinDamping, store.Float(Cvars.ShipContactSpinDamping));
        // The original's 8 + 6 × 5 inventory, filled slot by slot from the hotbar; the main inventory takes the rest of
        // a pickup only when all of it fits.
        Assert.Equal(Inventory.DefaultSlotCount, Inventory.HotbarSize + store.Int(Cvars.InventorySlots));
        Assert.True(store.Bool(Cvars.InventoryFillHotbarFirst));
        Assert.True(store.Bool(Cvars.InventoryPickupWhole));
        // Stacks thrown out of item windows: 3 m/s on a click outside, 2 m/s on closing, 2 s before pickup, ×0.98 drag.
        Assert.Equal((3f, 2f, 2f, 0.98f), (store.Float(Cvars.ItemTossSpeed), store.Float(Cvars.ItemCloseTossSpeed),
            store.Float(Cvars.ItemTossPickupDelay), store.Float(Cvars.ItemDrag)));
        // As in the original, a click on an item window off its slots throws the held stack, and a press counts only
        // while the other mouse button is up.
        Assert.True(store.Bool(Cvars.ItemTossOnWindow) && store.Bool(Cvars.ItemClickNeedsRelease));
        // E and F reach 1.7 m; a broken chest throws its stacks out at 1–4 m/s, out of reach of pickup for 1 s.
        Assert.Equal(1.7f, store.Float(Cvars.UseReach));
        Assert.True(store.Bool(Cvars.ContainerSpill));
        Assert.Equal((1f, 4f, 1f), (store.Float(Cvars.ContainerSpillSpeedMin), store.Float(Cvars.ContainerSpillSpeedMax),
            store.Float(Cvars.ContainerSpillPickupDelay)));
        Assert.All([Cvars.UseReach, Cvars.ContainerSpill, Cvars.ContainerSpillSpeedMin, Cvars.ContainerSpillSpeedMax,
            Cvars.ContainerSpillPickupDelay], c => Assert.True(c.Flags.HasFlag(CvarFlags.World) && c.Name.StartsWith("sv_", StringComparison.Ordinal)));
        // The original's options: field of view 90 (45–135), mouse sensitivity 0.5 (0–3), gamma 50 (0–50),
        // target squares, view bobbing and hints on, SSAO high, medium view distance, V-Sync on.
        Assert.Equal((90.0, 45.0, 135.0), (store.Get(Cvars.Fov), Cvars.Fov.Min, Cvars.Fov.Max));
        Assert.Equal((0.5, 0.0, 3.0), (store.Get(Cvars.Sensitivity), Cvars.Sensitivity.Min, Cvars.Sensitivity.Max));
        Assert.Equal(50, store.Int(Cvars.Gamma));
        Assert.True(store.Bool(Cvars.DrawTargets) && store.Bool(Cvars.ViewBobbing) && store.Bool(Cvars.ShowHints) && store.Bool(Cvars.VSync));
        Assert.Equal(2, store.Int(Cvars.Ssao));
        Assert.Equal(100, store.Int(Cvars.ViewDistance));
    }

    [Theory]
    [InlineData("sv_ricochet", "on", 1)]
    [InlineData("sv_ricochet", "1", 1)]
    [InlineData("sv_ricochet", "false", 0)]
    [InlineData("sv_ship_max_speed", "35.5", 35.5)]
    [InlineData("sv_projectile_limit", "100", 100)]
    [InlineData("cl_fov", "90", 90)]
    public void Valid_text_sets_the_value(string name, string text, double expected)
    {
        var store = new CvarStore();
        var cvar = store.Find(name)!;
        Assert.True(store.TrySet(cvar, text, out var error), error);
        Assert.Equal(expected, store.Get(cvar));
    }

    [Theory]
    [InlineData("sv_ricochet", "maybe")]
    [InlineData("sv_ship_max_speed", "fast")]
    [InlineData("sv_ship_max_speed", "-1")]
    [InlineData("sv_projectile_limit", "2.5")]
    [InlineData("sv_projectile_limit", "0")]
    [InlineData("cl_fov", "500")]
    [InlineData("cl_fov", "NaN")]
    public void Invalid_text_is_refused_and_the_value_kept(string name, string text)
    {
        var store = new CvarStore();
        var cvar = store.Find(name)!;
        Assert.False(store.TrySet(cvar, text, out var error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal(cvar.Default, store.Get(cvar));
    }

    [Fact]
    public void Names_are_found_ignoring_case_and_spaces()
    {
        var store = new CvarStore();
        Assert.Same(Cvars.Ricochet, store.Find("SV_Ricochet"));
        Assert.Same(Cvars.Ricochet, store.Find("  sv_ricochet "));
        Assert.Null(store.Find("sv_nonsense"));
    }

    [Fact]
    public void Cheat_settings_need_sv_cheats_and_reset_when_it_is_turned_off()
    {
        var store = new CvarStore();
        Assert.False(store.TrySet(Cvars.PlayerSpeedScale, 3, out var error));
        Assert.Contains("sv_cheats", error);
        Assert.False(store.CheatsUsed);

        store.Set(Cvars.Cheats, true);
        Assert.True(store.CheatsUsed);
        store.Set(Cvars.PlayerSpeedScale, 3);
        Assert.Equal(3, store.Get(Cvars.PlayerSpeedScale));

        store.Set(Cvars.Cheats, false);
        Assert.Equal(1, store.Get(Cvars.PlayerSpeedScale));
        Assert.True(store.CheatsUsed);   // remembered
    }

    [Fact]
    public void Changed_fires_only_on_a_real_change()
    {
        var store = new CvarStore();
        var seen = new List<(string, double)>();
        store.Changed += (c, v) => seen.Add((c.Name, v));
        store.Set(Cvars.Fov, 90);   // already the default
        store.Set(Cvars.Fov, 100);
        store.Reset(Cvars.Fov);
        Assert.Equal([("cl_fov", 100), ("cl_fov", 90)], seen);
    }

    [Fact]
    public void Reset_all_respects_the_scope()
    {
        var store = new CvarStore();
        store.Set(Cvars.Ricochet, true);
        store.Set(Cvars.Fov, 100);

        store.ResetAll(CvarFlags.World);
        Assert.False(store.Bool(Cvars.Ricochet));
        Assert.Equal(100, store.Get(Cvars.Fov));

        store.ResetAll();
        Assert.Equal(90, store.Get(Cvars.Fov));
    }

    [Fact]
    public void World_rules_export_and_import_round_trip_and_leave_preferences_alone()
    {
        var store = new CvarStore();
        store.Set(Cvars.Cheats, true);
        store.Set(Cvars.PlayerSpeedScale, 4);
        store.Set(Cvars.ShipMaxSpeed, 40);
        store.Set(Cvars.Fov, 100);
        var saved = store.Export(CvarFlags.World);
        Assert.Equal(new Dictionary<string, string> { ["sv_cheats"] = "1", ["sv_player_speed_scale"] = "4", ["sv_ship_max_speed"] = "40" }, saved);

        var other = new CvarStore();
        other.Set(Cvars.Ricochet, true);   // a rule of the previous world, gone after the load
        other.Set(Cvars.Fov, 60);
        // Unordered on purpose: the cheat setting comes before sv_cheats in this dictionary.
        var problems = other.Import(new Dictionary<string, string>
        {
            ["sv_player_speed_scale"] = "4", ["sv_ship_max_speed"] = "40", ["sv_cheats"] = "1",
        }, CvarFlags.World);
        Assert.Empty(problems);
        Assert.Equal(4, other.Get(Cvars.PlayerSpeedScale));
        Assert.Equal(40, other.Get(Cvars.ShipMaxSpeed));
        Assert.False(other.Bool(Cvars.Ricochet));
        Assert.Equal(60, other.Get(Cvars.Fov));
    }

    [Fact]
    public void Import_reports_unknown_misplaced_and_bad_values_and_keeps_defaults()
    {
        var store = new CvarStore();
        var problems = store.Import(new Dictionary<string, string>
        {
            ["sv_from_the_future"] = "1",
            ["cl_fov"] = "90",
            ["sv_ship_max_speed"] = "-5",
            ["sv_player_speed_scale"] = "9",   // a cheat without sv_cheats
        }, CvarFlags.World);
        Assert.Equal(4, problems.Count);
        Assert.Empty(store.Differences());
    }

    [Fact]
    public void Config_text_lists_only_changed_settings_in_scope()
    {
        var store = new CvarStore();
        store.Set(Cvars.Fov, 100);
        store.Set(Cvars.Ricochet, true);
        var text = store.WriteConfig(CvarFlags.User, "Open Corneroids preferences");
        Assert.Equal("// Open Corneroids preferences\ncl_fov \"100\"\n", text);
    }

    [Fact]
    public void A_written_config_reads_back_and_bad_lines_are_reported()
    {
        var store = new CvarStore();
        store.Set(Cvars.Fov, 100);
        store.Set(Cvars.FlightAssist, true);
        var text = store.WriteConfig(CvarFlags.User, "header");

        var other = new CvarStore();
        Assert.Empty(other.ApplyConfig(text, CvarFlags.User));
        Assert.Equal(100, other.Get(Cvars.Fov));
        Assert.True(other.Bool(Cvars.FlightAssist));

        var problems = other.ApplyConfig("cl_fov 500\nsv_ricochet 1\nnoclip\n\n// fine\r\ncl_sensitivity 2 // faster\r\n", CvarFlags.User);
        Assert.Equal(3, problems.Count);
        Assert.StartsWith("line 1", problems[0]);
        Assert.Equal(100, other.Get(Cvars.Fov));
        Assert.False(other.Bool(Cvars.Ricochet));
        Assert.Equal(2, other.Get(Cvars.Sensitivity));
    }

    [Fact]
    public void Definitions_reject_bad_ranges_and_names()
    {
        Assert.Throws<ArgumentException>(() => new Cvar("bad name", CvarKind.Bool, 0, CvarFlags.World, "x"));
        Assert.Throws<ArgumentException>(() => new Cvar("sv_x", CvarKind.Float, 5, CvarFlags.World, "x", 0, 1));
        Assert.Throws<ArgumentException>(() => new Cvar("sv_x", CvarKind.Int, 0.5, CvarFlags.World, "x", 0, 1));
        Assert.Throws<ArgumentException>(() => new Cvar("sv_x", CvarKind.Bool, 0, CvarFlags.World | CvarFlags.User, "x"));
        Assert.Throws<ArgumentException>(() => new CvarStore([Cvars.Fov, Cvars.Fov]));
    }

    [Fact]
    public void Tuned_motion_mining_and_ship_limits_follow_the_settings()
    {
        var store = new CvarStore();
        store.Set(Cvars.PlayerSpeed, 10);
        store.Set(Cvars.PlayerAccel, 100);
        var input = new AstronautMotion.Input(Vector3.UnitZ, Vector3.UnitZ, true, false);
        var v = AstronautMotion.Step(Vector3.Zero, input, 0.05f, AstronautMotion.Tuning.From(store));
        Assert.Equal(5f, v.Length(), 3);   // 100 m/s² for 0.05 s, under the 10 m/s limit
        Assert.Equal(0.25f, AstronautMotion.Step(Vector3.Zero, input, 0.05f).Length(), 3);

        var block = Items.Support.TestItems.Block(99, health: 5, powerToDrill: 1);
        Assert.Equal(0.3, Mining.SecondsToMine(10, block, 0.8, 0.3, 0.1), 6);
        Assert.Equal(2.0, Mining.SecondsToMine(1, block, 2.0, 0.3, 0.1), 6);
        // A minimum above the maximum reads as the maximum.
        Assert.Equal(0.5, Mining.SecondsToMine(10, block, 0.5, 0.9, 0.1), 6);

        store.Set(Cvars.ShipMaxSpeed, 5);
        var body = new RigidBodyState { LinearVelocity = new Vector3(19, 0, 0) };
        body.StepOriginal(Vector3.Zero, Vector3.Zero, MassProperties.Empty, 0.1f, thrustersFiring: true, ShipLimits.From(store));
        Assert.Equal(5f, body.LinearVelocity.Length(), 3);
    }
}
