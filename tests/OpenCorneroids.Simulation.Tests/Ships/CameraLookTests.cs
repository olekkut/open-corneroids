using OpenCorneroids.Simulation.Settings;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Simulation.Tests.Ships;

public class CameraLookTests
{
    const float Rate = 0.005f;   // 0.01 rad per pixel × the default sensitivity 0.5
    static readonly float HalfPi = MathF.PI / 2;
    static readonly Int3 Console = new(2, 1, 2);
    static readonly Int3 Camera = new(2, 2, 0);

    [Fact]
    public void A_camera_starts_looking_straight_out_of_its_front()
    {
        Assert.Equal(new CameraLook(0, 0), CameraLook.Rest);
        Assert.Equal(CameraLook.Rest, new CameraLooks().Get(Camera));
    }

    [Fact]
    public void Mouse_right_turns_right_and_mouse_down_looks_down_as_in_the_original()
    {
        var look = CameraLook.Rest.Rotate(100, 40, Rate, HalfPi);

        Assert.Equal(-0.5f, look.Yaw, 5);
        Assert.Equal(-0.2f, look.Pitch, 5);
    }

    [Fact]
    public void Yaw_wraps_round_and_never_stops()
    {
        var look = CameraLook.Rest;
        for (int i = 0; i < 100; i++) look = look.Rotate(-100, 0, Rate, HalfPi);   // 50 rad to the left

        Assert.InRange(look.Yaw, -MathF.PI, MathF.PI);
        Assert.Equal(CameraLook.WrapAngle(50f), look.Yaw, 4);
        Assert.Equal(MathF.PI, CameraLook.WrapAngle(-MathF.PI), 5);
        Assert.Equal(0f, CameraLook.WrapAngle(float.NaN));
    }

    [Fact]
    public void Pitch_stops_at_the_limit_straight_up_and_down_by_default()
    {
        float limit = CameraLook.PitchLimitRadians(Cvars.CameraPitchLimit.Default);

        Assert.Equal(HalfPi, limit, 5);
        Assert.Equal(HalfPi, CameraLook.Rest.Rotate(0, -10_000, Rate, limit).Pitch, 5);
        Assert.Equal(-HalfPi, CameraLook.Rest.Rotate(0, 10_000, Rate, limit).Pitch, 5);
        Assert.Equal(0.5f, CameraLook.Rest.Rotate(0, -10_000, Rate, 0.5f).Pitch, 5);
        Assert.Equal(0f, CameraLook.Rest.Rotate(0, -10_000, Rate, -1f).Pitch);
    }

    [Fact]
    public void Lowering_the_limit_clamps_a_look_already_turned_further()
    {
        var look = new CameraLook(1f, 1.2f);

        Assert.Equal(new CameraLook(1f, 0.5f), look.Clamped(0.5f));
        Assert.Equal(new CameraLook(1f, -0.5f), (look with { Pitch = -1.2f }).Clamped(0.5f));
    }

    [Fact]
    public void The_pitch_limit_setting_is_held_within_0_to_90_degrees()
    {
        Assert.Equal(0f, CameraLook.PitchLimitRadians(-30));
        Assert.Equal(HalfPi, CameraLook.PitchLimitRadians(400), 5);
        Assert.Equal(MathF.PI / 4, CameraLook.PitchLimitRadians(45), 5);
        Assert.Equal(HalfPi, CameraLook.PitchLimitRadians(double.NaN), 5);
    }

    [Fact]
    public void Each_camera_keeps_its_own_look_between_lookups()
    {
        var looks = new CameraLooks();
        var other = new Int3(5, 2, 0);

        looks.Rotate(Camera, 100, 0, Rate, HalfPi);
        looks.Rotate(Camera, 0, -40, Rate, HalfPi);

        AssertLook(-0.5f, 0.2f, looks.Get(Camera));
        Assert.Equal(looks.Get(Camera), looks.Get(Camera));
        Assert.Equal(CameraLook.Rest, looks.Get(other));
    }

    [Fact]
    public void A_look_turned_back_to_rest_is_not_kept()
    {
        var looks = new CameraLooks();
        looks.Rotate(Camera, 100, 0, Rate, HalfPi);
        looks.Rotate(Camera, -100, 0, Rate, HalfPi);

        Assert.Equal(0, looks.Count);
    }

    [Fact]
    public void A_removed_camera_forgets_its_look_and_a_new_one_there_starts_at_rest()
    {
        var bindings = new ShipConsoleBindings();
        bindings.ToggleCamera(Console, 4, false, Camera);
        bindings.CameraLooks.Rotate(Camera, 100, 0, Rate, HalfPi);

        bindings.RemoveBlock(Camera);

        Assert.Equal(CameraLook.Rest, bindings.CameraLooks.Get(Camera));
        Assert.Equal(0, bindings.CameraLooks.Count);
    }

    [Fact]
    public void The_look_stays_when_the_console_is_removed_while_the_camera_stays()
    {
        var bindings = new ShipConsoleBindings();
        bindings.ToggleCamera(Console, 4, false, Camera);
        bindings.CameraLooks.Rotate(Camera, 100, 0, Rate, HalfPi);

        bindings.RemoveBlock(Console);

        Assert.Equal(-0.5f, bindings.CameraLooks.Get(Camera).Yaw, 5);
    }

    [Fact]
    public void Launching_moves_the_look_with_the_camera()
    {
        var world = new ShipConsoleBindings();
        world.ToggleCamera(Console, 4, false, Camera);
        world.CameraLooks.Rotate(Camera, 100, -40, Rate, HalfPi);
        var outside = new Int3(9, 9, 9);
        world.CameraLooks.Rotate(outside, 50, 0, Rate, HalfPi);
        var shift = new Int3(1, 1, 1);

        var ship = world.ExtractAndShift(new HashSet<Int3> { Console, Camera }, shift);

        AssertLook(-0.5f, 0.2f, ship.CameraLooks.Get(Camera - shift));
        Assert.Equal(CameraLook.Rest, world.CameraLooks.Get(Camera));
        Assert.Equal(CameraLook.Rest, ship.CameraLooks.Get(outside - shift));
        Assert.Equal(-0.25f, world.CameraLooks.Get(outside).Yaw, 5);

        var copy = new ShipConsoleBindings();
        copy.CameraLooks.CopyFrom(ship.CameraLooks);
        Assert.Equal(ship.CameraLooks.Get(Camera - shift), copy.CameraLooks.Get(Camera - shift));
    }

    [Fact]
    public void Looks_are_not_saved_and_a_load_starts_every_camera_at_rest()
    {
        var bindings = new ShipConsoleBindings();
        bindings.ToggleCamera(Console, 4, false, Camera);
        bindings.CameraLooks.Rotate(Camera, 100, -40, Rate, HalfPi);

        var saved = bindings.ToSnapshot();
        var only = Assert.Single(saved);
        Assert.Equal((Console, Camera), (only.Cell, only.Camera));
        Assert.Empty(only.Groups);

        var loaded = new ShipConsoleBindings();
        loaded.CameraLooks.Rotate(Camera, 10, 0, Rate, HalfPi);
        loaded.LoadSnapshot(saved, _ => (4, false), _ => ConsoleTargetKind.Camera);

        Assert.Equal(Camera, loaded.TryGet(Console)!.Camera);
        Assert.Equal(CameraLook.Rest, loaded.CameraLooks.Get(Camera));
    }

    [Fact]
    public void The_camera_settings_default_to_the_original_and_are_player_preferences()
    {
        Assert.Equal(90, Cvars.CameraPitchLimit.Default);
        Assert.Equal(0, Cvars.CameraEyeForward.Default);
        Assert.Equal(0, Cvars.SpeedConeFollowsView.Default);
        foreach (var cvar in new[] { Cvars.CameraPitchLimit, Cvars.CameraEyeForward, Cvars.SpeedConeFollowsView })
        {
            Assert.Contains(cvar, Cvars.All);
            Assert.True(cvar.Flags.HasFlag(CvarFlags.User));
            Assert.StartsWith("cl_", cvar.Name);
        }
    }

    [Fact]
    public void The_eye_stays_at_the_centre_by_default_as_in_the_original()
    {
        Assert.Null(CameraLook.EyeOutsideDistance(Cvars.CameraEyeForward.Default, 0.5f));
        Assert.Null(CameraLook.EyeOutsideDistance(-0.2, 0.5f));
        Assert.Null(CameraLook.EyeOutsideDistance(double.NaN, 0.5f));
    }

    [Theory]
    [InlineData(0.01, 0.5f, 0.51f)]
    [InlineData(0.25, 0.5f, 0.75f)]
    [InlineData(0.6, 0.5f, 1.1f)]
    [InlineData(0.25, 1f, 1.25f)]   // a deeper object model
    public void Any_eye_forward_setting_takes_the_eye_past_the_front_face(double forward, float halfDepth, float expected)
    {
        float distance = CameraLook.EyeOutsideDistance(forward, halfDepth)!.Value;

        Assert.Equal(expected, distance, 5);
        Assert.True(distance > halfDepth);
    }

    [Fact]
    public void The_largest_eye_forward_stays_in_the_cell_in_front()
    {
        // From the centre of a one-cell camera, the cell in front spans 0.5 to 1.5.
        float distance = CameraLook.EyeOutsideDistance(Cvars.CameraEyeForward.Max, 0.5f)!.Value;

        Assert.InRange(distance, 0.5f, 1.5f);
    }

    static void AssertLook(float yaw, float pitch, CameraLook look)
    {
        Assert.Equal(yaw, look.Yaw, 5);
        Assert.Equal(pitch, look.Pitch, 5);
    }
}
