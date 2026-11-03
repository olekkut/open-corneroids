using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// The astronaut's eyes and hands: mouse look, WASD, Space/E up, Ctrl/Q down, Shift boost, N toggles noclip,
/// left button mines, right button places, F uses the targeted station, I/Tab opens the inventory, F5 saves.
/// Movement itself (inertia, collision) lives in <see cref="PlayerMotor"/>; what mining and placing actually do
/// depends on the <see cref="PlayMode"/> (survival or creative). This class owns every key so the two modes and
/// the windows can never fight over the same input.
/// </summary>
public partial class FlyCamera : Camera3D
{
    const float MouseSensitivity = 0.0025f;
    const float Reach = 8f;

    /// <summary>Exposed so a save system can persist velocity and noclip.</summary>
    public PlayerMotor Motor { get; } = new();
    VoxelWorld _world = null!;
    Hud _hud = null!;
    PlayMode _mode = null!;
    GameWindows? _windows;
    MeshInstance3D _highlight = null!;
    bool _hasTarget;
    bool _primaryHeld;
    Vector3I _target, _placeAt;
    /// <summary>Our own frame rate: <see cref="Engine.GetFramesPerSecond"/> averages whole seconds since startup, so
    /// right after a world loads it still reports the loading hitch ("FPS 1") for a while.</summary>
    double _fpsWindow;
    int _fpsFrames;
    string _fps = "--";

    public event Action? ExitRequested;
    /// <summary>F5. Raised even while a window is open: saving changes nothing the window shows.</summary>
    public event Action? SaveRequested;

    /// <param name="windows">The inventory and crafting windows; null in creative, which has neither.</param>
    public void Setup(VoxelWorld world, Hud hud, PlayMode mode, GameWindows? windows)
    {
        _world = world;
        _hud = hud;
        _mode = mode;
        _windows = windows;
        _highlight = new MeshInstance3D { Mesh = WireCube(), Visible = false, TopLevel = true };
        AddChild(_highlight);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    bool WindowOpen => _windows?.IsOpen == true;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } saveKey && saveKey.IsAction(GameInput.Save))
        {
            SaveRequested?.Invoke();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (WindowOpen)
        {
            WindowInput(@event);
            return;
        }

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
            case InputEventMouseButton { Pressed: true } when !captured:
                // The click that takes the mouse back is not also a mining click.
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                _primaryHeld = false;
                break;
            case InputEventMouseButton { Pressed: true } button:
                switch (button.ButtonIndex)
                {
                    case MouseButton.Left:
                        _primaryHeld = true;
                        _mode.PrimaryPressed(_hasTarget ? _target : null);
                        break;
                    case MouseButton.Right:
                        if (_hasTarget) _mode.Place(GlobalPosition, _placeAt);
                        break;
                    case MouseButton.WheelUp: _mode.Cycle(-1); break;
                    case MouseButton.WheelDown: _mode.Cycle(1); break;
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.PhysicalKeycode == Key.Escape)
                {
                    if (captured) Input.MouseMode = Input.MouseModeEnum.Visible;
                    else ExitRequested?.Invoke();
                }
                else if (key.IsAction(GameInput.Noclip))
                    Motor.Noclip = !Motor.Noclip;
                else if (key.IsAction(GameInput.Inventory))
                    OpenInventory();
                else if (key.IsAction(GameInput.Interact))
                    Interact();
                else if (key.PhysicalKeycode is >= Key.Key1 and <= Key.Key9)
                    _mode.Select((int)(key.PhysicalKeycode - Key.Key1));
                break;
        }
    }

    /// <summary>While a window is open only the keys that close it count; the windows take their own clicks.</summary>
    void WindowInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key &&
            (key.PhysicalKeycode == Key.Escape || key.IsAction(GameInput.Inventory)))
        {
            _windows!.Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public void OpenInventory()
    {
        if (_windows is null)
        {
            _hud.Flash("Creative mode has no inventory", warning: true);
            return;
        }
        StopActions();
        _windows.OpenInventory();
    }

    /// <summary>Opens a station's blueprints. Also used by <c>--open=crafting</c>, which needs no aiming.</summary>
    public void OpenCrafting(CraftingStation station, string title)
    {
        if (_windows is null) return;
        StopActions();
        _windows.OpenCrafting(station, title);
    }

    void Interact()
    {
        if (!_hasTarget) return;
        var item = _world.Palette.Item(_world.Get(_target));
        if (item.Get<CraftingStationComponent>() is not { } station)
        {
            _hud.Flash($"{item.Name} has no use");
            return;
        }
        if (_windows is null)
        {
            _hud.Flash("Crafting is off in creative mode", warning: true);
            return;
        }
        OpenCrafting(station.Station, item.Name);
    }

    /// <summary>A window opening mid-drill must not leave the drill running behind it.</summary>
    void StopActions()
    {
        _primaryHeld = false;
        _mode.Update(0, null, drilling: false);
    }

    public override void _Process(double delta)
    {
        // Movement keys are ignored while a window is open (the player drifts to a stop), so typing in a window
        // can never fly the player into something unseen.
        bool windowOpen = WindowOpen;
        var input = windowOpen ? Vector3.Zero : new Vector3(
            Axis(GameInput.Right, GameInput.Left),
            Axis(GameInput.Up, GameInput.Down),
            Axis(GameInput.Back, GameInput.Forward));
        var basis = GlobalTransform.Basis;
        var thrust = basis.X * input.X + Vector3.Up * input.Y + basis.Z * input.Z;
        GlobalPosition = Motor.Step(GlobalPosition, thrust, !windowOpen && Input.IsActionPressed(GameInput.Boost), (float)delta, _world);

        _hasTarget = VoxelRaycast.Cast(_world, GlobalPosition, -GlobalTransform.Basis.Z, Reach, out _target, out _placeAt);
        _highlight.Visible = _hasTarget;
        if (_hasTarget) _highlight.GlobalPosition = new Vector3(_target.X + 0.5f, _target.Y + 0.5f, _target.Z + 0.5f);

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (!captured) _primaryHeld = false;
        _mode.Update(delta, _hasTarget ? _target : null, drilling: _primaryHeld && captured && !windowOpen);

        // Windows of half a second or 120 frames, whichever ends first; the first frame (which may still carry the
        // world's setup) is not counted.
        if (_fpsFrames++ > 0) _fpsWindow += delta;
        if (_fpsWindow >= 0.5 || _fpsFrames > 120)
        {
            _fps = $"{(_fpsFrames - 1) / _fpsWindow:0}";
            (_fpsFrames, _fpsWindow) = (1, 0);
        }

        var p = GlobalPosition;
        _hud.SetStatus($"FPS {_fps}   position {p.X:0},{p.Y:0},{p.Z:0}" + _mode.Status +
                       (_hasTarget ? $"   looking at {_world.Palette.Item(_world.Get(_target)).Name}" : ""));
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
