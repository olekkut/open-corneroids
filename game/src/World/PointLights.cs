using Godot;
using OpenCorneroids.Content;

namespace OpenCorneroids.Game.World;

/// <summary>
/// How an itemset point light (a light block, a held or thrown torch) becomes a Godot light, in one place so they all
/// look alike. Ours: the original's falloff is not Godot's, so the range is 1.5 × the light's radius (at least 2 m) at
/// energy 2, picked by eye.
/// </summary>
public static class PointLights
{
    /// <summary>Brightness of every itemset light. Ours.</summary>
    public const float Energy = 2f;

    /// <summary>The Godot range for an itemset light radius. Ours.</summary>
    public static float Range(float radius) => float.IsFinite(radius) ? Math.Max(2f, radius * 1.5f) : 2f;

    public static Color ColorOf(Rgb8 color) => Color.Color8(color.R, color.G, color.B);

    /// <summary>A light with the itemset colour and radius, hidden while lighting is off (<see cref="Settings.Visuals.Light"/>).</summary>
    public static OmniLight3D Make(Rgb8 color, float radius) =>
        Settings.Visuals.Light(Set(new OmniLight3D(), color, radius));

    /// <summary>Gives <paramref name="light"/> the itemset colour and radius.</summary>
    public static OmniLight3D Set(OmniLight3D light, Rgb8 color, float radius)
    {
        light.LightColor = ColorOf(color);
        light.OmniRange = Range(radius);
        light.LightEnergy = Energy;
        return light;
    }
}
