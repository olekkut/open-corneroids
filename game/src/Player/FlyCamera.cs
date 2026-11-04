using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// The astronaut's eyes and hands: mouse look, WASD, Space/E up, Ctrl/Q down, Shift boost, N toggles noclip,
/// left button mines, right button places, R/T turn the multi-block to place, F uses the targeted station or door,
/// I/Tab opens the inventory, F5 saves.
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
    /// <summary>Ghost of the multi-block a right click would place: its footprint as a wire box, its model as the
    /// see-through textured model (so the player sees which way its faces point), tinted green when it fits and red
    /// when it doesn't.</summary>
    MeshInstance3D _ghostFootprint = null!, _ghostModel = null!;
    Material _ghostWireOk = null!, _ghostWireBad = null!;
    /// <summary>Ghost model materials by [placeable, cutout]: a cutout model (big window) keeps its holes.</summary>
    readonly ShaderMaterial[,] _ghostModelMaterials = new ShaderMaterial[2, 2];
    /// <summary>The block and orientation <see cref="_ghostModel"/>'s mesh was built for; rebuilt only when they change,
    /// not every time the aim moves (the mesh sits at origin zero and is moved).</summary>
    (ushort Block, Orientation Orientation)? _ghostMeshFor;
    /// <summary>The door callbacks, made once: <see cref="VoxelWorld.StepDoors"/> runs every frame.</summary>
    readonly Func<Func<Vector3I, bool>, bool> _doorBlocked;
    readonly Action<WorldObject> _doorRefused;

    public FlyCamera()
    {
        _doorBlocked = cells => PlayerMotor.BodyOverlaps(GlobalPosition, cells);
        _doorRefused = door => _hud.Flash($"{_world.Palette.Item(door.Block).Name} can't close: you are in the way", warning: true);
    }
    bool _hasTarget;
    bool _primaryHeld;
    BlockTarget _target;
    Vector3I _hit, _placeAt;
    /// <summary>Our own frame rate: <see cref="Engine.GetFramesPerSecond"/> averages whole seconds since startup, so
    /// right after a world loads it still reports the loading hitch ("FPS 1") for a while.</summary>
    double _fpsWindow;
    int _fpsFrames;
    string _fps = "--";

    /// <summary>The rules in play, e.g. for <c>--orientation</c> and <c>--preview</c> to set the selection.</summary>
    public PlayMode Mode => _mode;

    /// <summary>
    /// <c>--preview</c>: the aimed face held fixed (the solid cell and the empty cell in front of it) whatever the
    /// crosshair points at and however far away it is, so the camera can stand back far enough to frame a whole
    /// footprint. The highlight, the ghost and a right click all use it. The first mouse movement or key press lets go,
    /// and aiming is normal again.
    /// </summary>
    public (Vector3I Hit, Vector3I Before)? PinnedAim { get; set; }

    /// <summary>
    /// Screenshot runs (<c>--screenshot</c>): mouse and keyboard are ignored and the mouse is not captured, so stray
    /// input reaching the window while it renders (a wheel tick, a nudge of the mouse) can't change the selection or
    /// the view, and two runs with the same arguments give the same picture.
    /// </summary>
    public bool InputLocked
    {
        get => _inputLocked;
        set
        {
            _inputLocked = value;
            Input.MouseMode = value ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        }
    }
    bool _inputLocked;

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
        _ghostWireOk = GhostMaterial(new Color(0.35f, 1f, 0.45f, 0.9f));
        _ghostWireBad = GhostMaterial(new Color(1f, 0.3f, 0.25f, 0.9f));
        var ghostShader = GD.Load<Shader>("res://shaders/ghost.gdshader");
        for (int placeable = 0; placeable < 2; placeable++)
        for (int cutout = 0; cutout < 2; cutout++)
        {
            var material = new ShaderMaterial { Shader = ghostShader };
            material.SetShaderParameter("albedo_atlas", world.Textures.Albedo);
            material.SetShaderParameter("cutout", cutout == 1);
            // Red is pushed harder: "blocked" must read at a glance even over a red texture.
            material.SetShaderParameter("tint", placeable == 1 ? new Color(0.35f, 1f, 0.45f) : new Color(1f, 0.25f, 0.2f));
            material.SetShaderParameter("tint_amount", placeable == 1 ? 0.35f : 0.55f);
            _ghostModelMaterials[placeable, cutout] = material;
        }
        _ghostFootprint = new MeshInstance3D
        {
            Mesh = WireCube(), Visible = false, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _ghostModel = new MeshInstance3D
        {
            Visible = false, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ghostFootprint);
        AddChild(_ghostModel);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    bool WindowOpen => _windows?.IsOpen == true;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_inputLocked)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventMouseMotion or InputEventKey { Pressed: true }) PinnedAim = null;
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
                        if (_hasTarget) _mode.Place(GlobalPosition, _hit, _placeAt);
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
                else if (key.IsAction(GameInput.Spin))
                    Turn(o => o.NextSpin());
                else if (key.IsAction(GameInput.TurnUp))
                    Turn(o => o.NextUp());
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

    /// <summary>A right click at the current aim (the pinned one, if any); <c>--place</c> uses it.</summary>
    public void PlaceAtAim()
    {
        if (PinnedAim is { } pin) _mode.Place(GlobalPosition, pin.Hit, pin.Before);
        else if (VoxelRaycast.Cast(_world, GlobalPosition, -GlobalTransform.Basis.Z, Reach, out var hit, out var before))
            _mode.Place(GlobalPosition, hit, before);
        else _hud.Flash("Nothing aimed at to place against", warning: true);
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

    /// <summary>R/T. Only multi-blocks turn (the chunk grid has no orientation), so with anything else selected the
    /// key says so instead of claiming a turn nobody can see, and the hidden orientation can't surprise the next
    /// multi-block's ghost.</summary>
    void Turn(Func<Orientation, Orientation> turn)
    {
        if (!_world.Palette.IsObject(_mode.SelectedBlock))
        {
            _hud.Flash("Only multi-blocks can be turned");
            return;
        }
        var orientation = turn(_mode.Orientation);
        _mode.Orientation = orientation;
        _hud.Flash($"Orientation {orientation.Describe()}");
    }

    /// <summary>F: a door opens or closes (in either mode); a crafting station, single-cell or multi-block, opens crafting.</summary>
    void Interact()
    {
        if (!_hasTarget) return;
        if (_target.Object is { Door: not null } door)
        {
            _world.ToggleDoor(door);
            return;
        }
        var item = _world.Palette.Item(_world.BlockOf(_target));
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
        var input = windowOpen || _inputLocked ? Vector3.Zero : new Vector3(
            Axis(GameInput.Right, GameInput.Left),
            Axis(GameInput.Up, GameInput.Down),
            Axis(GameInput.Back, GameInput.Forward));
        var basis = GlobalTransform.Basis;
        var thrust = basis.X * input.X + Vector3.Up * input.Y + basis.Z * input.Z;
        GlobalPosition = Motor.Step(GlobalPosition, thrust, !windowOpen && !_inputLocked && Input.IsActionPressed(GameInput.Boost), (float)delta, _world);

        // Doors move whatever the player does; one never moves into the player's body (see StepDoors).
        _world.StepDoors((float)delta, _doorBlocked, _doorRefused);

        if (PinnedAim is { } pin)
        {
            (_hasTarget, _hit, _placeAt) = (_world.IsSolid(pin.Hit), pin.Hit, pin.Before);
            if (!_hasTarget) PinnedAim = null;   // the aimed block is gone (mined, or a door moved away)
        }
        else
        {
            _hasTarget = VoxelRaycast.Cast(_world, GlobalPosition, -GlobalTransform.Basis.Z, Reach, out _hit, out _placeAt);
        }
        if (_hasTarget) _target = _world.TargetAt(_hit);
        ShowHighlight();
        ShowGhost();

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (!captured) _primaryHeld = false;
        _mode.Update(delta, _hasTarget ? _target : null, drilling: _primaryHeld && captured && !windowOpen && !_inputLocked);

        // Windows of half a second or 120 frames, whichever ends first; the first frame (which may still carry the
        // world's setup) is not counted.
        if (_fpsFrames++ > 0) _fpsWindow += delta;
        if (_fpsWindow >= 0.5 || _fpsFrames > 120)
        {
            _fps = $"{(_fpsFrames - 1) / _fpsWindow:0}";
            (_fpsFrames, _fpsWindow) = (1, 0);
        }

        var p = GlobalPosition;
        bool turnable = _world.Palette.IsObject(_mode.SelectedBlock);
        _hud.SetStatus($"FPS {_fps}   position {p.X:0},{p.Y:0},{p.Z:0}" + _mode.Status +
                       (turnable ? $"   orientation {_mode.Orientation.Describe()}" : "") +
                       (_hasTarget ? $"   looking at {_world.Palette.Item(_world.BlockOf(_target)).Name}" : ""));
    }

    /// <summary>A wire box around the aimed grid block, or around the whole model of an aimed object.</summary>
    void ShowHighlight()
    {
        _highlight.Visible = _hasTarget;
        if (!_hasTarget) return;
        if (_target.Object is { } obj)
        {
            var box = obj.ModelBox;
            _highlight.GlobalTransform = new Transform3D(Basis.Identity.Scaled(box.Size), box.GetCenter());
        }
        else
        {
            var cell = _target.Cell;
            _highlight.GlobalTransform = new Transform3D(Basis.Identity, new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));
        }
    }

    void ShowGhost()
    {
        var plan = _hasTarget && !WindowOpen ? _mode.PlanObject(GlobalPosition, _hit, _placeAt) : null;
        _ghostFootprint.Visible = _ghostModel.Visible = plan is not null;
        if (plan is not { } p) return;
        var bounds = p.Shape.RotatedSize(p.Orientation).ToVector3().ToGodot();
        _ghostFootprint.GlobalTransform = new Transform3D(Basis.Identity.Scaled(bounds), p.Origin.ToGodot() + bounds * 0.5f);
        if (_ghostMeshFor != (p.Block, p.Orientation))
        {
            _ghostModel.Mesh = _world.ModelMesh(p.Block, p.Orientation, _ghostModelMaterials[1, 0]);
            _ghostMeshFor = (p.Block, p.Orientation);
        }
        // The mesh is built at origin zero; it is moved to the plan's origin and scaled a hair about the model's centre
        // so the ghost does not z-fight with the faces it touches.
        var model = p.Shape.ModelBox(Simulation.Shapes.Int3.Zero, p.Orientation);
        var size = model.Size.ToGodot();
        var center = model.Center.ToGodot();
        var scale = new Vector3((size.X + 0.02f) / size.X, (size.Y + 0.02f) / size.Y, (size.Z + 0.02f) / size.Z);
        _ghostModel.GlobalTransform = new Transform3D(Basis.Identity.Scaled(scale), p.Origin.ToGodot() + center - center * scale);
        _ghostFootprint.MaterialOverride = p.Placeable ? _ghostWireOk : _ghostWireBad;
        _ghostModel.MaterialOverride = _ghostModelMaterials[p.Placeable ? 1 : 0, _world.Palette.IsCutout(p.Block) ? 1 : 0];
    }

    static StandardMaterial3D GhostMaterial(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = color,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

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
