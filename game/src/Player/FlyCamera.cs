using Godot;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// Free-flying astronaut (no collision yet): mouse look, WASD, Space/E up, Ctrl/Q down, Shift boost.
/// Left click mines the targeted block, right click places the selected one.
/// </summary>
public partial class FlyCamera : Camera3D
{
    const float Speed = 8f;
    const float BoostMultiplier = 4f;
    const float MouseSensitivity = 0.0025f;
    const float Reach = 8f;

    VoxelWorld _world = null!;
    Hud _hud = null!;
    MeshInstance3D _highlight = null!;
    bool _hasTarget;
    Vector3I _target, _placeAt;

    public event Action? ExitRequested;

    public void Setup(VoxelWorld world, Hud hud)
    {
        _world = world;
        _hud = hud;
        _highlight = new MeshInstance3D { Mesh = WireCube(), Visible = false, TopLevel = true };
        AddChild(_highlight);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (@event)
        {
            case InputEventMouseMotion motion when captured:
                var rotation = Rotation;
                rotation.Y -= motion.Relative.X * MouseSensitivity;
                rotation.X = Mathf.Clamp(rotation.X - motion.Relative.Y * MouseSensitivity, -1.55f, 1.55f);
                rotation.Z = 0;
                Rotation = rotation;
                break;
            case InputEventMouseButton { Pressed: true } button when !captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            case InputEventMouseButton { Pressed: true } button:
                switch (button.ButtonIndex)
                {
                    case MouseButton.Left: Mine(); break;
                    case MouseButton.Right: Place(); break;
                    case MouseButton.WheelUp: _hud.Cycle(-1); break;
                    case MouseButton.WheelDown: _hud.Cycle(1); break;
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.PhysicalKeycode == Key.Escape)
                {
                    if (captured) Input.MouseMode = Input.MouseModeEnum.Visible;
                    else ExitRequested?.Invoke();
                }
                else if (key.PhysicalKeycode is >= Key.Key1 and <= Key.Key9)
                {
                    _hud.SelectSlot((int)(key.PhysicalKeycode - Key.Key1));
                }
                break;
        }
    }

    public override void _Process(double delta)
    {
        var input = new Vector3(
            Axis(GameInput.Right, GameInput.Left),
            Axis(GameInput.Up, GameInput.Down),
            Axis(GameInput.Back, GameInput.Forward));
        if (input != Vector3.Zero)
        {
            var basis = GlobalTransform.Basis;
            var move = basis.X * input.X + Vector3.Up * input.Y + basis.Z * input.Z;
            float speed = Speed * (Input.IsActionPressed(GameInput.Boost) ? BoostMultiplier : 1f);
            Position += move.Normalized() * speed * (float)delta;
        }

        _hasTarget = VoxelRaycast.Cast(_world, GlobalPosition, -GlobalTransform.Basis.Z, Reach, out _target, out _placeAt);
        _highlight.Visible = _hasTarget;
        if (_hasTarget) _highlight.GlobalPosition = new Vector3(_target.X + 0.5f, _target.Y + 0.5f, _target.Z + 0.5f);

        var p = GlobalPosition;
        _hud.SetStatus($"FPS {Engine.GetFramesPerSecond()}   position {p.X:0},{p.Y:0},{p.Z:0}" +
                       (_hasTarget ? $"   looking at {_world.Palette.Item(_world.Get(_target)).Name}" : ""));
    }

    void Mine()
    {
        if (!_hasTarget) return;
        var name = _world.Palette.Item(_world.Get(_target)).Name;
        _world.Set(_target, BlockPalette.Air);
        _hud.Flash($"Mined {name}");
    }

    void Place()
    {
        if (!_hasTarget) return;
        var body = new Vector3I(Mathf.FloorToInt(GlobalPosition.X), Mathf.FloorToInt(GlobalPosition.Y), Mathf.FloorToInt(GlobalPosition.Z));
        if (_placeAt == body || _world.Get(_placeAt) != BlockPalette.Air) return;
        if (_world.Set(_placeAt, _hud.Selected)) _hud.Flash($"Placed {_world.Palette.Item(_hud.Selected).Name}");
    }

    static float Axis(string positive, string negative) =>
        (Input.IsActionPressed(positive) ? 1f : 0f) - (Input.IsActionPressed(negative) ? 1f : 0f);

    static Mesh WireCube()
    {
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 1, 1, 0.9f) };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        const float h = 0.505f;
        Vector3[] c =
        [
            new(-h, -h, -h), new(h, -h, -h), new(h, -h, h), new(-h, -h, h),
            new(-h, h, -h), new(h, h, -h), new(h, h, h), new(-h, h, h),
        ];
        int[] edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
        foreach (var i in edges) mesh.SurfaceAddVertex(c[i]);
        mesh.SurfaceEnd();
        return mesh;
    }
}
