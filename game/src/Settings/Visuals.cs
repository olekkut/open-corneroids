using Godot;
using Rules = OpenCorneroids.Simulation.Settings.Cvars;

namespace OpenCorneroids.Game.Settings;

/// <summary>
/// The graphics preferences applied to what is on screen: dynamic lights (<c>cl_lighting</c>), how far blocks are drawn
/// (<c>cl_view_distance</c>), SSAO (<c>cl_ssao</c>) and brightness (<c>cl_gamma</c>). Lights and chunks join a group
/// when made, so a change reaches every one at once.
/// </summary>
public static class Visuals
{
    public const string LightGroup = "dynamic_lights";
    public const string ChunkGroup = "view_distance";

    static Simulation.Settings.CvarStore Cvars => GameSettings.Cvars;

    public static bool LightingOn => Cvars.Bool(Rules.Lighting);

    /// <summary>Metres blocks are drawn to: 8 × the original's view distance (Short 400, Medium 800, Far 1200). Ours.</summary>
    public static float DrawDistance => Cvars.Int(Rules.ViewDistance) * 8f;

    /// <summary>Marks a light as dynamic: hidden while lighting is off.</summary>
    public static T Light<T>(T light) where T : Light3D
    {
        light.AddToGroup(LightGroup);
        light.Visible = LightingOn;
        return light;
    }

    /// <summary>Marks a chunk or object mesh as drawn only within the view distance.</summary>
    public static T Chunk<T>(T mesh) where T : GeometryInstance3D
    {
        mesh.AddToGroup(ChunkGroup);
        mesh.VisibilityRangeEnd = DrawDistance;
        return mesh;
    }

    /// <summary>Brightness from gamma 0..50: 0.6 at 0 to 1 at 50, the default. Ours.</summary>
    public static float Brightness => 0.6f + 0.4f * Cvars.Int(Rules.Gamma) / 50f;

    /// <summary>Sets an environment's SSAO and brightness from the preferences.</summary>
    public static void Apply(Godot.Environment environment)
    {
        int ssao = Cvars.Int(Rules.Ssao);
        environment.SsaoEnabled = ssao > 0;
        // As in the original: low works at a quarter of the resolution, high at half; ours maps them to Godot's levels.
        RenderingServer.EnvironmentSetSsaoQuality(ssao >= 2 ? RenderingServer.EnvironmentSsaoQuality.High : RenderingServer.EnvironmentSsaoQuality.Low,
            ssao < 2, 0.5f, 2, 50, 300);
        environment.AdjustmentEnabled = true;
        environment.AdjustmentBrightness = Brightness;
    }

    /// <summary>Re-applies a changed setting to everything in <paramref name="tree"/>.</summary>
    public static void Changed(SceneTree tree, Simulation.Settings.Cvar cvar, Godot.Environment? environment)
    {
        if (cvar == Rules.Lighting) tree.CallGroup(LightGroup, Node3D.MethodName.SetVisible, LightingOn);
        else if (cvar == Rules.ViewDistance) tree.CallGroup(ChunkGroup, GeometryInstance3D.MethodName.SetVisibilityRangeEnd, DrawDistance);
        else if ((cvar == Rules.Ssao || cvar == Rules.Gamma) && environment is not null) Apply(environment);
    }
}
