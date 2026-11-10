using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Physics;
using Rules = OpenCorneroids.Simulation.Settings.Cvars;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Torches in flight, as in the original (the rules are <see cref="Torches"/>'): each is the torch's icon as a small
/// camera-facing sprite (0.2 m across, drawn within <c>cl_torch_draw_distance</c>, 32 m) with a point light of the
/// torch's colour whose radius is ln(milliseconds left), flickering. They slow to a stop, slide along the world and
/// ships, burn out after their <c>burningTime</c>, can't be picked up and are not saved. Ours: only the
/// <c>cl_torch_light_limit</c> torches nearest the camera cast light, to spare the renderer.
/// </summary>
public partial class ThrownTorches : Node3D
{
    /// <summary>Drawn this big, in metres, as in the original.</summary>
    const float SpriteSize = 0.2f;

    sealed class Burning
    {
        public ThrownTorch State;
        public required Sprite3D Sprite;
        public required OmniLight3D Light;
    }

    readonly List<Burning> _torches = new();
    readonly List<IGridShape> _ships = new();
    readonly List<(float Distance, Burning Torch)> _byDistance = new();
    readonly Random _flicker = new();
    GridSet _grids = null!;
    ItemIcons _icons = null!;

    static Simulation.Settings.CvarStore Cvars => Settings.GameSettings.Cvars;

    public void Setup(GridSet grids, ItemIcons icons) => (_grids, _icons) = (grids, icons);

    /// <summary>How many torches burn now.</summary>
    public int Count => _torches.Count;

    /// <summary>
    /// A torch leaves the hand at <paramref name="eye"/> along <paramref name="forward"/> (<see cref="Torches.Throw"/>);
    /// over <c>sv_torch_limit</c> the oldest go out. The caller has already taken the torch and its power.
    /// </summary>
    public void Throw(ItemDef torch, Vector3 eye, Vector3 forward, Vector3 carrierVelocity)
    {
        if (Torches.Throw(torch, eye.ToNumerics(), forward.ToNumerics(), carrierVelocity.ToNumerics(),
                Torches.Tuning.From(Cvars)) is not { } state)
            return;
        var texture = _icons.Of(torch);
        var sprite = new Sprite3D
        {
            Texture = texture,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Shaded = false,
            PixelSize = SpriteSize / Math.Max(1, texture?.GetWidth() ?? 1),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = Cvars.Float(Rules.TorchDrawDistance),
        };
        var light = PointLights.Set(new OmniLight3D(), state.Color, Torches.LightRadius(state.RemainingMs, 0, 0));
        light.Visible = false;   // _Process decides, by distance and the light limit
        AddChild(sprite);
        AddChild(light);
        sprite.GlobalPosition = eye;
        light.GlobalPosition = eye;
        _torches.Add(new Burning { State = state, Sprite = sprite, Light = light });
        DropOverLimit();
    }

    public override void _Process(double delta)
    {
        if (_torches.Count == 0) return;
        var tuning = Torches.Tuning.From(Cvars);
        DropOverLimit();
        var world = _grids.CaptureShapes(_ships);
        float flicker = (float)tuning.Flicker;
        float drawDistance = Cvars.Float(Rules.TorchDrawDistance);
        for (int i = _torches.Count - 1; i >= 0; i--)
        {
            var torch = _torches[i];
            var (move, burning) = Torches.Step(ref torch.State, delta, tuning);
            if (!burning)
            {
                Remove(i);
                continue;
            }
            Torches.Collide(ref torch.State, move, world, _ships, tuning.Radius);
            var at = torch.State.Position.ToGodot();
            torch.Sprite.GlobalPosition = at;
            torch.Sprite.VisibilityRangeEnd = drawDistance;
            torch.Light.GlobalPosition = at;
            torch.Light.OmniRange = PointLights.Range(Torches.LightRadius(torch.State.RemainingMs, _flicker.NextDouble(), flicker));
        }
        ShowNearestLights();
    }

    /// <summary>Lights the <c>cl_torch_light_limit</c> torches nearest the camera (Ours), while lighting is on.</summary>
    void ShowNearestLights()
    {
        int limit = Settings.Visuals.LightingOn ? Cvars.Int(Rules.TorchLightLimit) : 0;
        if (limit >= _torches.Count)
        {
            foreach (var torch in _torches) torch.Light.Visible = true;
            return;
        }
        var camera = GetViewport()?.GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        _byDistance.Clear();
        foreach (var torch in _torches) _byDistance.Add((torch.Light.GlobalPosition.DistanceSquaredTo(camera), torch));
        _byDistance.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        for (int i = 0; i < _byDistance.Count; i++) _byDistance[i].Torch.Light.Visible = i < limit;
    }

    /// <summary><c>sv_torch_limit</c>: the oldest go out first (no limit by default, as in the original).</summary>
    void DropOverLimit()
    {
        int over = Torches.OverLimit(_torches.Count, Cvars.Int(Rules.TorchLimit));
        for (int i = over - 1; i >= 0; i--) Remove(i);
    }

    void Remove(int index)
    {
        _torches[index].Sprite.QueueFree();
        _torches[index].Light.QueueFree();
        _torches.RemoveAt(index);
    }
}
