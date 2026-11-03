using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Game.Player;
using OpenCorneroids.Game.Ui;

namespace OpenCorneroids.Game.World;

/// <summary>One play session: space environment, generated asteroids, the player camera and the HUD.</summary>
public partial class WorldView : Node3D
{
    public event Action? ExitRequested;

    /// <summary>Builds the world from a loaded itemset. Must be called once the view is in the scene tree.</summary>
    public bool Setup(LoadedItemset loaded)
    {
        var textures = BlockTextures.Create(loaded);
        var palette = BlockPalette.Build(loaded.Itemset!, textures);
        if (palette.Count == 0) return false;

        AddEnvironment();

        var world = new VoxelWorld { Name = "VoxelWorld" };
        world.Initialize(palette, textures.Opaque, textures.Cutout);
        AddChild(world);
        var (eye, target) = AsteroidGenerator.Generate(world, seed: 2012);
        world.RebuildAll();

        var header = loaded.Entry.Header;
        var hud = new Hud { Name = "Hud" };
        AddChild(hud);
        hud.Setup(palette, textures, $"{header.Name} by {header.Author}  [{loaded.View.PackId}]");

        var camera = new FlyCamera { Name = "Player", Position = eye };
        AddChild(camera);
        camera.LookAt(target, Vector3.Up);
        camera.Setup(world, hud);
        camera.ExitRequested += () => ExitRequested?.Invoke();
        camera.MakeCurrent();
        _world = world;
        _eye = eye;
        return true;
    }

    VoxelWorld? _world;
    Vector3 _eye;

    /// <summary>Places every placeable block in a spaced grid in front of the spawn point, using the same
    /// <see cref="VoxelWorld.Set"/> path as the player, so meshing, cutouts and block lights are exercised.</summary>
    public void Showcase()
    {
        if (_world is null) return;
        var blocks = _world.Palette.Indices.ToList();
        const int columns = 10;
        int x0 = Mathf.RoundToInt(_eye.X) - columns + 1, y0 = Mathf.RoundToInt(_eye.Y) + 4, z = Mathf.RoundToInt(_eye.Z) - 10;
        for (int i = 0; i < blocks.Count; i++)
            _world.Set(new Vector3I(x0 + i % columns * 2, y0 - i / columns * 2, z), blocks[i]);
    }

    void AddEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.01f, 0.01f, 0.02f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.6f, 0.75f),
            AmbientLightEnergy = 0.35f,
            GlowEnabled = true,
            GlowIntensity = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -40, 0), LightEnergy = 1.1f, LightColor = new Color(1f, 0.96f, 0.9f) });
        AddChild(Stars(900, seed: 7));
    }

    static MultiMeshInstance3D Stars(int count, int seed)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)seed };
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new SphereMesh { Radius = 0.6f, Height = 1.2f, RadialSegments = 4, Rings = 2 },
            InstanceCount = count,
        };
        for (int i = 0; i < count; i++)
        {
            var direction = new Vector3(rng.Randfn(), rng.Randfn(), rng.Randfn()).Normalized();
            var scale = rng.RandfRange(0.4f, 1.6f);
            multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), new Vector3(64, 64, 64) + direction * 600f));
        }
        return new MultiMeshInstance3D
        {
            Name = "Stars",
            Multimesh = multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.9f, 0.92f, 1f) },
        };
    }
}
