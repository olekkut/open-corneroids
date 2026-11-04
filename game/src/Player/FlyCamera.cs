using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;
using Num = System.Numerics;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// The astronaut's eyes and hands: mouse look, WASD, Space/E up, Ctrl/Q down, Shift boost, N toggles noclip,
/// left button mines, right button places, R/T turn the multi-block to place, F uses the targeted station, door or
/// console, I/Tab opens the inventory, F5 saves. Aiming, mining and placing work on every grid (the world and the ships)
/// through <see cref="GridSet"/>. Seated at a ship's console the same keys fly the ship instead (see
/// <see cref="PilotControls"/>).
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
    GridSet _grids = null!;
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
    /// <summary>The door callbacks, made once: <see cref="BlockGrid.StepDoors"/> runs every frame for every grid.
    /// <see cref="_doorBlocked"/> checks the body at <see cref="_doorEye"/>, set just before the doors step.</summary>
    readonly Action<WorldObject> _doorRefused;
    readonly Func<BlockGrid, Func<Vector3I, bool>, bool> _doorBlocked;
    Vector3 _doorEye;

    /// <summary>Pilot's top speed request, m/s; Shift multiplies it by <see cref="PilotBoost"/>. Ours, tunable.</summary>
    public const float PilotSpeed = 30f;
    public const float PilotBoost = 2f;
    /// <summary>Turn rate a held key asks for, and the most the mouse may ask for, rad/s. Ours, tunable.</summary>
    public const float PilotTurnRate = 0.9f, MaxTurnRate = 1.8f;
    /// <summary>Time constant smoothing the mouse's turn request, seconds, so a mouse that reports only some frames
    /// still asks for a steady turn.</summary>
    const float MouseSmoothing = 0.08f;
    /// <summary>The pilot's eye sits this far above the console's centre, along the console's up (see <see cref="SeatEye"/>).</summary>
    const float SeatHeight = 0.9f;
    /// <summary>How far behind the console's centre the eye moves when there is no room above it.</summary>
    const float SeatBack = 1f;

    /// <summary>Where the pilot sits: a console block (a grid cell, or an object) of a ship.</summary>
    sealed record Seat(ShipGrid Ship, Vector3I Cell, WorldObject? Object);

    public enum PilotView { Seat, Chase, Fixed }

    Seat? _seat;
    Vector2 _mouseDelta;
    Vector3 _mouseRate;
    /// <summary><c>--hold</c>: controls held one after another as if pressed, with how long the first has run.</summary>
    readonly List<(HashSet<string> Actions, float Seconds)> _holds = [];
    float _holdTime;

    /// <summary>The ship being piloted, or null when standing.</summary>
    public ShipGrid? PilotShip => _seat?.Ship;

    /// <summary>How the camera follows the ship while piloting. <see cref="PilotView.Fixed"/> (a screenshot hook) leaves
    /// it where it was when the pilot sat down.</summary>
    public PilotView View { get; set; } = PilotView.Seat;

    /// <summary>Inertial dampeners: the flight assistant also brings the ship to rest when nothing is asked. Z toggles.</summary>
    public bool Dampeners { get; set; } = true;

    public FlyCamera()
    {
        _doorRefused = door => _hud.Flash($"{_grids.Palette.Item(door.Block).Name} can't close: you are in the way", warning: true);
        _doorBlocked = (grid, cells) => PlayerMotor.BodyOverlaps(grid, _doorEye, cells);
    }
    bool _hasTarget;
    bool _primaryHeld;
    BlockTarget _target;
    Aim _aim;
    /// <summary>Our own frame rate: <see cref="Engine.GetFramesPerSecond"/> averages whole seconds since startup, so
    /// right after a world loads it still reports the loading hitch ("FPS 1") for a while.</summary>
    double _fpsWindow;
    int _fpsFrames;
    string _fps = "--";

    /// <summary>The rules in play, e.g. for <c>--orientation</c> and <c>--preview</c> to set the selection.</summary>
    public PlayMode Mode => _mode;

    /// <summary>
    /// <c>--preview</c>: the aimed face held fixed (the grid, its solid cell and the empty cell in front of it) whatever
    /// the crosshair points at and however far away it is, so the camera can stand back far enough to frame a whole
    /// footprint. The highlight, the ghost, a right click and F all use it. The first mouse movement or key press lets
    /// go, and aiming is normal again.
    /// </summary>
    public Aim? PinnedAim { get; set; }

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
    public void Setup(GridSet grids, Hud hud, PlayMode mode, GameWindows? windows)
    {
        _grids = grids;
        var world = grids.World;
        _hud = hud;
        _mode = mode;
        _windows = windows;
        _mode.Push = kick => Motor.Velocity += kick;
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
        if (_seat is not null)
        {
            PilotInput(@event);
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
                        if (!_mode.UseSelected() && _hasTarget) _mode.Place(GlobalPosition, _aim);
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

    /// <summary>Seated: the mouse steers (its motion is collected for <see cref="PilotControls"/>), F or Esc stands up,
    /// Z toggles the dampeners and V the view. Building is off: the hands are on the controls.</summary>
    void PilotInput(InputEvent @event)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (@event)
        {
            case InputEventMouseMotion motion when captured:
                _mouseDelta += motion.Relative;
                break;
            case InputEventMouseButton { Pressed: true } when !captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            // Held to fire the ship's guns (see PilotProcess).
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } fire:
                _primaryHeld = fire.Pressed;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.PhysicalKeycode == Key.Escape || key.IsAction(GameInput.Interact))
                    StandUp();
                else if (key.IsAction(GameInput.Dampeners))
                {
                    Dampeners = !Dampeners;
                    _hud.Flash($"Inertial dampeners {(Dampeners ? "on" : "off")}");
                }
                else if (key.IsAction(GameInput.PilotView))
                {
                    View = View == PilotView.Seat ? PilotView.Chase : PilotView.Seat;
                    _hud.Flash(View == PilotView.Seat ? "Seat view" : "Chase view");
                }
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
        if (PinnedAim is { } pin) _mode.Place(GlobalPosition, pin);
        else if (_grids.Raycast(GlobalPosition, -GlobalTransform.Basis.Z, Reach, out var aim)) _mode.Place(GlobalPosition, aim);
        else _hud.Flash("Nothing aimed at to place against", warning: true);
    }

    /// <summary>F at the current aim (the pinned one, if any); <c>--launch</c> uses it on a console.</summary>
    public void InteractAtAim()
    {
        if (PinnedAim is { } pin && _grids.Contains(pin.Grid) && pin.Grid.IsSolid(pin.Hit))
            (_hasTarget, _aim, _target) = (true, pin, pin.Grid.TargetAt(pin.Hit));
        Interact();
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
        if (!_grids.Palette.IsObject(_mode.SelectedBlock))
        {
            _hud.Flash("Only multi-blocks can be turned");
            return;
        }
        var orientation = turn(_mode.Orientation);
        _mode.Orientation = orientation;
        _hud.Flash($"Orientation {orientation.Describe()}");
    }

    /// <summary>
    /// F: a door opens or closes (in either mode); a console of the static world launches its structure as a ship, a
    /// ship's console is for piloting; a crafting station, single-cell or multi-block, opens crafting.
    /// </summary>
    void Interact()
    {
        if (!_hasTarget) return;
        if (_target.Object is { Door: not null } door)
        {
            _target.Grid.ToggleDoor(door);
            return;
        }
        var item = _grids.Palette.Item(_target.Grid.BlockOf(_target));
        if (item.Get<ConsoleComponent>() is not null)
        {
            UseConsole(_target);
            return;
        }
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

    /// <summary>F on a console: launch from the world, pilot on a ship.</summary>
    void UseConsole(BlockTarget console)
    {
        if (console.Grid is ShipGrid ship)
        {
            Pilot(ship, console);
            return;
        }
        var (launched, message) = _grids.LaunchShip(console);
        _hud.Flash(message, warning: launched is null, seconds: 4);
        // The structure left the grid it was aimed in; aim again next frame rather than at a stale target.
        PinnedAim = null;
        _hasTarget = false;
    }

    /// <summary>F on a ship's console: sit down and fly it.</summary>
    void Pilot(ShipGrid ship, BlockTarget console)
    {
        var cell = console.Object is { } obj ? obj.Origin.ToGodot() : console.Cell;
        Sit(ship, cell);
    }

    /// <summary>
    /// Sits at the console at <paramref name="cell"/> of <paramref name="ship"/> (a console grid block there, or a
    /// console object whose origin it is) and takes the controls. Returns false when there is no console there.
    /// </summary>
    public bool Sit(ShipGrid ship, Vector3I cell)
    {
        if (ConsoleAt(ship, cell) is not { } seat) return false;
        StopActions();
        PinnedAim = null;
        _hasTarget = false;
        _highlight.Visible = _ghostFootprint.Visible = _ghostModel.Visible = false;
        _seat = seat;
        _hud.SelectionVisible = false;
        (_mouseDelta, _mouseRate) = (Vector2.Zero, Vector3.Zero);
        Motor.Velocity = Motor.Drift = Vector3.Zero;
        _hud.SetControls("Piloting: W/S forward/back · A/D strafe · Space/Ctrl up/down · mouse or arrows pitch/yaw · Q/E roll · " +
                         "Shift boost · Z dampeners · V view · F or Esc stand up   F5 save");
        _hud.Flash(FormattableString.Invariant($"Piloting {ship.Name}: {ship.BlockCount} blocks, mass {ship.Mass.Mass:0.#}"), seconds: 3);
        FollowShip();
        return true;
    }

    /// <summary>The seat at a console of <paramref name="ship"/> at <paramref name="cell"/> (a console grid block, or the
    /// origin of a console object), or null when no console is there (any more).</summary>
    Seat? ConsoleAt(ShipGrid ship, Vector3I cell)
    {
        var obj = ship.Objects.FirstOrDefault(o => o.Origin.ToGodot() == cell);
        var block = obj?.Block ?? ship.Get(cell);
        return block != BlockPalette.Air && _grids.Palette.Item(block).Get<ConsoleComponent>() is not null ? new Seat(ship, cell, obj) : null;
    }

    /// <summary>
    /// Leaves the seat: the player stands at the nearest free spot beside the console (<see cref="StandingSpot"/>),
    /// moving with the ship's velocity there, looking the way the camera did (without roll). That velocity is the
    /// motor's <see cref="PlayerMotor.Drift"/>, which is not damped, so the player keeps drifting with the ship (until
    /// they thrust against it or hit something) instead of being left behind within a second.
    /// </summary>
    public void StandUp()
    {
        if (_seat is null) return;
        var (eye, velocity) = StandingSpot();
        var forward = -GlobalTransform.Basis.Z;
        _seat = null;
        _hud.SelectionVisible = true;
        GlobalPosition = eye;
        Rotation = LevelRotation(forward);
        Motor.Velocity = Motor.Drift = velocity;
        _hud.SetControls(_mode.Controls + "   F5 save");
        _hud.Flash("Standing");
    }

    /// <summary>Pitch and yaw (no roll) that look along <paramref name="forward"/>, as mouse look keeps them.</summary>
    public static Vector3 LevelRotation(Vector3 forward)
    {
        forward = forward.Normalized();
        return new Vector3(Mathf.Clamp(Mathf.Asin(Mathf.Clamp(forward.Y, -1, 1)), -1.55f, 1.55f), Mathf.Atan2(-forward.X, -forward.Z), 0);
    }

    /// <summary>
    /// Where the pilot would stand up: the first spot, nearest first, beside the console (above it, behind, to the
    /// sides, in front, below, in the console's own axes) where the body is free of every grid, with the ship's velocity
    /// at that point. Saving while piloting stores this, so a load starts standing there.
    /// </summary>
    public (Vector3 Eye, Vector3 Velocity) StandingSpot()
    {
        if (_seat is not { } seat) return (GlobalPosition, Motor.Velocity);
        var ship = seat.Ship;
        var frame = ConsoleFrame(seat);
        var center = SeatCenter(seat);
        var lift = new Vector3(0, PlayerMotor.EyeHeight, 0);
        Vector3[] directions = [frame.Y, frame.Z, frame.X, -frame.X, -frame.Z, -frame.Y];
        for (float distance = 1; distance <= 4; distance += 0.5f)
            foreach (var direction in directions)
            {
                var eye = ship.ToGlobal(center + direction * distance) + lift;
                if (_grids.All.All(g => !PlayerMotor.BodyOverlaps(g, eye, g.IsSolid))) return (eye, ship.PointVelocity(eye));
            }
        var above = ship.ToGlobal(center + frame.Y * 2) + lift;
        return (above, ship.PointVelocity(above));
    }

    /// <summary>The console's axes in the ship's local frame: right, up, back (the pilot looks along −Z). A console
    /// grid block has no orientation, so it faces the ship's −Z; a console object is turned by its orientation. Ours.</summary>
    static Basis ConsoleFrame(Seat seat) => seat.Object is { } obj
        ? new Basis(obj.Orientation.Apply(Num.Vector3.UnitX).ToGodot(), obj.Orientation.Apply(Num.Vector3.UnitY).ToGodot(),
            obj.Orientation.Apply(Num.Vector3.UnitZ).ToGodot())
        : Basis.Identity;

    /// <summary>
    /// The pilot's eye, ship-local: <see cref="SeatHeight"/> above the console's centre, or, when that point is inside a
    /// solid cell of the ship (a roof or a dashboard right over the console), the first free one of: behind the console
    /// at that height, straight behind it, a little above its centre. If all are solid the first is used anyway (the
    /// view then looks through the hull from inside a block). Ours.
    /// </summary>
    static Vector3 SeatEye(Seat seat, Basis frame)
    {
        var center = SeatCenter(seat);
        Span<Vector3> candidates =
        [
            center + frame.Y * SeatHeight,
            center + frame.Z * SeatBack + frame.Y * SeatHeight,
            center + frame.Z * SeatBack,
            center + frame.Y * 0.55f,
        ];
        foreach (var eye in candidates)
            if (!seat.Ship.IsSolid(new Vector3I(Mathf.FloorToInt(eye.X), Mathf.FloorToInt(eye.Y), Mathf.FloorToInt(eye.Z)))) return eye;
        return candidates[0];
    }

    static Vector3 SeatCenter(Seat seat) =>
        seat.Object is { } obj ? obj.ModelBox.GetCenter() : seat.Cell + new Vector3(0.5f, 0.5f, 0.5f);

    /// <summary>
    /// What the pilot asks of the ship this frame, in ship-local axes: W/S, A/D and Space/Ctrl ask for a velocity
    /// along the console's forward, right and up (<see cref="PilotSpeed"/>, Shift for more), the mouse or the arrow keys
    /// for pitch and yaw and Q/E for roll (<see cref="PilotTurnRate"/>). Zero when not piloting. Consumes the mouse
    /// motion collected since the last call.
    /// </summary>
    public PilotRequest PilotControls(float delta)
    {
        if (_seat is not { } seat)
        {
            _mouseDelta = Vector2.Zero;
            return default;
        }
        var move = new Vector3(Axis(GameInput.Right, GameInput.Left), Axis(GameInput.ShipUp, GameInput.ShipDown),
            Axis(GameInput.Back, GameInput.Forward));
        if (move.LengthSquared() > 1) move = move.Normalized();
        float speed = PilotSpeed * (Held(GameInput.Boost) ? PilotBoost : 1);
        var keys = new Vector3(Axis(GameInput.PitchUp, GameInput.PitchDown), Axis(GameInput.YawLeft, GameInput.YawRight),
            Axis(GameInput.RollLeft, GameInput.RollRight)) * PilotTurnRate;
        // Mouse up (negative Y) is nose up, a positive turn about the console's right axis; mouse right yaws right.
        var mouse = delta > 0 ? new Vector3(-_mouseDelta.Y, -_mouseDelta.X, 0) * (MouseSensitivity / delta) : Vector3.Zero;
        _mouseDelta = Vector2.Zero;
        _mouseRate = _mouseRate.Lerp(mouse, 1 - Mathf.Exp(-Math.Max(0, delta) / MouseSmoothing));
        var turn = keys + _mouseRate;
        if (turn.Length() > MaxTurnRate) turn = turn.Normalized() * MaxTurnRate;
        var frame = ConsoleFrame(seat);
        return new PilotRequest((frame * (move * speed)).ToNumerics(), (frame * turn).ToNumerics(), Dampeners);
    }

    /// <summary>Puts the camera where the view mode has it: on the seat looking along the console's forward, or behind
    /// and above the ship looking at its centre of mass; <see cref="PilotView.Fixed"/> leaves it.</summary>
    void FollowShip()
    {
        if (_seat is not { } seat) return;
        var ship = seat.Ship;
        var frame = ConsoleFrame(seat);
        switch (View)
        {
            case PilotView.Seat:
                GlobalTransform = ship.GlobalTransform * new Transform3D(frame, SeatEye(seat, frame));
                break;
            case PilotView.Chase:
                var basis = ship.GlobalTransform.Basis.Orthonormalized() * frame;
                var com = ship.ToGlobal(ship.Mass.CenterOfMass.ToGodot());
                float distance = ship.LocalExtent.Radius * 2.2f + 4;
                GlobalPosition = com + basis.Z * distance + basis.Y * (distance * 0.35f);
                LookAt(com, basis.Y);
                break;
        }
    }

    /// <summary>The <c>--hold</c> name for the primary mouse button, which has no input action of its own.</summary>
    public const string PrimaryHold = "primary";

    /// <summary>The primary button is down: really (with the mouse captured, outside a screenshot run) or by --hold.</summary>
    bool PrimaryDown => (_primaryHeld && Input.MouseMode == Input.MouseModeEnum.Captured && !_inputLocked) ||
                        (_holds.Count > 0 && _holds[0].Actions.Contains(PrimaryHold));

    /// <summary>
    /// <c>--hold</c>: holds each group of actions for its seconds, one group after another, as if their keys were down
    /// (the screenshot lock ignores the real keyboard, not these). Unknown action names are dropped with a warning.
    /// </summary>
    public void Hold(IEnumerable<(string[] Actions, float Seconds)> holds)
    {
        foreach (var (actions, seconds) in holds)
        {
            var known = new HashSet<string>();
            foreach (var action in actions)
            {
                // The primary mouse button has no input action; "primary" holds it (shoot, drill, fire the guns).
                if (action == PrimaryHold) known.Add(action);
                else if (!InputMap.HasAction(action)) GD.PushWarning($"--hold: there is no action '{action}'");
                else
                {
                    known.Add(action);
                    // Held as asked, but a pilot's hands are on the ship's controls: say so rather than do nothing quietly.
                    if (_seat is not null && action is GameInput.Up or GameInput.Down)
                        GD.PushWarning($"--hold: '{action}' moves a standing player only; piloting uses {GameInput.ShipUp}/{GameInput.ShipDown}");
                }
            }
            _holds.Add((known, seconds));
        }
        _holdTime = 0;
    }

    /// <summary>The action is held: by a <c>--hold</c> now running, or by the player's own key (unless input is locked or
    /// a window is open).</summary>
    bool Held(string action) =>
        (_holds.Count > 0 && _holds[0].Actions.Contains(action)) || (!_inputLocked && !WindowOpen && Input.IsActionPressed(action));

    void AdvanceHolds(float delta)
    {
        if (_holds.Count == 0) return;
        _holdTime += delta;
        if (_holdTime < _holds[0].Seconds) return;
        GD.Print(FormattableString.Invariant($"--hold: released {string.Join("+", _holds[0].Actions)} after {_holds[0].Seconds:0.##} s"));
        _holdTime -= _holds[0].Seconds;
        _holds.RemoveAt(0);
    }

    /// <summary>A window opening mid-drill must not leave the drill running behind it.</summary>
    void StopActions()
    {
        _primaryHeld = false;
        _mode.Update(0, null, drilling: false);
    }

    public override void _Process(double delta)
    {
        AdvanceHolds((float)Math.Min(delta, ShipPhysics.MaxFrame));
        // The ship went away, or its console did (mined from outside): the pilot is standing again.
        if (_seat is { } seat && (!_grids.Contains(seat.Ship) || ConsoleAt(seat.Ship, seat.Cell) is null))
        {
            if (_grids.Contains(seat.Ship)) StandUp();
            else
            {
                _seat = null;
                _hud.SelectionVisible = true;
            }
        }
        if (_seat is not null)
        {
            PilotProcess(delta);
            return;
        }

        // Movement keys are ignored while a window is open (the player drifts to a stop), so typing in a window
        // can never fly the player into something unseen.
        bool windowOpen = WindowOpen;
        var input = new Vector3(
            Axis(GameInput.Right, GameInput.Left),
            Axis(GameInput.Up, GameInput.Down),
            Axis(GameInput.Back, GameInput.Forward));
        var basis = GlobalTransform.Basis;
        var thrust = basis.X * input.X + Vector3.Up * input.Y + basis.Z * input.Z;
        // Boosting costs suit power in survival, and only while actually thrusting.
        bool boost = Held(GameInput.Boost) && thrust != Vector3.Zero && _mode.TryBoost(delta);
        GlobalPosition = Motor.Step(GlobalPosition, thrust, boost, (float)delta, _grids);

        // Doors move whatever the player does; one never moves into the player's body (see StepDoors), checked in
        // each grid's own cells.
        _doorEye = GlobalPosition;
        _grids.World.StepDoors((float)delta, _doorBlocked, _doorRefused);
        var ships = _grids.Ships;
        for (int i = 0; i < ships.Count; i++) ships[i].StepDoors((float)delta, _doorBlocked, _doorRefused);

        if (PinnedAim is { } pin)
        {
            // The aimed block is gone (mined, a door moved away, or its ship was launched or emptied): aim normally.
            (_hasTarget, _aim) = (_grids.Contains(pin.Grid) && pin.Grid.IsSolid(pin.Hit), pin);
            if (!_hasTarget) PinnedAim = null;
        }
        else
        {
            _hasTarget = _grids.Raycast(GlobalPosition, -GlobalTransform.Basis.Z, Reach, out _aim);
        }
        if (_hasTarget) _target = _aim.Grid.TargetAt(_aim.Hit);
        ShowHighlight();
        ShowGhost();

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (!captured) _primaryHeld = false;
        _mode.View = (GlobalPosition, -GlobalTransform.Basis.Z, Motor.Velocity);
        _mode.Update(delta, _hasTarget ? _target : null, drilling: PrimaryDown && !windowOpen);

        CountFrame(delta);

        var p = GlobalPosition;
        bool turnable = _grids.Palette.IsObject(_mode.SelectedBlock);
        _hud.SetStatus($"FPS {_fps}   position {p.X:0},{p.Y:0},{p.Z:0}" + _mode.Status +
                       (turnable ? $"   orientation {_mode.Orientation.Describe()}" : "") +
                       (_hasTarget ? $"   looking at {_grids.Palette.Item(_target.Grid.BlockOf(_target)).Name}{ShipNote(_target.Grid)}" : ""));
    }

    /// <summary>
    /// A frame in the pilot's seat: the camera follows the ship (which <see cref="ShipPhysics"/> has already moved this
    /// frame), doors keep moving (a seated pilot is never in their way), nothing is aimed at or drilled, and the status
    /// line shows the flight readout.
    /// </summary>
    void PilotProcess(double delta)
    {
        var ship = _seat!.Ship;
        FollowShip();
        // Far outside every grid: the seated body blocks no door.
        _doorEye = new Vector3(-1e5f, -1e5f, -1e5f);
        _grids.World.StepDoors((float)delta, _doorBlocked, _doorRefused);
        var ships = _grids.Ships;
        for (int i = 0; i < ships.Count; i++) ships[i].StepDoors((float)delta, _doorBlocked, _doorRefused);
        _mode.Update(delta, null, drilling: false);
        // The primary button fires every gun on the ship (the "automatic" control; console button binding comes later).
        if (PrimaryDown && !WindowOpen)
            _mode.Shots?.FireGuns(ship, Time.GetTicksMsec() / 1000.0);
        CountFrame(delta);

        var (supply, demand, satisfaction) = ship.Systems.PowerSummary;
        var (position, heading, pitch, roll, turn) = FlightReadout(_seat);
        _hud.SetStatus(FormattableString.Invariant(
            $"FPS {_fps}   piloting {ship.Name}   position {position.X:0},{position.Y:0},{position.Z:0}   speed {ship.LinearVelocity.Length():0.0} m/s   ") +
            $"heading {heading}°  pitch {Signed(pitch)}°  roll {Signed(roll)}°   " +
            $"turning pitch {Signed(turn.X)}  yaw {Signed(turn.Y)}  roll {Signed(turn.Z)} °/s\n" +
            FormattableString.Invariant($"mass {ship.Mass.Mass:0.#}   power {supply:0.#}/{demand:0.#} ({satisfaction * 100:0}%)   ") +
            $"thrusters {ship.Systems.Firing}/{ship.Systems.Thrusters.Count} firing   dampeners {(Dampeners ? "on" : "off")}   " +
            $"view {View.ToString().ToLowerInvariant()}");
    }

    /// <summary>
    /// The pilot's instruments, all in the world's frame: where the ship's centre of mass is; the console's heading
    /// (compass degrees: 0 along the world's −Z, 90 along +X, counting up as the ship turns right), pitch (nose above
    /// the world's horizontal plane) and roll (right side down is positive); and how fast it
    /// turns about the console's own axes in degrees per second, signed like the controls ask: pitch up, yaw right and
    /// roll right are positive. Ours.
    /// </summary>
    static (Vector3 Position, int Heading, float Pitch, float Roll, Vector3 Turn) FlightReadout(Seat seat)
    {
        var ship = seat.Ship;
        var basis = ship.GlobalTransform.Basis.Orthonormalized() * ConsoleFrame(seat);
        var forward = -basis.Z;
        // Rounded first, so just left of north reads 0, not 360.
        int heading = ((int)MathF.Round(Mathf.RadToDeg(Mathf.Atan2(forward.X, -forward.Z))) % 360 + 360) % 360;
        float pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(forward.Y, -1, 1)));
        float roll = Mathf.RadToDeg(Mathf.Atan2(-basis.X.Y, basis.Y.Y));
        var spin = basis.Inverse() * ship.AngularVelocity;
        var turn = new Vector3(spin.X, -spin.Y, -spin.Z) * (180 / Mathf.Pi);
        return (ship.ToGlobal(ship.Mass.CenterOfMass.ToGodot()), heading, pitch, roll, turn);
    }

    /// <summary>Whole degrees with a sign ("+5", "-12", "0"); never "-0".</summary>
    static string Signed(float degrees)
    {
        int whole = (int)MathF.Round(degrees);
        return whole > 0 ? FormattableString.Invariant($"+{whole}") : FormattableString.Invariant($"{whole}");
    }

    /// <summary>Windows of half a second or 120 frames, whichever ends first; the first frame (which may still carry
    /// the world's setup) is not counted.</summary>
    void CountFrame(double delta)
    {
        if (_fpsFrames++ > 0) _fpsWindow += delta;
        if (_fpsWindow >= 0.5 || _fpsFrames > 120)
        {
            _fps = $"{(_fpsFrames - 1) / _fpsWindow:0}";
            (_fpsFrames, _fpsWindow) = (1, 0);
        }
    }

    /// <summary>" (ship, 40 blocks, mass 96)" for a ship's block, empty for the world's.</summary>
    static string ShipNote(BlockGrid grid) =>
        grid is ShipGrid ship ? FormattableString.Invariant($" ({ship.Name}, {ship.BlockCount} blocks, mass {ship.Mass.Mass:0.#})") : "";

    /// <summary>A wire box around the aimed grid block, or around the whole model of an aimed object, in the aimed
    /// grid's frame (turned with a ship).</summary>
    void ShowHighlight()
    {
        _highlight.Visible = _hasTarget;
        if (!_hasTarget) return;
        Transform3D local;
        if (_target.Object is { } obj)
        {
            var box = obj.ModelBox;
            local = new Transform3D(Basis.Identity.Scaled(box.Size), box.GetCenter());
        }
        else
        {
            var cell = _target.Cell;
            local = new Transform3D(Basis.Identity, new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));
        }
        _highlight.GlobalTransform = _target.Grid.GlobalTransform * local;
    }

    void ShowGhost()
    {
        var plan = _hasTarget && !WindowOpen ? _mode.PlanObject(GlobalPosition, _aim) : null;
        _ghostFootprint.Visible = _ghostModel.Visible = plan is not null;
        if (plan is not { } p) return;
        // Built in the grid's local cells, then carried into the world by the grid's transform (a turned ship turns it).
        var frame = p.Grid.GlobalTransform;
        var bounds = p.Shape.RotatedSize(p.Orientation).ToVector3().ToGodot();
        _ghostFootprint.GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(bounds), p.Origin.ToGodot() + bounds * 0.5f);
        if (_ghostMeshFor != (p.Block, p.Orientation))
        {
            _ghostModel.Mesh = _grids.World.ModelMesh(p.Block, p.Orientation, _ghostModelMaterials[1, 0]);
            _ghostMeshFor = (p.Block, p.Orientation);
        }
        // The mesh is built at origin zero; it is moved to the plan's origin and scaled a hair about the model's centre
        // so the ghost does not z-fight with the faces it touches.
        var model = p.Shape.ModelBox(Simulation.Shapes.Int3.Zero, p.Orientation);
        var size = model.Size.ToGodot();
        var center = model.Center.ToGodot();
        var scale = new Vector3((size.X + 0.02f) / size.X, (size.Y + 0.02f) / size.Y, (size.Z + 0.02f) / size.Z);
        _ghostModel.GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(scale), p.Origin.ToGodot() + center - center * scale);
        _ghostFootprint.MaterialOverride = p.Placeable ? _ghostWireOk : _ghostWireBad;
        _ghostModel.MaterialOverride = _ghostModelMaterials[p.Placeable ? 1 : 0, _grids.Palette.IsCutout(p.Block) ? 1 : 0];
    }

    static StandardMaterial3D GhostMaterial(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = color,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    float Axis(string positive, string negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);

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
