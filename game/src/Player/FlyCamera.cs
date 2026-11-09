using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;
using Num = System.Numerics;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// The astronaut's eyes and hands: free mouse look, WASD, Space up, Ctrl down, Z/X roll, L level, K fixed camera when
/// aboard, Shift boost, N toggles noclip, left/right mouse buttons use/place/mine according to <see cref="PlayMode"/>,
/// R/middle turn the block to place, E uses the targeted station, door or console, F edits a console's colour-group
/// bindings or inspects a power network, C/I opens the inventory, H toggles the optional flight assistant, F5 saves.
/// Aiming, mining and placing work on every grid (the world and the ships) through <see cref="GridSet"/>, as well as in
/// empty space (attaching within 16 units with a red indicator line, or starting a new floating ship).
/// </summary>
public partial class FlyCamera : Camera3D, Projectiles.ITarget
{
    // ---- damage, death and respawn (as in the original; every number is a setting) ----

    bool _dead;
    Vector3 _deathAt;
    static Simulation.Settings.CvarStore Rules => Settings.GameSettings.Cvars;

    /// <summary>Health ran out and the death window is up.</summary>
    public bool IsDead => _dead;

    /// <summary>Creatures may go after the player: alive and in survival.</summary>
    public bool Targetable => !_dead && _mode is SurvivalMode;

    /// <summary>The body's centre, world space: the camera on foot, the console while seated.</summary>
    public Vector3 BodyCenter => ((Projectiles.ITarget)this).Center;

    /// <summary>Riding a ship or asteroid (the boot icon).</summary>
    public bool Aboard => _riding is { } r && IsInstanceValid(r);

    /// <summary>Seconds before a creature's touch can hurt again, as in the original (0.7 s after each).</summary>
    double _touchCooldown;
    const double TouchCooldown = 0.7;
    const float TouchKnockback = 5, TouchJolt = 0.2f;

    /// <summary>
    /// A creature at <paramref name="source"/> overlaps the body: the player moves <paramref name="push"/> along
    /// <paramref name="away"/>, and when it hurts (<paramref name="damage"/> &gt; 0) and the last touch was over 0.7 s ago,
    /// as in the original, takes the damage, flies off at 5 u/s and the view jolts by 0.2 rad.
    /// </summary>
    public void Touched(Vector3 away, float push, double damage, Vector3 source)
    {
        if (_dead) return;
        if (_seat is null) GlobalPosition += away * push;
        if (damage <= 0 || _touchCooldown > 0 || !_mode.Vulnerable) return;
        _touchCooldown = TouchCooldown;
        TakeDamage(damage, source);
        if (_dead || _seat is not null) return;
        Motor.Velocity += away * TouchKnockback;
        RotateObjectLocal(Vector3.Up, TouchJolt);
        RotateObjectLocal(Vector3.Right, TouchJolt);
        Orthonormalize();
    }

    /// <summary>The body: where the camera is on foot; at the console while seated, wherever the view is.</summary>
    Vector3 Projectiles.ITarget.Center => _seat is { } seat && IsInstanceValid(seat.Ship)
        ? seat.Ship.ToGlobal((Vector3)seat.Cell + new Vector3(0.5f, 0.5f, 0.5f))
        : GlobalPosition;

    bool Projectiles.ITarget.Hittable => !_dead && !Motor.Noclip && _mode.Vulnerable;

    /// <summary>A shot soaks up at most the health the player has left, as in the original; armor then cuts what is
    /// taken.</summary>
    double Projectiles.ITarget.Absorb(double damage, Vector3 direction)
    {
        double absorbed = Math.Min(Math.Max(0, damage), _mode.HealthLeft);
        if (absorbed > 0) TakeDamage(absorbed, GlobalPosition - direction);
        return absorbed;
    }

    /// <summary>
    /// Hurts the player by <paramref name="damage"/> (before armor) coming from <paramref name="source"/> (world space,
    /// null when it has no direction). As in the original any hit shows the red markers and throws the player out of a
    /// console seat (<c>sv_damage_seat_eject</c>).
    /// </summary>
    public void TakeDamage(double damage, Vector3? source)
    {
        if (_dead || !(damage > 0) || !_mode.Vulnerable) return;
        _mode.TakeDamage(damage);
        _hud.ShowDamage(ScreenAngle(source));
        if (_seat is not null && Rules.Bool(Simulation.Settings.Cvars.DamageSeatEject)) StandUp("Hit!", warning: true);
    }

    /// <summary>Which way <paramref name="source"/> lies on screen, clockwise from up; null (markers all round) when
    /// it has no direction or lies within about 20 degrees of straight ahead or behind.</summary>
    float? ScreenAngle(Vector3? source)
    {
        if (source is not { } s) return null;
        var local = GlobalBasis.Inverse() * (s - GlobalPosition);
        if (new Vector2(local.X, local.Y).LengthSquared() < 0.12f * local.LengthSquared()) return null;
        return MathF.Atan2(local.X, local.Y);
    }

    /// <summary>Hitting a surface hard hurts: <c>sv_impact_damage</c> per m/s straight into it above
    /// <c>sv_impact_speed</c> (7 per m/s above 8, as in the original).</summary>
    void HurtByImpact()
    {
        float speed = Motor.Impact.Length();
        if (speed <= 0) return;
        int damage = Simulation.Combat.Damage.Impact(speed, Rules.Float(Simulation.Settings.Cvars.ImpactSpeed),
            Rules.Float(Simulation.Settings.Cvars.ImpactDamage));
        if (damage > 0) TakeDamage(damage, GlobalPosition + Motor.Impact.Normalized());
    }

    /// <summary>
    /// Notices death (shot, crash, or the console's kill) and a revival from the console; true while dead, when the
    /// player neither moves nor acts.
    /// </summary>
    /// <summary>Runs the death at once if health already ran out, without waiting for the next frame.</summary>
    public void NoticeDeath() => UpdateDeath(0);

    bool UpdateDeath(double delta)
    {
        if (!_dead && _mode.IsDead) Die();
        else if (_dead && !_mode.IsDead)
        {
            // Brought back without the Respawn button (heal, god, a switch to creative): stay where the body is.
            _dead = false;
            _hud.HideDeath();
            if (!_inputLocked && !WindowOpen) Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        return _dead;
    }

    /// <summary>
    /// Death, as in the original: out of any seat, the hand emptied (the item stays in the inventory), consumable
    /// effects cleared, and the "You have died" window with its Respawn button.
    /// </summary>
    void Die()
    {
        _deathAt = GlobalPosition;
        if (_seat is not null) StandUp();
        StopActions();
        _bindTool = null;
        _windows?.Close();
        _mode.OnDeath(_deathAt);
        Motor.Velocity = Motor.Drift = Vector3.Zero;
        _dead = true;
        _hud.SetCenterPrompt("");
        _hud.ShowProgress(null);
        _hud.ShowDeath(Respawn);
        if (!_inputLocked) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>
    /// Respawn, as in the original: <c>sv_respawn_min</c>..<c>sv_respawn_max</c> (64 to 95) metres from the death
    /// point, in a direction up every axis, never inside a block, facing back toward where the player died; health
    /// to 100 and suit power full.
    /// </summary>
    public void Respawn()
    {
        if (!_dead) return;
        float min = Rules.Float(Simulation.Settings.Cvars.RespawnMin), max = Rules.Float(Simulation.Settings.Cvars.RespawnMax);
        var spot = _deathAt;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            var candidate = Simulation.Combat.Damage.RespawnPoint(_deathAt.ToNumerics(),
                Random.Shared.NextDouble(), Random.Shared.NextDouble(), Random.Shared.NextDouble(), Random.Shared.NextDouble(), min, max).ToGodot();
            spot = candidate;
            if (!PlayerMotor.Blocked(candidate, _grids)) break;
        }
        _dead = false;
        _hud.HideDeath();
        TeleportTo(spot);
        if ((_deathAt - spot).LengthSquared() > 1e-4f) LookAt(_deathAt, Vector3.Up);
        _mode.OnRespawn();
        if (!_inputLocked) Input.MouseMode = Input.MouseModeEnum.Captured;
    }
    /// <summary>Radians per pixel: the original's 0.01 times its default sensitivity 0.5.</summary>
    /// <summary>Radians per pixel at sensitivity 1, as in the original (its default 0.5 makes 0.005).</summary>
    const float BaseMouseSensitivity = 0.01f;
    const float Reach = 8f;

    /// <summary>Exposed so a save system can persist velocity and noclip.</summary>
    public PlayerMotor Motor { get; } = new();
    GridSet _grids = null!;
    Hud _hud = null!;
    PlayMode _mode = null!;
    GameWindows? _windows;
    DebugConsoleOverlay? _consoleOverlay;
    MeshInstance3D _highlight = null!;
    /// <summary>Ghost of the multi-block a placement would place: its footprint as a wire box, its model as the
    /// see-through textured model (so the player sees which way its faces point), tinted green when it fits and red
    /// when it doesn't.</summary>
    MeshInstance3D _ghostFootprint = null!, _ghostModel = null!;
    /// <summary>0.1 × 0.1 thick red attachment beam connecting a previewed block to the object it joins, as in the original.</summary>
    MeshInstance3D _attachLine = null!;
    /// <summary>Depth-tested 8-corner selector brackets drawn while holding a console's binding or camera tool.</summary>
    MeshInstance3D _bindOverlay = null!;
    ImmediateMesh _bindOverlayMesh = null!;
    StandardMaterial3D _bindOverlayMaterial = null!;
    ImageTexture _cornerTexture = null!;
    Material _ghostWireOk = null!, _ghostWireBad = null!;
    const float SelectorScale = 1.03f;
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

    /// <summary>Pilot's top speed request in flight-assistant mode, m/s; Shift multiplies it by <see cref="PilotBoost"/>. Ours, tunable.</summary>
    public const float PilotSpeed = 30f;
    public const float PilotBoost = 2f;
    /// <summary>Turn rate a held key asks for, and the most the mouse may ask for in flight-assistant mode, rad/s. Ours, tunable.</summary>
    public const float PilotTurnRate = 0.9f, MaxTurnRate = 1.8f;

    /// <summary>How fast Z/X roll the astronaut, rad/s.</summary>
    public const float WalkRollRate = 2f;

    /// <summary>The player boards a ship when its blocks come within this of the eye, and leaves it beyond
    /// <see cref="LeaveReach"/>: 2.5 and 5 body radii.</summary>
    public const float BoardReach = 2.5f * PlayerMotor.BodyRadius, LeaveReach = 5f * PlayerMotor.BodyRadius;

    float _aimDistance = float.PositiveInfinity;
    ShipGrid? _riding;
    Transform3D _ridePose;      // the ridden ship's pose at the end of the last frame
    Vector3 _rideSpeed;         // its motion where the player was then, given back if the ship vanishes
    bool _secondaryHeld;
    SpotLight3D? _flashlight;

    enum BindToolKind { Triggers, Camera }
    readonly record struct ActiveBindTool(BlockGrid Grid, Vector3I ConsoleCell, ConsoleComponent Component, BindToolKind Kind);
    ActiveBindTool? _bindTool;

    /// <summary>The ship the player is aboard (moving with it), or null.</summary>
    public ShipGrid? Riding => _riding;

    /// <summary>The player's velocity in the world: their own plus, aboard a ship, the ship's motion where they are.</summary>
    public Vector3 WorldVelocity => _riding is { } ship && IsInstanceValid(ship) ? Motor.Velocity + ship.PointVelocity(GlobalPosition) : Motor.Velocity;
    /// <summary>Time constant smoothing the mouse's turn request, seconds, so a mouse that reports only some frames
    /// still asks for a steady turn.</summary>
    const float MouseSmoothing = 0.08f;
    /// <summary>The pilot's eye sits this far above the console's centre, along the console's up (see <see cref="SeatEye"/>).</summary>
    const float SeatHeight = 0.9f;
    /// <summary>How far behind the console's centre the eye moves when there is no room above it.</summary>
    const float SeatBack = 1f;

    /// <summary>Where the pilot sits: a console block (a grid cell, or an object) of a ship.</summary>
    sealed record Seat(ShipGrid Ship, Vector3I Cell, WorldObject? Object, ConsoleComponent Component);

    public enum PilotView { Seat, Chase, Fixed }

    Seat? _seat;
    Vector2 _mouseDelta;
    Vector3 _mouseRate;
    float _seatYaw, _seatPitch;
    static readonly HashSet<Int3> EmptyTargets = [];
    /// <summary><c>--hold</c>: controls held one after another as if pressed, with how long the first has run.</summary>
    readonly List<(HashSet<string> Actions, float Seconds)> _holds = [];
    float _holdTime;

    /// <summary>The ship being piloted, or null when standing.</summary>
    public ShipGrid? PilotShip => _seat?.Ship;

    /// <summary>How the camera follows the ship while piloting. <see cref="PilotView.Fixed"/> (a screenshot hook) leaves
    /// it where it was when the pilot sat down.</summary>
    public PilotView View { get; set; } = PilotView.Seat;

    /// <summary>Our optional automatic flight assistant (default off: seated pilots use the original's console
    /// colour-group key bindings). H toggles; the setting is <c>cl_flight_assist</c>.</summary>
    public bool FlightAssist
    {
        get => Settings.GameSettings.Cvars.Bool(Simulation.Settings.Cvars.FlightAssist);
        set => Settings.GameSettings.Cvars.Set(Simulation.Settings.Cvars.FlightAssist, value);
    }

    /// <summary>Locks the camera's up vector to the ship's deck while aboard (default off). K toggles; the setting is
    /// <c>cl_fixed_camera</c>.</summary>
    public bool FixedCamera
    {
        get => Settings.GameSettings.Cvars.Bool(Simulation.Settings.Cvars.FixedCamera);
        set => Settings.GameSettings.Cvars.Set(Simulation.Settings.Cvars.FixedCamera, value);
    }

    /// <summary>Radians of turn per pixel of mouse movement: the base rate times <c>cl_sensitivity</c>.</summary>
    static float MouseSensitivity => BaseMouseSensitivity * Settings.GameSettings.Cvars.Float(Simulation.Settings.Cvars.Sensitivity);

    /// <summary>Reach of the console binding tool, <c>sv_bind_distance</c>.</summary>
    static float BindReach => Settings.GameSettings.Cvars.Float(Simulation.Settings.Cvars.BindDistance);

    /// <summary>Inertial dampeners: in flight-assistant mode, also brings the ship to rest when nothing is asked. G toggles.</summary>
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
    /// footprint. The highlight, the ghost, a placement and E/F all use it. The first mouse movement or key press lets
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

    /// <summary>F5. Raised even while a window is open: saving changes nothing the window shows.</summary>
    public event Action? SaveRequested;

    /// <param name="windows">The modal in-world windows (inventory, crafting, console binding, power network).</param>
    /// <param name="consoleOverlay">The drop-down developer/debug console toggled with <c>`</c>.</param>
    public void Setup(GridSet grids, Hud hud, PlayMode mode, GameWindows? windows, DebugConsoleOverlay? consoleOverlay = null)
    {
        _grids = grids;
        var world = grids.World;
        _hud = hud;
        _mode = mode;
        _windows = windows;
        _consoleOverlay = consoleOverlay;
        _mode.Push = kick => Motor.Velocity += kick;
        _cornerTexture = CornerBracketTexture();
        _highlight = new MeshInstance3D
        {
            Mesh = SelectorBoxMesh(SelectorMaterial(_cornerTexture, new Color(1f, 1f, 1f, 0.9f))),
            Visible = false,
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_highlight);
        _ghostWireOk = SelectorMaterial(_cornerTexture, Colors.White);
        _ghostWireBad = SelectorMaterial(_cornerTexture, new Color(1f, 0.25f, 0.2f));
        var ghostShader = GD.Load<Shader>("res://shaders/ghost.gdshader");
        for (int placeable = 0; placeable < 2; placeable++)
        for (int cutout = 0; cutout < 2; cutout++)
        {
            var material = new ShaderMaterial { Shader = ghostShader };
            material.SetShaderParameter("albedo_atlas", world.Textures.Albedo);
            material.SetShaderParameter("cutout", cutout == 1);
            // Valid placements show natural block colours inside white corner brackets, as in the original; blocked ones tint red.
            material.SetShaderParameter("tint", placeable == 1 ? Colors.White : new Color(1f, 0.22f, 0.18f));
            material.SetShaderParameter("tint_amount", placeable == 1 ? 0f : 0.55f);
            material.SetShaderParameter("opacity", placeable == 1 ? 0.88f : 0.72f);
            _ghostModelMaterials[placeable, cutout] = material;
        }
        _ghostFootprint = new MeshInstance3D
        {
            Mesh = SelectorBoxMesh(_ghostWireOk), Visible = false, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _ghostModel = new MeshInstance3D
        {
            Visible = false, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _attachLine = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.12f, 0.1f),
            },
            Visible = false,
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _bindOverlayMesh = new ImmediateMesh();
        _bindOverlayMaterial = SelectorMaterial(_cornerTexture, Colors.White, vertexColors: true);
        _bindOverlay = new MeshInstance3D
        {
            Mesh = _bindOverlayMesh,
            Visible = false,
            TopLevel = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ghostFootprint);
        AddChild(_ghostModel);
        AddChild(_attachLine);
        AddChild(_bindOverlay);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    bool WindowOpen => _windows?.IsOpen == true || _consoleOverlay?.IsOpen == true || MenusOpen?.Invoke() == true;

    /// <summary>Whether a menu (the Game menu, Options, the welcome window) is up; set by the world.</summary>
    public Func<bool>? MenusOpen { get; set; }

    /// <summary>Esc: the "Game menu", as in the original.</summary>
    public event Action? MenuRequested;

    /// <summary>F1: the welcome window, as in the original.</summary>
    public event Action? HelpRequested;

    /// <summary>Switches the active <see cref="PlayMode"/> (e.g. between creative and survival via the debug console).</summary>
    public void SetMode(PlayMode mode)
    {
        StopActions();
        _bindTool = null;
        _mode?.Detach();
        _mode = mode;
        _mode.Push = kick => Motor.Velocity += kick;
    }

    /// <summary>Teleports the player to <paramref name="worldPos"/>, unmounting from any ship seat and zeroing velocity.</summary>
    public void TeleportTo(Vector3 worldPos)
    {
        if (_seat is not null) StandUp();
        _riding = null;
        PinnedAim = null;
        GlobalPosition = worldPos;
        Motor.Velocity = Vector3.Zero;
        Motor.Drift = Vector3.Zero;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_inputLocked)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_consoleOverlay?.IsOpen == true) return;
        if (@event is InputEventMouseMotion or InputEventKey { Pressed: true }) PinnedAim = null;
        if (@event is InputEventKey { Pressed: true, Echo: false } fnKey)
        {
            if (fnKey.IsAction(GameInput.DebugConsole) ||
                fnKey.PhysicalKeycode is Key.Quoteleft or Key.Section ||
                fnKey.Keycode is Key.Quoteleft or Key.Asciitilde)
            {
                StopActions();
                _consoleOverlay?.Open();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (fnKey.IsAction(GameInput.Save))
            {
                SaveRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (fnKey.IsAction(GameInput.DebugHud))
            {
                _hud.DebugVisible = !_hud.DebugVisible;
                GetViewport().SetInputAsHandled();
                return;
            }
            if (fnKey.IsAction(GameInput.ToggleHud))
            {
                _hud.Visible = !_hud.Visible;
                GetViewport().SetInputAsHandled();
                return;
            }
            if (fnKey.IsAction(GameInput.Help) && !WindowOpen)
            {
                StopActions();
                HelpRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        if (_dead)
        {
            // Dead: only the Respawn button (or Esc for the menu) does anything.
            if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape }) MenuRequested?.Invoke();
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
                RotateFootView(motion.Relative);
                break;
            case InputEventMouseButton { Pressed: true } when !captured:
                // The click that takes the mouse back is not also a mining click.
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            // A release counts only after a press seen in play: the click that took the mouse back, or a press made in
            // a window or the pilot's seat, does nothing when it comes up.
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (_primaryHeld)
                {
                    if (_bindTool is not null) ApplyBindToolClick();
                    else _mode.PrimaryReleased(GlobalPosition, _hasTarget ? _aim : null);
                }
                _primaryHeld = false;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
                if (_secondaryHeld)
                {
                    if (_bindTool is not null) _bindTool = null;
                    else _mode.SecondaryReleased();
                }
                _secondaryHeld = false;
                break;
            case InputEventMouseButton { Pressed: true } button:
                switch (button.ButtonIndex)
                {
                    case MouseButton.Left:
                        _primaryHeld = true;
                        if (_bindTool is null) _mode.PrimaryPressed(_hasTarget ? _target : null);
                        break;
                    case MouseButton.Right:
                        _secondaryHeld = true;
                        if (_bindTool is null) _mode.SecondaryPressed(GlobalPosition, _hasTarget ? _aim : null);
                        break;
                    case MouseButton.Middle:
                        if (_bindTool is null) Turn(NextOrientation);
                        break;
                    case MouseButton.WheelUp:
                        _bindTool = null;
                        _mode.Cycle(-1);
                        break;
                    case MouseButton.WheelDown:
                        _bindTool = null;
                        _mode.Cycle(1);
                        break;
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.PhysicalKeycode == Key.Escape)
                {
                    StopActions();
                    MenuRequested?.Invoke();
                }
                else if (key.IsAction(GameInput.Noclip))
                    ToggleNoclip();
                else if (key.IsAction(GameInput.Level))
                {
                    LevelAstronautView();
                    _hud.Flash("View levelled");
                }
                else if (key.IsAction(GameInput.FixedCamera))
                    ToggleFixedCamera();
                else if (key.IsAction(GameInput.FlightAssist))
                    ToggleFlightAssist();
                else if (key.IsAction(GameInput.Inventory))
                    OpenInventory();
                else if (key.IsAction(GameInput.Interact))
                    Interact();
                else if (key.IsAction(GameInput.Edit))
                    EditBlock();
                else if (key.IsAction(GameInput.Flashlight))
                    ToggleFlashlight();
                else if (key.IsAction(GameInput.Spin))
                    Turn(NextOrientation);
                else if (key.PhysicalKeycode is >= Key.Key1 and <= Key.Key8)
                {
                    _bindTool = null;
                    _mode.Select((int)(key.PhysicalKeycode - Key.Key1));
                }
                break;
        }
    }

    void RotateFootView(Vector2 relative)
    {
        if (_riding is { } aboard && IsInstanceValid(aboard) && FixedCamera)
        {
            var shipBasis = aboard.GlobalTransform.Basis.Orthonormalized();
            var localForward = -(shipBasis.Inverse() * GlobalTransform.Basis.Z);
            var euler = LevelRotation(localForward);
            float pitch = Mathf.Clamp(euler.X - relative.Y * MouseSensitivity, -1.55f, 1.55f);
            float yaw = euler.Y - relative.X * MouseSensitivity;
            GlobalTransform = new Transform3D(shipBasis * Basis.FromEuler(new Vector3(pitch, yaw, 0)), GlobalPosition);
            return;
        }
        // Free turning, as in the original: yaw and pitch about the astronaut's own axes, with no fixed up and no stop
        // at straight up or down. Z/X roll (see _Process); L levels again.
        RotateObjectLocal(Vector3.Up, -relative.X * MouseSensitivity);
        RotateObjectLocal(Vector3.Right, -relative.Y * MouseSensitivity);
        Orthonormalize();
    }

    void LevelAstronautView()
    {
        if (_riding is { } aboard && IsInstanceValid(aboard))
        {
            var shipBasis = aboard.GlobalTransform.Basis.Orthonormalized();
            var localForward = -(shipBasis.Inverse() * GlobalTransform.Basis.Z);
            GlobalTransform = new Transform3D(shipBasis * Basis.FromEuler(LevelRotation(localForward)), GlobalPosition);
            return;
        }
        Rotation = LevelRotation(-GlobalTransform.Basis.Z);
    }

    /// <summary>N: noclip, a cheat like the console's <c>noclip</c>, so it needs <c>sv_cheats 1</c>; turning noclip off
    /// always works.</summary>
    void ToggleNoclip()
    {
        if (!Motor.Noclip && !Settings.GameSettings.Cvars.CheatsEnabled)
        {
            _hud.Flash("Noclip is a cheat: type sv_cheats 1 in the console (`) first", warning: true);
            return;
        }
        Motor.Noclip = !Motor.Noclip;
        _hud.Flash(Motor.Noclip ? "Noclip on" : "Noclip off");
    }

    void ToggleFixedCamera()
    {
        FixedCamera = !FixedCamera;
        if (FixedCamera && _riding is { } aboard && IsInstanceValid(aboard))
            LevelAstronautView();
        _hud.Flash(FixedCamera ? "Fixed camera when aboard on" : "Fixed camera when aboard off");
    }

    void ToggleFlightAssist()
    {
        FlightAssist = !FlightAssist;
        (_mouseDelta, _mouseRate) = (Vector2.Zero, Vector3.Zero);
        if (_seat is not null)
        {
            (_seatYaw, _seatPitch) = (0, 0);
            UpdateSeatedControlsHint();
        }
        _hud.Flash(FlightAssist ? "Flight assistant on" : "Flight assistant off (console bindings)");
    }

    /// <summary>Seated: in console mode the mouse freelooks (and aims gun-console cannons) while bound colour-group keys
    /// fire thrusters and guns; in flight-assistant mode (H) the mouse steers and LMB fires all guns. E or Esc stands up.</summary>
    void PilotInput(InputEvent @event)
    {
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (@event)
        {
            case InputEventMouseMotion motion when captured:
                if (FlightAssist && _seat is { Component.GunsOnly: false })
                {
                    _mouseDelta += motion.Relative;
                }
                else
                {
                    _seatYaw -= motion.Relative.X * MouseSensitivity;
                    _seatPitch = Mathf.Clamp(_seatPitch - motion.Relative.Y * MouseSensitivity, -1.48f, 1.48f);
                }
                break;
            case InputEventMouseButton { Pressed: true } when !captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
            // Held to fire the ship's guns in flight-assistant mode (see PilotProcess).
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } fire:
                _primaryHeld = fire.Pressed;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.PhysicalKeycode == Key.Escape || key.IsAction(GameInput.Interact))
                    StandUp();
                else if (key.IsAction(GameInput.FlightAssist))
                    ToggleFlightAssist();
                else if (key.IsAction(GameInput.FixedCamera))
                    ToggleFixedCamera();
                else if (FlightAssist && key.IsAction(GameInput.Dampeners))
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

    /// <summary>Whether the F3 technical readout at the top-left of the HUD is visible.</summary>
    public bool DebugHud
    {
        get => _hud.DebugVisible;
        set => _hud.DebugVisible = value;
    }

    /// <summary>While a window is open only the keys that close it count; the windows take their own clicks.</summary>
    void WindowInput(InputEvent @event)
    {
        if (_windows?.IsListeningForKey == true) return;
        if (@event is InputEventKey { Pressed: true, Echo: false } key &&
            (key.PhysicalKeycode == Key.Escape || key.IsAction(GameInput.Inventory) || key.IsAction(GameInput.Edit) || key.IsAction(GameInput.Help)))
        {
            _windows!.Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Places at the current aim (the pinned one, if any); <c>--place</c> uses it.</summary>
    public void PlaceAtAim()
    {
        if (PinnedAim is { } pin) _mode.Place(GlobalPosition, pin);
        else if (_grids.Raycast(GlobalPosition, -GlobalTransform.Basis.Z, Reach, out var aim)) _mode.Place(GlobalPosition, aim);
        else _mode.PlaceInSpace(GlobalPosition);
    }

    /// <summary>E at the current aim (the pinned one, if any); <c>--launch</c> uses it on a console.</summary>
    public void InteractAtAim()
    {
        if (PinnedAim is { } pin && _grids.Contains(pin.Grid) && pin.Grid.IsSolid(pin.Hit))
            (_hasTarget, _aim, _target) = (true, pin, pin.Grid.TargetAt(pin.Hit));
        Interact();
    }

    public void OpenInventory()
    {
        if (_windows is null || !_windows.HasSurvivalInventory)
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
        if (_windows is null || !_windows.HasSurvivalInventory)
        {
            _hud.Flash("Crafting is off in creative mode", warning: true);
            return;
        }
        StopActions();
        _windows.OpenCrafting(station, title);
    }

    /// <summary>Equips <paramref name="consoleCell"/>'s trigger binding tool (deselecting the hotbar), as pressing F on it does.</summary>
    public void EquipConsoleBindTool(BlockGrid grid, Vector3I consoleCell, ConsoleComponent component)
    {
        grid.Consoles.GetOrCreate(consoleCell.ToInt3(), component.Buttons, component.GunsOnly);
        _mode.Deselect();
        _bindTool = new ActiveBindTool(grid, consoleCell, component, BindToolKind.Triggers);
    }

    /// <summary>Opens the "Control block" window for a console. Also used by <c>--open=console</c>.</summary>
    public void OpenConsoleWindow(BlockGrid grid, Vector3I consoleCell, ConsoleComponent component)
    {
        if (_windows is null) return;
        StopActions();
        grid.Consoles.GetOrCreate(consoleCell.ToInt3(), component.Buttons, component.GunsOnly);
        if (_bindTool is null)
        {
            _mode.Deselect();
            _bindTool = new ActiveBindTool(grid, consoleCell, component, BindToolKind.Triggers);
        }
        _windows.OpenConsoleWindow(grid, consoleCell, component, onSetCamera: () =>
        {
            _mode.Deselect();
            _bindTool = new ActiveBindTool(grid, consoleCell, component, BindToolKind.Camera);
        });
    }

    /// <summary>Opens the "Power network" popup. Also used by <c>--open=power</c>.</summary>
    public void OpenPowerWindow(PowerNetworkInfo network)
    {
        if (_windows is null) return;
        StopActions();
        _windows.OpenPowerWindow(network);
    }

    /// <summary>Opens the F1 Help &amp; Controls window. Also used by <c>--open=help</c>.</summary>
    public void OpenHelp()
    {
        if (_windows is null) return;
        StopActions();
        _windows.OpenHelpWindow();
    }

    /// <summary>R/middle mouse. Multi-blocks and turnable single blocks (<see cref="BlockPalette.IsTurnable"/>) turn; with a
    /// plain cube or nothing selected the key says so instead of claiming a turn nobody can see.</summary>
    void Turn(Func<Orientation, Orientation> turn)
    {
        var selected = _mode.SelectedBlock;
        if (!_grids.Palette.IsObject(selected) && !_grids.Palette.IsTurnable(selected))
        {
            _hud.Flash(selected == BlockPalette.Air ? "Nothing selected to turn" : $"{_grids.Palette.Item(selected).Name} looks the same any way round");
            return;
        }
        var orientation = turn(_mode.Orientation);
        _mode.Orientation = orientation;
        _hud.Flash($"Orientation {orientation.Describe()}");
    }

    /// <summary>
    /// E: a door opens or closes (in either mode); a console of the static world launches its structure as a ship, a
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
        OpenCrafting(station.Station, item.Name);
    }

    /// <summary>E on a console: launch from the world, pilot on a ship.</summary>
    void UseConsole(BlockTarget console)
    {
        if (console.Grid is ShipGrid ship)
        {
            Pilot(ship, console);
            return;
        }
        _bindTool = null;
        var (launched, message) = _grids.LaunchShip(console);
        _hud.Flash(message, warning: launched is null, seconds: 4);
        // The structure left the grid it was aimed in; aim again next frame rather than at a stale target.
        PinnedAim = null;
        _hasTarget = false;
    }

    /// <summary>E on a ship's console: sit down and fly it.</summary>
    void Pilot(ShipGrid ship, BlockTarget console)
    {
        var cell = console.Object is { } obj ? obj.Origin.ToGodot() : console.Cell;
        Sit(ship, cell);
    }

    /// <summary>
    /// Sits at the console at <paramref name="cell"/> of <paramref name="ship"/> (a console grid block there, or a
    /// console object whose origin it is) and takes the controls. Refused when the console has no power, as in the
    /// original. Returns false when there is no powered console there.
    /// </summary>
    public bool Sit(ShipGrid ship, Vector3I cell)
    {
        if (ConsoleAt(ship, cell) is not { } seat) return false;
        if (!ship.Systems.IsBlockActive(new BlockTarget(ship, seat.Cell, seat.Object)))
        {
            _hud.Flash("Console has no power", warning: true);
            return false;
        }
        StopActions();
        _bindTool = null;
        PinnedAim = null;
        _hasTarget = false;
        _highlight.Visible = _ghostFootprint.Visible = _ghostModel.Visible = _attachLine.Visible = _bindOverlay.Visible = false;
        if (_facing is not null) _facing.Visible = false;
        _seat = seat;
        _hud.SelectionVisible = false;
        _hud.BoardedVisible = false;
        _hud.SetCrosshair(Hud.CrosshairKind.Default);
        _hud.SetCenterPrompt("");
        (_mouseDelta, _mouseRate) = (Vector2.Zero, Vector3.Zero);
        (_seatYaw, _seatPitch) = (0, 0);
        Motor.Velocity = Motor.Drift = Vector3.Zero;
        _riding = null;
        UpdateSeatedControlsHint();
        FollowShip();
        _hud.SetPiloting(true, ship.LinearVelocity, GlobalTransform.Basis, FlightAssist && !seat.Component.GunsOnly, Dampeners);
        return true;
    }

    void UpdateSeatedControlsHint()
    {
        if (_seat is null) return;
        if (_seat.Component.GunsOnly)
        {
            _hud.SetControls("Gun console: bound keys fire cannons · mouse aims · V view · E or Esc stand up   F5 save");
        }
        else if (FlightAssist)
        {
            _hud.SetControls("Piloting (Flight assistant, H for console keys): W/S/A/D · Space/Ctrl · mouse/arrows pitch/yaw · Z/X roll · " +
                             "Shift boost · G dampeners · LMB fire guns · V view · E/Esc stand up   F5 save");
        }
        else
        {
            _hud.SetControls("Piloting (Console bindings, H for flight assistant): bound group keys fire thrusters/guns · " +
                             "mouse look · V view · E or Esc stand up   F5 save");
        }
    }

    /// <summary>The seat at a console of <paramref name="ship"/> at <paramref name="cell"/> (a console grid block, or the
    /// origin of a console object), or null when no console is there (any more).</summary>
    Seat? ConsoleAt(ShipGrid ship, Vector3I cell)
    {
        var obj = ship.ObjectAtOrigin(cell) ?? ship.ObjectAt(cell);
        var block = obj?.Block ?? ship.Get(cell);
        return block != BlockPalette.Air && _grids.Palette.Item(block).Get<ConsoleComponent>() is { } comp
            ? new Seat(ship, cell, obj, comp)
            : null;
    }

    /// <summary>
    /// Leaves the seat: the player stands at the nearest free spot beside the console (<see cref="StandingSpot"/>),
    /// aboard the ship and at rest on it, so they move with it, looking the way the camera did.
    /// </summary>
    public void StandUp(string message = "Standing", bool warning = false)
    {
        if (_seat is not { } seat) return;
        var (eye, _) = StandingSpot();
        _seat = null;
        _hud.SetPiloting(false);
        _hud.SelectionVisible = true;
        GlobalPosition = eye;
        // Facing the way the seat did, roll and all (unless FixedCamera levels to the ship's deck).
        GlobalBasis = new Basis(GlobalBasis.GetRotationQuaternion());
        Motor.Velocity = Motor.Drift = Vector3.Zero;
        // A button held to fire the guns must not place or use anything when it comes up on foot.
        _primaryHeld = _secondaryHeld = false;
        _riding = seat.Ship;
        _hud.BoardedVisible = true;
        if (FixedCamera) LevelAstronautView();
        RememberRide();
        _hud.SetControls(_mode.Controls + "   F5 save");
        if (warning) _hud.Flash(message, warning: true);
    }

    /// <summary>Moves the player with the ship they are aboard: where it went since last frame, turned with it, their own
    /// velocity turned with it too.</summary>
    void CarryWithShip()
    {
        if (_riding is not { } ship) return;
        if (!IsInstanceValid(ship) || !_grids.Contains(ship))
        {
            Motor.Velocity += _rideSpeed;
            _riding = null;
            return;
        }
        // The ship's own move since last frame, applied to the player as they are now, so mouse look, levelling and
        // recoil from between frames are kept.
        var moved = ship.GlobalTransform * _ridePose.AffineInverse();
        var carried = moved * GlobalTransform;
        GlobalTransform = new Transform3D(carried.Basis.Orthonormalized(), carried.Origin);
        Motor.Velocity = moved.Basis * Motor.Velocity;
    }

    /// <summary>
    /// Boards a ship whose blocks come within <see cref="BoardReach"/>, leaves the one aboard once its blocks are further
    /// than <see cref="LeaveReach"/>. Boarding takes the ship's motion out of the player's velocity,
    /// leaving gives it back, so nothing jumps.
    /// </summary>
    void UpdateBoarding()
    {
        if (_riding is { } ship && !PlayerMotor.Touches(ship, GlobalPosition, LeaveReach))
        {
            Motor.Velocity += ship.PointVelocity(GlobalPosition);
            _riding = null;
        }
        if (_riding is null)
        {
            var ships = _grids.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                var (center, radius) = ships[i].WorldExtent;
                if (GlobalPosition.DistanceSquaredTo(center) > (radius + BoardReach + 1) * (radius + BoardReach + 1)) continue;
                if (!PlayerMotor.Touches(ships[i], GlobalPosition, BoardReach)) continue;
                Motor.Velocity -= ships[i].PointVelocity(GlobalPosition);
                _riding = ships[i];
                if (FixedCamera) LevelAstronautView();
                break;
            }
        }
        RememberRide();
    }

    void RememberRide()
    {
        if (_riding is not { } ship) return;
        _ridePose = ship.GlobalTransform;
        _rideSpeed = ship.PointVelocity(GlobalPosition);
    }

    /// <summary>R and the middle button: the block's top turns to the next of the six directions, and after all six the
    /// spin moves on, so every press changes the facing and all 24 orientations come round.</summary>
    static Orientation NextOrientation(Orientation o) =>
        o.NextUp().Up == Orientation.Default.Up ? o.NextUp().NextSpin() : o.NextUp();

    /// <summary>
    /// F: edit the aimed block, as in the original. On a console, equips its colour-group binding tool (deselecting the
    /// hotbar), or if already holding that console's binding tool, opens the "Control block" key-binding window. On any
    /// other powered block, opens the "Power network" status popup.
    /// </summary>
    void EditBlock()
    {
        if (_hasTarget)
        {
            var block = _target.Grid.BlockOf(_target);
            if (block != BlockPalette.Air && _grids.Palette.Item(block).Get<ConsoleComponent>() is { } consoleComp)
            {
                var consoleCell = _target.Object is { } o ? o.Origin.ToGodot() : _target.Cell;
                _target.Grid.Consoles.GetOrCreate(consoleCell.ToInt3(), consoleComp.Buttons, consoleComp.GunsOnly);
                if (_bindTool is { Kind: BindToolKind.Triggers } bt && bt.Grid == _target.Grid && bt.ConsoleCell == consoleCell)
                {
                    OpenConsoleWindow(_target.Grid, consoleCell, consoleComp);
                    return;
                }
                EquipConsoleBindTool(_target.Grid, consoleCell, consoleComp);
                return;
            }
        }

        if (_bindTool is { Kind: BindToolKind.Triggers } activeBt)
        {
            OpenConsoleWindow(activeBt.Grid, activeBt.ConsoleCell, activeBt.Component);
            return;
        }

        if (!_hasTarget) return;
        var aimedBlock = _target.Grid.BlockOf(_target);
        if (aimedBlock == BlockPalette.Air) return;
        var item = _grids.Palette.Item(aimedBlock);
        if (_grids.Palette.IsPowered(aimedBlock) && _target.Grid.Systems.NetworkOf(_target) is { } network)
        {
            OpenPowerWindow(network);
            return;
        }

        _hud.Flash($"{item.Name} has nothing to edit");
    }

    void ApplyBindToolClick()
    {
        if (_bindTool is not { } bt) return;
        if (!_hasTarget || _target.Grid != bt.Grid || _aimDistance > BindReach)
        {
            if (_hasTarget && _target.Grid == bt.Grid && _aimDistance > BindReach)
                _hud.Flash("Move closer (within 3 m) to bind blocks", warning: true);
            else if (_hasTarget && _target.Grid != bt.Grid)
                _hud.Flash("Blocks can only be bound on the same ship or structure", warning: true);
            return;
        }

        var targetCell = _target.Object is { } obj ? obj.Origin.ToGodot() : _target.Cell;
        if (targetCell == bt.ConsoleCell) return;
        var targetBlock = _target.Grid.BlockOf(_target);
        if (targetBlock == BlockPalette.Air) return;
        var targetItem = _grids.Palette.Item(targetBlock);

        if (bt.Kind == BindToolKind.Camera)
        {
            if (targetItem.Kind != "camera")
            {
                _hud.Flash($"{targetItem.Name} is not a camera block", warning: true);
                return;
            }
            bool bound = bt.Grid.Consoles.ToggleCamera(bt.ConsoleCell.ToInt3(), bt.Component.Buttons, bt.Component.GunsOnly, targetCell.ToInt3());
            _hud.Flash(bound ? $"Bound {targetItem.Name} to console" : $"Unbound {targetItem.Name} from console");
            return;
        }

        bool isThruster = targetItem.Get<ThrusterComponent>() is not null;
        bool isGun = targetItem.Get<GunComponent>() is not null;
        if (bt.Component.GunsOnly ? !isGun : (!isThruster && !isGun))
        {
            _hud.Flash(bt.Component.GunsOnly ? "Gun consoles can only bind cannons" : $"{targetItem.Name} cannot be bound to a console", warning: true);
            return;
        }

        int group = bt.Grid.Consoles.CycleTarget(bt.ConsoleCell.ToInt3(), bt.Component.Buttons, bt.Component.GunsOnly,
            targetCell.ToInt3(), isThruster, isGun);
        if (group < 0)
        {
            _hud.Flash($"Unbound {targetItem.Name}");
        }
        else
        {
            var state = bt.Grid.Consoles.GetOrCreate(bt.ConsoleCell.ToInt3(), bt.Component.Buttons, bt.Component.GunsOnly);
            _hud.Flash($"Bound {targetItem.Name} to {ConsoleBinding.ColorGroups[group].Name} (key: {state.Groups[group].Key})");
        }
    }

    /// <summary>T: the helmet light, a spot along the view.</summary>
    void ToggleFlashlight()
    {
        if (_flashlight is null)
        {
            _flashlight = new SpotLight3D { SpotRange = 24, SpotAngle = 28, LightEnergy = 2.2f, ShadowEnabled = false, Visible = false };
            AddChild(_flashlight);
        }
        _flashlightOn = !_flashlightOn;
        ShowFlashlight();
        _hud.Flash(_flashlightOn ? (Settings.Visuals.LightingOn ? "Flashlight on" : "Flashlight on (Lighting is off in Graphics options)") : "Flashlight off");
    }

    bool _flashlightOn;

    /// <summary>The flashlight shines while switched on and dynamic lighting is on (<c>cl_lighting</c>).</summary>
    void ShowFlashlight()
    {
        if (_flashlight is not null) _flashlight.Visible = _flashlightOn && Settings.Visuals.LightingOn;
    }

    // ---- view bobbing (as in the original: the view shakes above 4 m/s, up to 0.02 rad at 8 m/s) ----

    Vector2 _bob;

    /// <summary>Takes last frame's shake out of the view, so look and aim work on the steady view.</summary>
    void UndoBob()
    {
        if (_bob == Vector2.Zero) return;
        RotateObjectLocal(Vector3.Right, -_bob.Y);
        RotateObjectLocal(Vector3.Up, -_bob.X);
        _bob = Vector2.Zero;
    }

    void ApplyBob(float speed)
    {
        if (!Rules.Bool(Simulation.Settings.Cvars.ViewBobbing) || _inputLocked) return;
        float amount = 0.02f * Math.Clamp((speed - 4) / 4, 0, 1);
        if (amount <= 0) return;
        _bob = new Vector2((float)(Random.Shared.NextDouble() * 2 - 1), (float)(Random.Shared.NextDouble() * 2 - 1)) * amount;
        RotateObjectLocal(Vector3.Up, _bob.X);
        RotateObjectLocal(Vector3.Right, _bob.Y);
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
        if (_seat is not { } seat) return (GlobalPosition, WorldVelocity);
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

    /// <summary>The console's axes in the ship's local frame: right, up, back (the pilot looks along −Z).</summary>
    static Basis ConsoleFrame(Seat seat) => seat.Object is { } obj
        ? TurnBasis(obj.Orientation)
        : TurnBasis(seat.Ship.TurnOf(seat.Cell));

    /// <summary>
    /// The pilot's eye, ship-local: <see cref="SeatHeight"/> above the console's centre, or, when that point is inside a
    /// solid cell of the ship (a roof or a dashboard right over the console), the first free one of: behind the console
    /// at that height, straight behind it, a little above its centre.
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

    HashSet<Int3> HeldConsoleTargets(Seat seat)
    {
        var consoleState = seat.Ship.Consoles.TryGet(seat.Cell.ToInt3());
        if (consoleState is null) return EmptyTargets;
        bool realInput = !_inputLocked && !WindowOpen;
        HashSet<string>? heldKeys = null;
        for (int i = 0; i < consoleState.GroupCount; i++)
        {
            var key = consoleState.Groups[i].Key;
            if (!GameInput.IsConsoleKeyHeld(key, Held, realInput)) continue;
            heldKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            heldKeys.Add(key);
        }
        return heldKeys is null ? EmptyTargets : seat.Ship.Consoles.ActiveTargets(seat.Cell.ToInt3(), heldKeys);
    }

    /// <summary>
    /// What the pilot asks of the ship this frame: by default (<see cref="FlightAssist"/> = false), the thrusters bound
    /// to currently held console colour-group keys (<see cref="PilotRequest.Console"/>, as in the original); when
    /// <see cref="FlightAssist"/> is on, a velocity and spin command for the automatic thrust allocator.
    /// </summary>
    public PilotRequest PilotControls(float delta)
    {
        if (_seat is not { } seat)
        {
            _mouseDelta = Vector2.Zero;
            return default;
        }
        if (seat.Component.GunsOnly)
        {
            _mouseDelta = Vector2.Zero;
            return PilotRequest.Console(EmptyTargets);
        }
        if (!FlightAssist)
        {
            _mouseDelta = Vector2.Zero;
            return PilotRequest.Console(HeldConsoleTargets(seat));
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
        return new PilotRequest((frame * (move * speed)).ToNumerics(), (frame * turn).ToNumerics(), Dampeners, UseFlightAssist: true);
    }

    /// <summary>Resolves a powered camera block bound to <paramref name="seat"/>'s console, if any, returning its
    /// ship-local frame and eye position.</summary>
    (Basis Frame, Vector3 Eye)? BoundCameraPose(Seat seat)
    {
        if (seat.Ship.Consoles.TryGet(seat.Cell.ToInt3())?.Camera is not { } camInt3) return null;
        var camCell = camInt3.ToGodot();
        var camObj = seat.Ship.ObjectAtOrigin(camCell) ?? seat.Ship.ObjectAt(camCell);
        var camBlock = camObj?.Block ?? seat.Ship.Get(camCell);
        if (camBlock == BlockPalette.Air || _grids.Palette.Item(camBlock).Kind != "camera") return null;
        if (!seat.Ship.Systems.IsBlockActive(new BlockTarget(seat.Ship, camCell, camObj))) return null;

        var turn = camObj?.Orientation ?? seat.Ship.TurnOf(camCell);
        var basis = TurnBasis(turn);
        var center = camObj?.ModelBox.GetCenter() ?? (Vector3)camCell + new Vector3(0.5f, 0.5f, 0.5f);
        var forward = -basis.Z;
        var outside = center + forward * 0.55f;
        var eye = !seat.Ship.IsSolid((Vector3I)outside.Floor()) ? outside : center;
        return (basis, eye);
    }

    /// <summary>Puts the camera where the view mode has it: at the bound camera block (if powered) or the seat, or
    /// chasing the ship from behind; <see cref="PilotView.Fixed"/> leaves it.</summary>
    void FollowShip()
    {
        if (_seat is not { } seat) return;
        var ship = seat.Ship;
        var frame = ConsoleFrame(seat);
        bool freeLook = !FlightAssist || seat.Component.GunsOnly;
        var lookOffset = freeLook ? Basis.FromEuler(new Vector3(_seatPitch, _seatYaw, 0)) : Basis.Identity;
        switch (View)
        {
            case PilotView.Seat:
                if (BoundCameraPose(seat) is { } cam)
                    GlobalTransform = ship.GlobalTransform * new Transform3D(cam.Frame * lookOffset, cam.Eye);
                else
                    GlobalTransform = ship.GlobalTransform * new Transform3D(frame * lookOffset, SeatEye(seat, frame));
                break;
            case PilotView.Chase:
                var basis = ship.GlobalTransform.Basis.Orthonormalized() * (frame * lookOffset);
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
                if (action == PrimaryHold) known.Add(action);
                else if (!InputMap.HasAction(action)) GD.PushWarning($"--hold: there is no action '{action}'");
                else known.Add(action);
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
        _primaryHeld = _secondaryHeld = false;
        _mode.Update(0, null, drilling: false);
    }

    void ValidateBindTool()
    {
        if (_bindTool is not { } bt) return;
        if (!IsInstanceValid(bt.Grid) || !_grids.Contains(bt.Grid))
        {
            _bindTool = null;
            return;
        }
        var obj = bt.Grid.ObjectAtOrigin(bt.ConsoleCell) ?? bt.Grid.ObjectAt(bt.ConsoleCell);
        var block = obj?.Block ?? bt.Grid.Get(bt.ConsoleCell);
        if (block == BlockPalette.Air || _grids.Palette.Item(block).Get<ConsoleComponent>() is null)
            _bindTool = null;
    }

    public override void _Process(double delta)
    {
        AdvanceHolds((float)Math.Min(delta, ShipPhysics.MaxFrame));
        ValidateBindTool();
        _touchCooldown = Math.Max(0, _touchCooldown - delta);
        UndoBob();
        ShowFlashlight();
        if (UpdateDeath(delta)) return;

        // The ship went away, its console was broken, or its power network fell below zero: the pilot is thrown out.
        if (_seat is { } seat)
        {
            if (!_grids.Contains(seat.Ship))
            {
                _seat = null;
                _hud.SetPiloting(false);
                _hud.SelectionVisible = true;
            }
            else if (ConsoleAt(seat.Ship, seat.Cell) is null)
            {
                StandUp();
            }
            else if (!seat.Ship.Systems.IsBlockActive(new BlockTarget(seat.Ship, seat.Cell, seat.Object)))
            {
                StandUp("Console lost power", warning: true);
            }
        }
        if (_seat is not null)
        {
            PilotProcess(delta);
            return;
        }

        // Aboard a ship the player goes where it went this frame (ShipPhysics has already moved it), turning with it.
        CarryWithShip();
        if (_riding is { } aboardShip && IsInstanceValid(aboardShip) && FixedCamera)
            LevelAstronautView();

        // Movement keys are ignored while a window is open (the player coasts to a stop), so typing in a window
        // can never fly the player into something unseen.
        bool windowOpen = WindowOpen;
        var input = new Vector3(
            Axis(GameInput.Right, GameInput.Left),
            Axis(GameInput.Up, GameInput.Down),
            Axis(GameInput.Back, GameInput.Forward));
        var basis = GlobalTransform.Basis;
        var keys = basis.X * input.X + basis.Y * input.Y + basis.Z * input.Z;
        float roll = (_riding is not null && FixedCamera) ? 0f : Axis(GameInput.RollLeft, GameInput.RollRight);
        if (roll != 0)
        {
            RotateObjectLocal(Vector3.Back, roll * WalkRollRate * (float)delta);
            Orthonormalize();
        }
        bool boost = Held(GameInput.Boost) && _mode.TryBoost(delta);
        var motion = new Simulation.Physics.AstronautMotion.Input(keys.ToNumerics(), (-basis.Z).ToNumerics(), Held(GameInput.Forward), boost);
        UpdateBoarding();
        GlobalPosition = Motor.Step(GlobalPosition, motion, (float)delta, _grids, _riding);
        RememberRide();
        HurtByImpact();
        if (UpdateDeath(delta)) return;

        _doorEye = GlobalPosition;
        _grids.World.StepDoors((float)delta, _doorBlocked, _doorRefused);
        var ships = _grids.Ships;
        for (int i = 0; i < ships.Count; i++) ships[i].StepDoors((float)delta, _doorBlocked, _doorRefused);

        if (PinnedAim is { } pin)
        {
            (_hasTarget, _aim) = (_grids.Contains(pin.Grid) && pin.Grid.IsSolid(pin.Hit), pin);
            if (!_hasTarget) PinnedAim = null;
            else _aimDistance = GlobalPosition.DistanceTo(_aim.Grid.ToGlobal((Vector3)_aim.Hit + new Vector3(0.5f, 0.5f, 0.5f)));
        }
        else
        {
            _hasTarget = _grids.Raycast(GlobalPosition, -GlobalTransform.Basis.Z, Reach, out _aim, out _aimDistance);
        }
        if (_hasTarget) _target = _aim.Grid.TargetAt(_aim.Hit);

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        if (!captured) _primaryHeld = false;
        _mode.View = (GlobalPosition, -GlobalTransform.Basis.Z, WorldVelocity);
        _mode.AimDistance = _hasTarget ? _aimDistance : float.PositiveInfinity;

        ShowHighlight();
        ShowGhost();
        ShowBindOverlay();

        _mode.Update(delta, _hasTarget ? _target : null, drilling: PrimaryDown && !windowOpen && _bindTool is null);

        _hud.SetPiloting(false);
        _hud.BoardedVisible = _riding is { } currentShip && IsInstanceValid(currentShip);
        _hud.SetCrosshair(windowOpen ? Hud.CrosshairKind.Default
            : _bindTool is { Kind: BindToolKind.Camera } ? Hud.CrosshairKind.BindCamera
            : _bindTool is { Kind: BindToolKind.Triggers } ? Hud.CrosshairKind.BindBlocks
            : Hud.CrosshairKind.Default);
        _hud.SetCenterPrompt(BuildCenterPrompt());

        CountFrame(delta);

        var p = GlobalPosition;
        bool turnable = _bindTool is null && (_grids.Palette.IsObject(_mode.SelectedBlock) || _grids.Palette.IsTurnable(_mode.SelectedBlock));
        string bindStatus = _bindTool is { } bt
            ? (bt.Kind == BindToolKind.Camera ? "   [Camera binding tool]" : "   [Console binding tool]")
            : "";
        _hud.SetStatus($"FPS {_fps}   position {p.X:0},{p.Y:0},{p.Z:0}" + (_riding is { } aboard ? $"   aboard {aboard.Name}" : "") +
                       _mode.Status + bindStatus +
                       (turnable ? $"   orientation {_mode.Orientation.Describe()}" : "") +
                       (_hasTarget && _target.Grid.BlockOf(_target) is var looked && looked != BlockPalette.Air
                           ? $"   looking at {_grids.Palette.Item(looked).Name}{ShipNote(_target.Grid)}" : ""));
        ApplyBob(Motor.Velocity.Length());
    }

    string BuildCenterPrompt()
    {
        if (WindowOpen || _seat is not null) return "";
        if (_bindTool is { Kind: BindToolKind.Camera })
            return "Choose a camera block you wish to bind to the console!";
        if (_bindTool is { Kind: BindToolKind.Triggers })
            return "Choose blocks you wish to control!\n\nPress F again to assign keys!";
        if (!Rules.Bool(Simulation.Settings.Cvars.ShowHints)) return "";
        if (!_hasTarget || (PinnedAim is null && _aimDistance > 4.5f)) return "";

        var block = _target.Grid.BlockOf(_target);
        if (block == BlockPalette.Air) return "";
        var item = _grids.Palette.Item(block);
        if (item.Get<ConsoleComponent>() is not null)
            return "F to edit\nE to use";

        bool canEdit = _grids.Palette.IsPowered(block) && _target.Grid.Systems.NetworkOf(_target) is not null;
        bool canUse = _target.Object?.Door is not null || item.Get<CraftingStationComponent>() is not null;
        return (canEdit, canUse) switch
        {
            (true, true) => "F to edit\nE to use",
            (true, false) => "F to edit",
            (false, true) => "E to use",
            _ => "",
        };
    }

    /// <summary>
    /// A frame in the pilot's seat: the camera follows the ship, doors keep moving, bound cannons fire when their group
    /// key is held (or all guns on LMB in flight-assistant mode), and the HUD shows speed and the 3D velocity cone.
    /// </summary>
    void PilotProcess(double delta)
    {
        var seat = _seat!;
        var ship = seat.Ship;
        FollowShip();
        // Far outside every grid: the seated body blocks no door.
        _doorEye = new Vector3(-1e5f, -1e5f, -1e5f);
        _grids.World.StepDoors((float)delta, _doorBlocked, _doorRefused);
        var ships = _grids.Ships;
        for (int i = 0; i < ships.Count; i++) ships[i].StepDoors((float)delta, _doorBlocked, _doorRefused);
        _mode.Update(delta, null, drilling: false);

        if (!WindowOpen && _mode.Shots is { } shots)
        {
            double now = Time.GetTicksMsec() / 1000.0;
            if (!FlightAssist || seat.Component.GunsOnly)
            {
                var bound = HeldConsoleTargets(seat);
                if (bound.Count > 0)
                {
                    Vector3? aimDir = seat.Component.GunsOnly ? -GlobalTransform.Basis.Z : null;
                    shots.FireBoundGuns(ship, bound, aimDir, now);
                }
            }
            else if (PrimaryDown)
            {
                shots.FireGuns(ship, now);
            }
        }

        _hud.BoardedVisible = false;
        _hud.SetCrosshair(Hud.CrosshairKind.Default);
        _hud.SetCenterPrompt("");
        _hud.SetPiloting(true, ship.LinearVelocity, GlobalTransform.Basis, FlightAssist && !seat.Component.GunsOnly, Dampeners);

        CountFrame(delta);

        var (supply, demand, _) = ship.Systems.PowerSummary;
        float balance = supply - demand;
        var (position, heading, pitch, roll, turn) = FlightReadout(seat);
        string controlMode = seat.Component.GunsOnly ? "gun console"
            : FlightAssist ? $"flight assistant (dampeners {(Dampeners ? "on" : "off")})"
            : "console bindings";
        _hud.SetStatus(FormattableString.Invariant(
            $"FPS {_fps}   piloting {ship.Name} ({controlMode})   position {position.X:0},{position.Y:0},{position.Z:0}   speed {ship.LinearVelocity.Length():0.0} m/s   ") +
            $"heading {heading}°  pitch {Signed(pitch)}°  roll {Signed(roll)}°   " +
            $"turning pitch {Signed(turn.X)}  yaw {Signed(turn.Y)}  roll {Signed(turn.Z)} °/s\n" +
            FormattableString.Invariant($"mass {ship.Mass.Mass:0.#}   power balance {(balance >= 0 ? "+" : "")}{balance:0.#} ({supply:0.#}/{demand:0.#})   ") +
            $"thrusters {ship.Systems.Firing}/{ship.Systems.Thrusters.Count} firing   " +
            $"view {View.ToString().ToLowerInvariant()}");
    }

    static (Vector3 Position, int Heading, float Pitch, float Roll, Vector3 Turn) FlightReadout(Seat seat)
    {
        var ship = seat.Ship;
        var basis = ship.GlobalTransform.Basis.Orthonormalized() * ConsoleFrame(seat);
        var forward = -basis.Z;
        int heading = ((int)MathF.Round(Mathf.RadToDeg(Mathf.Atan2(forward.X, -forward.Z))) % 360 + 360) % 360;
        float pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(forward.Y, -1, 1)));
        float roll = Mathf.RadToDeg(Mathf.Atan2(-basis.X.Y, basis.Y.Y));
        var spin = basis.Inverse() * ship.AngularVelocity;
        var turn = new Vector3(spin.X, -spin.Y, -spin.Z) * (180 / Mathf.Pi);
        return (ship.ToGlobal(ship.Mass.CenterOfMass.ToGodot()), heading, pitch, roll, turn);
    }

    static string Signed(float degrees)
    {
        int whole = (int)MathF.Round(degrees);
        return whole > 0 ? FormattableString.Invariant($"+{whole}") : FormattableString.Invariant($"{whole}");
    }

    void CountFrame(double delta)
    {
        if (_fpsFrames++ > 0) _fpsWindow += delta;
        if (_fpsWindow >= 0.5 || _fpsFrames > 120)
        {
            _fps = $"{(_fpsFrames - 1) / _fpsWindow:0}";
            (_fpsFrames, _fpsWindow) = (1, 0);
        }
    }

    static string ShipNote(BlockGrid grid) =>
        grid is ShipGrid ship
            ? (ship.IsAsteroid
                ? FormattableString.Invariant($" (Asteroid, {ship.BlockCount} blocks, mass {ship.Mass.Mass:0.#})")
                : FormattableString.Invariant($" ({ship.Name}, {ship.BlockCount} blocks, mass {ship.Mass.Mass:0.#})"))
            : "";

    void ShowHighlight()
    {
        bool miningReady = _mode.SelectedBlock == BlockPalette.Air && (_mode.HeldItem?.Get<DrillComponent>() is not null || !_mode.HasInventory);
        _highlight.Visible = !WindowOpen && _hasTarget && _bindTool is null && miningReady;
        if (!_highlight.Visible) return;
        Transform3D local;
        if (_target.Object is { } obj)
        {
            var size = obj.RotatedSize.ToVector3().ToGodot();
            local = new Transform3D(Basis.Identity.Scaled(size * SelectorScale), obj.Origin.ToGodot() + size * 0.5f);
        }
        else
        {
            var cell = _target.Cell;
            local = new Transform3D(Basis.Identity.Scaled(Vector3.One * SelectorScale), new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));
        }
        _highlight.GlobalTransform = _target.Grid.GlobalTransform * local;
    }

    void ShowBindOverlay()
    {
        _bindOverlayMesh.ClearSurfaces();
        if (_bindTool is not { } bt || !IsInstanceValid(bt.Grid) || !_grids.Contains(bt.Grid))
        {
            _bindOverlay.Visible = false;
            return;
        }

        _bindOverlayMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _bindOverlayMaterial);
        bool any = false;

        void AddBox(BlockGrid grid, Vector3I cell, Color color, float scale = SelectorScale)
        {
            Transform3D worldTransform;
            if ((grid.ObjectAtOrigin(cell) ?? grid.ObjectAt(cell)) is { } obj)
            {
                var size = obj.RotatedSize.ToVector3().ToGodot();
                var center = obj.Origin.ToGodot() + size * 0.5f;
                worldTransform = grid.GlobalTransform * new Transform3D(Basis.Identity.Scaled(size * scale), center);
            }
            else
            {
                worldTransform = grid.GlobalTransform * new Transform3D(Basis.Identity.Scaled(Vector3.One * scale),
                    new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));
            }
            AppendSelectorBox(_bindOverlayMesh, worldTransform, color);
            any = true;
        }

        // Active console in Blue, as in the original.
        AddBox(bt.Grid, bt.ConsoleCell, Color.Color8(0, 48, 255), 1.04f);

        var state = bt.Grid.Consoles.GetOrCreate(bt.ConsoleCell.ToInt3(), bt.Component.Buttons, bt.Component.GunsOnly);
        if (bt.Kind == BindToolKind.Triggers)
        {
            for (int g = 0; g < state.GroupCount; g++)
            {
                var rgb = state.Groups[g].ColorGroup.Color;
                var color = new Color(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
                foreach (var target in state.Groups[g].Targets)
                    AddBox(bt.Grid, target.ToGodot(), color, SelectorScale);
            }
        }
        if (state.Camera is { } cam)
            AddBox(bt.Grid, cam.ToGodot(), new Color(1f, 0.2f, 1f), 1.04f);

        // White highlight on aimed bindable block within 3.0 units on the same grid, as in the original.
        if (!WindowOpen && _hasTarget && _target.Grid == bt.Grid && _aimDistance <= BindReach)
        {
            var aimedCell = _target.Object is { } ao ? ao.Origin.ToGodot() : _target.Cell;
            if (aimedCell != bt.ConsoleCell)
            {
                var aimedBlock = bt.Grid.BlockOf(_target);
                if (aimedBlock != BlockPalette.Air)
                {
                    var aimedItem = _grids.Palette.Item(aimedBlock);
                    bool valid = bt.Kind == BindToolKind.Camera
                        ? aimedItem.Kind == "camera"
                        : (bt.Component.GunsOnly ? aimedItem.Get<GunComponent>() is not null
                            : aimedItem.Get<ThrusterComponent>() is not null || aimedItem.Get<GunComponent>() is not null);
                    if (valid)
                        AddBox(bt.Grid, aimedCell, Colors.White, 1.055f);
                }
            }
        }

        _bindOverlayMesh.SurfaceEnd();
        _bindOverlay.Visible = any;
    }

    void ShowGhost()
    {
        if (WindowOpen || _bindTool is not null)
        {
            _ghostFootprint.Visible = _ghostModel.Visible = _attachLine.Visible = false;
            if (_facing is not null) _facing.Visible = false;
            return;
        }

        if (!_hasTarget)
        {
            ShowSpaceGhost();
            return;
        }

        var plan = _mode.PlanObject(GlobalPosition, _aim);
        if (plan is null)
        {
            _ghostFootprint.Visible = _ghostModel.Visible = false;
            ShowBlockGhost();
            return;
        }

        var p = plan.Value;
        var frame = p.Grid.GlobalTransform;
        var bounds = p.Shape.RotatedSize(p.Orientation).ToVector3().ToGodot();
        var boundsCenter = p.Origin.ToGodot() + bounds * 0.5f;
        _ghostFootprint.GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(bounds * SelectorScale), boundsCenter);
        _ghostFootprint.MaterialOverride = p.Placeable ? _ghostWireOk : _ghostWireBad;
        _ghostFootprint.Visible = true;

        if (_ghostMeshFor != (p.Block, p.Orientation))
        {
            _ghostModel.Mesh = _grids.World.ModelMesh(p.Block, p.Orientation, _ghostModelMaterials[1, 0]);
            _ghostMeshFor = (p.Block, p.Orientation);
        }
        var model = p.Shape.ModelBox(Int3.Zero, p.Orientation);
        var size = model.Size.ToGodot();
        var center = model.Center.ToGodot();
        var scale = new Vector3((size.X + 0.006f) / size.X, (size.Y + 0.006f) / size.Y, (size.Z + 0.006f) / size.Z);
        _ghostModel.GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(scale), p.Origin.ToGodot() + center - center * scale);
        _ghostModel.MaterialOverride = _ghostModelMaterials[p.Placeable ? 1 : 0, _grids.Palette.IsCutout(p.Block) ? 1 : 0];
        _ghostModel.Visible = true;

        _facing ??= FacingArrow();
        _facing.GlobalTransform = ArrowTransform(frame, boundsCenter, p.Origin.ToGodot() + center, size, p.Orientation);
        _facing.Visible = true;

        if (p.Grid is ShipGrid ship)
            ShowAttachmentLine(frame * (p.Origin.ToGodot() + center), ship.GlobalTransform * ship.Mass.CenterOfMass.ToGodot());
        else
            _attachLine.Visible = false;
    }

    void ShowSpaceGhost()
    {
        if (_mode.PlanSpacePlacement(GlobalPosition, -GlobalTransform.Basis.Z) is not { } sp)
        {
            _ghostFootprint.Visible = _ghostModel.Visible = _attachLine.Visible = false;
            if (_facing is not null) _facing.Visible = false;
            return;
        }

        var frame = sp.TargetGrid?.GlobalTransform ?? new Transform3D(Basis.Identity, sp.WorldCenter);
        if (sp.Shape is { } shape)
        {
            var bounds = shape.RotatedSize(sp.Orientation).ToVector3().ToGodot();
            var localOrigin = sp.TargetGrid is not null ? (Vector3)sp.LocalOrigin : -bounds * 0.5f;
            var boundsCenter = localOrigin + bounds * 0.5f;
            _ghostFootprint.GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(bounds * SelectorScale), boundsCenter);
            _ghostFootprint.MaterialOverride = sp.Placeable ? _ghostWireOk : _ghostWireBad;
            _ghostFootprint.Visible = true;

            if (_ghostMeshFor != (sp.Block, sp.Orientation))
            {
                _ghostModel.Mesh = _grids.World.ModelMesh(sp.Block, sp.Orientation, _ghostModelMaterials[1, 0]);
                _ghostMeshFor = (sp.Block, sp.Orientation);
            }
            var model = shape.ModelBox(Int3.Zero, sp.Orientation);
            var size = model.Size.ToGodot();
            var center = model.Center.ToGodot();
            var scale = new Vector3((size.X + 0.006f) / size.X, (size.Y + 0.006f) / size.Y, (size.Z + 0.006f) / size.Z);
            _ghostModel.GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(scale), localOrigin + center - center * scale);
            _ghostModel.MaterialOverride = _ghostModelMaterials[sp.Placeable ? 1 : 0, _grids.Palette.IsCutout(sp.Block) ? 1 : 0];
            _ghostModel.Visible = true;

            _facing ??= FacingArrow();
            _facing.GlobalTransform = ArrowTransform(frame, boundsCenter, localOrigin + center, size, sp.Orientation);
            _facing.Visible = true;
        }
        else
        {
            var turn = _grids.Palette.IsTurnable(sp.Block) ? sp.Orientation : Orientation.Default;
            if (_ghostMeshFor != (sp.Block, turn))
            {
                _ghostModel.Mesh = ChunkMesher.SingleBlock(_grids.Palette, sp.Block, turn, _ghostModelMaterials[1, 0]);
                _ghostMeshFor = (sp.Block, turn);
            }
            const float grow = 1.006f;
            var localOrigin = sp.TargetGrid is not null ? (Vector3)sp.LocalOrigin : new Vector3(-0.5f, -0.5f, -0.5f);
            var center = new Vector3(0.5f, 0.5f, 0.5f);
            var blockCenter = localOrigin + center;
            _ghostFootprint.GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(Vector3.One * SelectorScale), blockCenter);
            _ghostFootprint.MaterialOverride = sp.Placeable ? _ghostWireOk : _ghostWireBad;
            _ghostFootprint.Visible = true;

            _ghostModel.GlobalTransform = frame * new Transform3D(Basis.Identity.Scaled(Vector3.One * grow), localOrigin + center - center * grow);
            _ghostModel.MaterialOverride = _ghostModelMaterials[sp.Placeable ? 1 : 0, _grids.Palette.IsCutout(sp.Block) ? 1 : 0];
            _ghostModel.Visible = true;

            _facing ??= FacingArrow();
            _facing.GlobalTransform = ArrowTransform(frame, blockCenter, blockCenter, Vector3.One, sp.Orientation);
            _facing.Visible = true;
        }

        if (sp.LineTargetWorld is { } targetWorld)
            ShowAttachmentLine(sp.WorldCenter, targetWorld);
        else
            _attachLine.Visible = false;
    }

    /// <summary>Draws the 0.1 × 0.1 red attachment beam between <paramref name="from"/> and <paramref name="to"/>, as in the original.</summary>
    void ShowAttachmentLine(Vector3 from, Vector3 to)
    {
        var diff = to - from;
        float length = diff.Length();
        if (length < 0.05f)
        {
            _attachLine.Visible = false;
            return;
        }
        var forward = diff / length;
        var up = Mathf.Abs(forward.Y) < 0.99f ? Vector3.Up : Vector3.Right;
        var right = forward.Cross(up).Normalized();
        var orthoUp = right.Cross(forward).Normalized();
        var basis = new Basis(right * 0.1f, orthoUp * 0.1f, -forward * length);
        _attachLine.GlobalTransform = new Transform3D(basis, (from + to) * 0.5f);
        _attachLine.Visible = true;
    }

    static Transform3D ArrowTransform(Transform3D frame, Vector3 boundsCenter, Vector3 modelCenter, Vector3 modelSize, Orientation orientation)
    {
        var basis = TurnBasis(orientation);
        var up = basis.Y;
        float halfAlongUp = 0.5f * (Mathf.Abs(up.X) * modelSize.X + Mathf.Abs(up.Y) * modelSize.Y + Mathf.Abs(up.Z) * modelSize.Z);
        float modelTopFromBoundsCenter = (modelCenter - boundsCenter).Dot(up) + halfAlongUp;
        float shift = MathF.Max(0f, modelTopFromBoundsCenter - 0.5f);
        return frame * new Transform3D(basis, boundsCenter + up * shift);
    }

    static Basis TurnBasis(Orientation o) => new(
        o.Apply(System.Numerics.Vector3.UnitX).ToGodot(),
        o.Apply(System.Numerics.Vector3.UnitY).ToGodot(),
        o.Apply(System.Numerics.Vector3.UnitZ).ToGodot());

    MeshInstance3D? _facing;

    MeshInstance3D FacingArrow()
    {
        static StandardMaterial3D Paint(Color c) => new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = c,
        };
        var mesh = new ArrayMesh();
        var shaft = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 1.0f, RadialSegments = 10 };
        var head = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.24f, Height = 0.45f, RadialSegments = 14 };
        var st = new SurfaceTool();
        st.AppendFrom(shaft, 0, new Transform3D(Basis.Identity, new Vector3(0, 0.5f, 0)));
        st.SetMaterial(Paint(new Color(0.95f, 0.08f, 0.06f)));
        st.Commit(mesh);
        st = new SurfaceTool();
        st.AppendFrom(head, 0, new Transform3D(Basis.Identity, new Vector3(0, 1.225f, 0)));
        st.SetMaterial(Paint(Colors.White));
        st.Commit(mesh);
        var arrow = new MeshInstance3D { Mesh = mesh, TopLevel = true, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(arrow);
        return arrow;
    }

    void ShowBlockGhost()
    {
        var block = _mode.SelectedBlock;
        if (!_hasTarget || WindowOpen || block == BlockPalette.Air || _grids.Palette.IsObject(block) || _aim.Hit == _aim.Before)
        {
            _ghostFootprint.Visible = _ghostModel.Visible = _attachLine.Visible = false;
            if (_facing is not null) _facing.Visible = false;
            return;
        }
        var turn = _grids.Palette.IsTurnable(block) ? _mode.Orientation : Orientation.Default;
        if (_ghostMeshFor != (block, turn))
        {
            _ghostModel.Mesh = ChunkMesher.SingleBlock(_grids.Palette, block, turn, _ghostModelMaterials[1, 0]);
            _ghostMeshFor = (block, turn);
        }
        const float grow = 1.006f;
        var cell = (Vector3)_aim.Before;
        var center = new Vector3(0.5f, 0.5f, 0.5f);
        var blockCenter = cell + center;
        bool fits = _mode.PlacementProblem(GlobalPosition, _aim.Grid, _aim.Before) is null;

        _ghostFootprint.GlobalTransform = _aim.Grid.GlobalTransform *
                                          new Transform3D(Basis.Identity.Scaled(Vector3.One * SelectorScale), blockCenter);
        _ghostFootprint.MaterialOverride = fits ? _ghostWireOk : _ghostWireBad;
        _ghostFootprint.Visible = true;

        _ghostModel.GlobalTransform = _aim.Grid.GlobalTransform *
                                      new Transform3D(Basis.Identity.Scaled(Vector3.One * grow), cell + center - center * grow);
        _ghostModel.MaterialOverride = _ghostModelMaterials[fits ? 1 : 0, _grids.Palette.IsCutout(block) ? 1 : 0];
        _ghostModel.Visible = true;

        _facing ??= FacingArrow();
        _facing.GlobalTransform = ArrowTransform(_aim.Grid.GlobalTransform, blockCenter, blockCenter, Vector3.One, _mode.Orientation);
        _facing.Visible = true;

        if (_aim.Grid is ShipGrid ship)
            ShowAttachmentLine(_aim.Grid.ToGlobal(blockCenter), ship.GlobalTransform * ship.Mass.CenterOfMass.ToGodot());
        else
            _attachLine.Visible = false;
    }

    float Axis(string positive, string negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);

    // ---- 8-corner selector box (classic Corneroids squareCorners style) ----

    static readonly (Vector3 Normal, Vector3 Right, Vector3 Up)[] SelectorFaces =
    [
        (Vector3.Right, new(0, 0, -1), Vector3.Up),
        (Vector3.Left, new(0, 0, 1), Vector3.Up),
        (Vector3.Back, Vector3.Right, Vector3.Up),
        (Vector3.Forward, Vector3.Left, Vector3.Up),
        (Vector3.Up, Vector3.Right, Vector3.Forward),
        (Vector3.Down, Vector3.Right, Vector3.Back),
    ];

    static ImageTexture CornerBracketTexture()
    {
        const int size = 64, arm = 13, thick = 4;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool xOuter = x < thick || x >= size - thick;
            bool yOuter = y < thick || y >= size - thick;
            bool xArm = x < arm || x >= size - arm;
            bool yArm = y < arm || y >= size - arm;
            if ((xOuter && yArm) || (yOuter && xArm))
                img.SetPixel(x, y, Colors.White);
        }
        return ImageTexture.CreateFromImage(img);
    }

    static StandardMaterial3D SelectorMaterial(Texture2D cornerTex, Color color, bool vertexColors = false) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
        AlphaScissorThreshold = 0.5f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        AlbedoTexture = cornerTex,
        AlbedoColor = color,
        VertexColorUseAsAlbedo = vertexColors,
    };

    static ArrayMesh SelectorBoxMesh(Material material)
    {
        var vertices = new Vector3[24];
        var uvs = new Vector2[24];
        var indices = new int[36];
        for (int f = 0; f < SelectorFaces.Length; f++)
        {
            var (n, right, up) = SelectorFaces[f];
            Vector3 c = n * 0.5f, r = right * 0.5f, u = up * 0.5f;
            int v = f * 4, idx = f * 6;
            vertices[v + 0] = c - r + u;
            vertices[v + 1] = c + r + u;
            vertices[v + 2] = c + r - u;
            vertices[v + 3] = c - r - u;
            uvs[v + 0] = new Vector2(0, 0);
            uvs[v + 1] = new Vector2(1, 0);
            uvs[v + 2] = new Vector2(1, 1);
            uvs[v + 3] = new Vector2(0, 1);
            indices[idx + 0] = v + 0;
            indices[idx + 1] = v + 1;
            indices[idx + 2] = v + 2;
            indices[idx + 3] = v + 0;
            indices[idx + 4] = v + 2;
            indices[idx + 5] = v + 3;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    static void AppendSelectorBox(ImmediateMesh mesh, Transform3D transform, Color color)
    {
        mesh.SurfaceSetColor(color);
        for (int f = 0; f < SelectorFaces.Length; f++)
        {
            var (n, right, up) = SelectorFaces[f];
            Vector3 c = n * 0.5f, r = right * 0.5f, u = up * 0.5f;
            Vector3 v0 = transform * (c - r + u);
            Vector3 v1 = transform * (c + r + u);
            Vector3 v2 = transform * (c + r - u);
            Vector3 v3 = transform * (c - r - u);

            mesh.SurfaceSetUV(new Vector2(0, 0)); mesh.SurfaceAddVertex(v0);
            mesh.SurfaceSetUV(new Vector2(1, 0)); mesh.SurfaceAddVertex(v1);
            mesh.SurfaceSetUV(new Vector2(1, 1)); mesh.SurfaceAddVertex(v2);

            mesh.SurfaceSetUV(new Vector2(0, 0)); mesh.SurfaceAddVertex(v0);
            mesh.SurfaceSetUV(new Vector2(1, 1)); mesh.SurfaceAddVertex(v2);
            mesh.SurfaceSetUV(new Vector2(0, 1)); mesh.SurfaceAddVertex(v3);
        }
    }
}
