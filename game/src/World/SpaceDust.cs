using Godot;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Faint specks drifting around the player, as in the original's space: they stay where they are in the world while
/// the player moves through them, which is what makes flying through empty space read as movement. The emitter rides
/// on the camera; the specks are in world space. Numbers are ours, tunable.
/// </summary>
public static class SpaceDust
{
    const float Reach = 30;

    public static CpuParticles3D Create()
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoColor = new Color(0.85f, 0.85f, 0.85f, 0.55f),
            VertexColorUseAsAlbedo = true,
        };
        return new CpuParticles3D
        {
            Name = "SpaceDust",
            Amount = 700,
            Lifetime = 24,
            Preprocess = 24,
            LocalCoords = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(Reach, Reach, Reach),
            Direction = Vector3.Up,
            Spread = 180,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 0.02f,
            InitialVelocityMax = 0.15f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.4f,
            // Fade in and out so specks never pop.
            ColorRamp = new Gradient
            {
                Offsets = [0f, 0.15f, 0.85f, 1f],
                Colors = [new Color(1, 1, 1, 0), Colors.White, Colors.White, new Color(1, 1, 1, 0)],
            },
            Mesh = new QuadMesh { Size = new Vector2(0.14f, 0.14f), Material = material },
        };
    }
}
