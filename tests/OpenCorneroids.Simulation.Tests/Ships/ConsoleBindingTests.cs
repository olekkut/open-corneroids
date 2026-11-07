using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class ConsoleBindingTests
{
    static readonly Int3 ConsoleA = new(2, 1, 2);
    static readonly Int3 ConsoleB = new(4, 1, 2);
    static readonly Int3 Thruster1 = new(1, 1, 4);
    static readonly Int3 Thruster2 = new(3, 1, 4);
    static readonly Int3 Cannon1 = new(2, 1, 0);
    static readonly Int3 Camera1 = new(2, 2, 0);

    [Fact]
    public void Ten_color_groups_follow_the_original_order_and_clamp_buttons()
    {
        Assert.Equal(10, ConsoleBinding.ColorGroups.Count);
        Assert.Equal("Red", ConsoleBinding.ColorGroups[0].Name);
        Assert.Equal(new Rgb8(255, 0, 0), ConsoleBinding.ColorGroups[0].Color);
        Assert.Equal("Green", ConsoleBinding.ColorGroups[1].Name);
        Assert.Equal("LightBlue", ConsoleBinding.ColorGroups[2].Name);
        Assert.Equal("Yellow", ConsoleBinding.ColorGroups[3].Name);
        Assert.Equal("Violet", ConsoleBinding.ColorGroups[4].Name);
        Assert.Equal("Orange", ConsoleBinding.ColorGroups[5].Name);
        Assert.Equal("Brown", ConsoleBinding.ColorGroups[6].Name);
        Assert.Equal("Turquoise", ConsoleBinding.ColorGroups[7].Name);
        Assert.Equal("Pink", ConsoleBinding.ColorGroups[8].Name);
        Assert.Equal("DarkBlue", ConsoleBinding.ColorGroups[9].Name);
        Assert.Equal(new Rgb8(0, 0, 139), ConsoleBinding.ColorGroups[9].Color);

        Assert.Equal(0, ConsoleBinding.ClampGroupCount(-3));
        Assert.Equal(5, ConsoleBinding.ClampGroupCount(5));
        Assert.Equal(10, ConsoleBinding.ClampGroupCount(15));
    }

    [Fact]
    public void Cycling_a_target_steps_through_each_color_and_then_unbinds()
    {
        var bindings = new ShipConsoleBindings();
        for (int expected = 0; expected < 3; expected++)
        {
            int group = bindings.CycleTarget(ConsoleA, buttons: 3, gunsOnly: false, Thruster1, isThruster: true, isGun: false);
            Assert.Equal(expected, group);
            Assert.Equal(expected, bindings.TryGet(ConsoleA)!.GroupOf(Thruster1));
        }

        // After the last group (index 2 for a 3-button console), clicking again unbinds the block (-1).
        int unbound = bindings.CycleTarget(ConsoleA, buttons: 3, gunsOnly: false, Thruster1, isThruster: true, isGun: false);
        Assert.Equal(-1, unbound);
        Assert.Equal(-1, bindings.TryGet(ConsoleA)!.GroupOf(Thruster1));
    }

    [Fact]
    public void Binding_a_target_to_another_console_steals_it_from_the_previous_console()
    {
        var bindings = new ShipConsoleBindings();
        Assert.Equal(0, bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Thruster1, isThruster: true, isGun: false));
        Assert.Equal(1, bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Thruster1, isThruster: true, isGun: false));
        Assert.Equal(1, bindings.TryGet(ConsoleA)!.GroupOf(Thruster1));

        // Binding Thruster1 to ConsoleB removes it from ConsoleA and places it in group 0 of ConsoleB.
        Assert.Equal(0, bindings.CycleTarget(ConsoleB, buttons: 5, gunsOnly: false, Thruster1, isThruster: true, isGun: false));
        Assert.Equal(-1, bindings.TryGet(ConsoleA)!.GroupOf(Thruster1));
        Assert.Equal(0, bindings.TryGet(ConsoleB)!.GroupOf(Thruster1));
    }

    [Fact]
    public void Gun_consoles_accept_cannons_and_reject_thrusters()
    {
        var bindings = new ShipConsoleBindings();
        Assert.Equal(-1, bindings.CycleTarget(ConsoleA, buttons: 2, gunsOnly: true, Thruster1, isThruster: true, isGun: false));
        Assert.Equal(0, bindings.CycleTarget(ConsoleA, buttons: 2, gunsOnly: true, Cannon1, isThruster: false, isGun: true));
    }

    [Fact]
    public void Multiple_groups_can_share_a_key_and_fire_together()
    {
        var bindings = new ShipConsoleBindings();
        bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Thruster1, isThruster: true, isGun: false); // group 0
        bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Thruster2, isThruster: true, isGun: false);
        bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Thruster2, isThruster: true, isGun: false); // group 1
        bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Cannon1, isThruster: false, isGun: true);
        bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Cannon1, isThruster: false, isGun: true);
        bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Cannon1, isThruster: false, isGun: true);   // group 2

        bindings.AssignKey(ConsoleA, buttons: 5, gunsOnly: false, 0, "W");
        bindings.AssignKey(ConsoleA, buttons: 5, gunsOnly: false, 1, "W");
        bindings.AssignKey(ConsoleA, buttons: 5, gunsOnly: false, 2, "Space");

        var heldW = new HashSet<string>(["W"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal([Thruster1, Thruster2], bindings.ActiveTargets(ConsoleA, heldW));

        var heldBoth = new HashSet<string>(["w", "space"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal([Thruster1, Thruster2, Cannon1], bindings.ActiveTargets(ConsoleA, heldBoth));
    }

    [Fact]
    public void Camera_binding_toggles_and_removing_blocks_cleans_up_references()
    {
        var bindings = new ShipConsoleBindings();
        Assert.True(bindings.ToggleCamera(ConsoleA, buttons: 5, gunsOnly: false, Camera1));
        Assert.Equal(Camera1, bindings.TryGet(ConsoleA)!.Camera);
        Assert.False(bindings.ToggleCamera(ConsoleA, buttons: 5, gunsOnly: false, Camera1));
        Assert.Null(bindings.TryGet(ConsoleA)!.Camera);

        bindings.ToggleCamera(ConsoleA, buttons: 5, gunsOnly: false, Camera1);
        bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Thruster1, isThruster: true, isGun: false);
        bindings.RemoveBlock(Camera1);
        Assert.Null(bindings.TryGet(ConsoleA)!.Camera);
        bindings.RemoveBlock(Thruster1);
        Assert.Equal(-1, bindings.TryGet(ConsoleA)!.GroupOf(Thruster1));
        bindings.RemoveBlock(ConsoleA);
        Assert.Null(bindings.TryGet(ConsoleA));
    }

    [Fact]
    public void Extracting_and_shifting_moves_bindings_into_ship_local_coordinates()
    {
        var bindings = new ShipConsoleBindings();
        var shift = new Int3(1, 1, 0);
        bindings.CycleTarget(ConsoleA, buttons: 5, gunsOnly: false, Thruster1, isThruster: true, isGun: false);
        bindings.AssignKey(ConsoleA, buttons: 5, gunsOnly: false, 0, "W");
        bindings.ToggleCamera(ConsoleA, buttons: 5, gunsOnly: false, Camera1);

        var cellSet = new HashSet<Int3>([ConsoleA, Thruster1, Camera1]);
        var shipBindings = bindings.ExtractAndShift(cellSet, shift);

        Assert.Null(bindings.TryGet(ConsoleA));
        var moved = shipBindings.TryGet(ConsoleA - shift);
        Assert.NotNull(moved);
        Assert.Equal(Camera1 - shift, moved.Camera);
        Assert.Equal("W", moved.Groups[0].Key);
        Assert.Equal([Thruster1 - shift], moved.Groups[0].Targets);
    }

    [Fact]
    public void Placement_reach_and_gun_aim_hemisphere_match_original_formulas()
    {
        Assert.Equal(MathF.Sqrt(0.75f) + 2.0f, ConsoleBinding.PlacementReach(Vector3.One), 1e-5f);
        Assert.Equal(MathF.Sqrt(3.0f) + 2.0f, ConsoleBinding.PlacementReach(new Vector3(2, 2, 2)), 1e-5f);

        // Within forward hemisphere (dot > 0): aims along the player's view ray.
        var aimed = ConsoleBinding.GunAimDirection(-Vector3.UnitZ, Vector3.Normalize(new Vector3(0.5f, 0.2f, -1f)));
        Assert.Equal(Vector3.Normalize(new Vector3(0.5f, 0.2f, -1f)), aimed);

        // At 90° or behind (dot <= 0): clamps to barrel forward.
        Assert.Equal(-Vector3.UnitZ, ConsoleBinding.GunAimDirection(-Vector3.UnitZ, Vector3.UnitX));
        Assert.Equal(-Vector3.UnitZ, ConsoleBinding.GunAimDirection(-Vector3.UnitZ, Vector3.UnitZ));
    }

    [Fact]
    public void Console_bindings_round_trip_through_world_save_format()
    {
        var console = new SavedConsole(
            new Int3(1, 0, 2),
            [new SavedConsoleGroup(0, "W", [new Int3(0, 0, 3), new Int3(2, 0, 3)]), new SavedConsoleGroup(2, "Space", [new Int3(1, 0, 0)])],
            new Int3(1, 1, 0));

        var world = SmallWorld() with
        {
            Consoles = [console],
            Ships = [Ship() with { Consoles = [console] }],
        };

        var loaded = FromBytes(ToBytes(world));
        Assert.Single(loaded.Consoles);
        Assert.Equal(console.Cell, loaded.Consoles[0].Cell);
        Assert.Equal(console.Camera, loaded.Consoles[0].Camera);
        Assert.Equal(2, loaded.Consoles[0].Groups.Count);
        Assert.Equal("W", loaded.Consoles[0].Groups[0].Key);
        Assert.Equal([new Int3(0, 0, 3), new Int3(2, 0, 3)], loaded.Consoles[0].Groups[0].Targets);
        Assert.Equal("Space", loaded.Consoles[0].Groups[1].Key);
        Assert.Equal([new Int3(1, 0, 0)], loaded.Consoles[0].Groups[1].Targets);

        Assert.Single(loaded.Ships[0].Consoles);
        Assert.Equal(console.Camera, loaded.Ships[0].Consoles[0].Camera);
    }
}
