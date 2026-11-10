using Godot;
using OpenCorneroids.Simulation.Ships;

namespace OpenCorneroids.Game;

/// <summary>
/// Input actions, registered in code. Physical keys, so WASD stays under the same fingers on Ukrainian,
/// Polish or any other keyboard layout. The defaults are the original's: Z/X roll, E use, F edit,
/// C inventory, B beacon tool, R orientate the block to place, T flashlight. Keys the original lacks are ours: L levels the view, N
/// noclip, I opens the inventory too, F5 saves, H toggles the optional flight assistant (console key bindings are the
/// default), G toggles the flight assistant's dampeners, K toggles fixed camera when aboard, V toggles the pilot's view.
/// </summary>
public static class GameInput
{
    public const string Forward = "move_forward";
    public const string Back = "move_back";
    public const string Left = "move_left";
    public const string Right = "move_right";
    public const string Up = "move_up";
    public const string Down = "move_down";
    public const string Boost = "move_boost";
    public const string Noclip = "toggle_noclip";
    /// <summary>Use the targeted block: a console's seat, a crafting station, a door. E, as in the original.</summary>
    public const string Interact = "interact";
    /// <summary>Edit the targeted block (a console's bindings, or a power block's network status). F, as in the original.</summary>
    public const string Edit = "edit_block";
    /// <summary>The helmet's flashlight. T, as in the original.</summary>
    public const string Flashlight = "toggle_flashlight";
    /// <summary>Opens and closes the inventory window.</summary>
    public const string Inventory = "toggle_inventory";
    /// <summary>Puts the beacon tool in hand, which marks points in space (B, as in the original).</summary>
    public const string BeaconTool = "beacon_tool";
    /// <summary>Turns the astronaut back upright (keeping the heading): no roll, and a level view if within reach.</summary>
    public const string Level = "level_view";
    /// <summary>Locks the astronaut's camera up vector to the ship's deck while aboard (ours, K; also in original options).</summary>
    public const string FixedCamera = "toggle_fixed_camera";

    /// <summary>Turns the block about to be placed to its next orientation (R, or the middle mouse button).</summary>
    public const string Spin = "turn_spin";
    /// <summary>Opens the welcome window (F1, as in the original); its Controls button opens our key list.</summary>
    public const string Help = "toggle_help";
    /// <summary>Toggles the technical/debug telemetry readout in the top-left corner (F3).</summary>
    public const string DebugHud = "toggle_debug_hud";
    /// <summary>Saves the world (quicksave). F5, as in most PC games.</summary>
    public const string Save = "save_world";
    /// <summary>Shows or hides the entire HUD (F6, as in the original).</summary>
    public const string ToggleHud = "toggle_hud";
    /// <summary>Opens or closes the in-game developer/debug console (` / ~).</summary>
    public const string DebugConsole = "toggle_debug_console";

    // Piloting a ship. By default consoles use their bound colour-group keys (as in the original); H toggles our
    // optional flight assistant, where forward, back and strafing reuse the move actions and roll is Z/X.
    public const string FlightAssist = "toggle_flight_assist";
    /// <summary>Ship up along its own axis (Space).</summary>
    public const string ShipUp = "ship_up";
    /// <summary>Ship down along its own axis (Ctrl).</summary>
    public const string ShipDown = "ship_down";
    public const string RollLeft = "ship_roll_left";
    public const string RollRight = "ship_roll_right";
    /// <summary>Nose up, as moving the mouse up does (arrow keys steer like the mouse, for keyboard pilots).</summary>
    public const string PitchUp = "ship_pitch_up";
    public const string PitchDown = "ship_pitch_down";
    public const string YawLeft = "ship_yaw_left";
    public const string YawRight = "ship_yaw_right";
    /// <summary>Inertial dampeners on/off: the flight assistant also cancels drift.</summary>
    public const string Dampeners = "toggle_dampeners";
    /// <summary>Pilot's view: from the seat (or bound camera), or chasing the ship from behind.</summary>
    public const string PilotView = "pilot_view";

    public static void Register()
    {
        Add(Forward, Key.W);
        Add(Back, Key.S);
        Add(Left, Key.A);
        Add(Right, Key.D);
        Add(Up, Key.Space);
        Add(Down, Key.Ctrl);
        Add(Level, Key.L);
        Add(FixedCamera, Key.K);
        Add(Edit, Key.F);
        Add(Flashlight, Key.T);
        Add(Boost, Key.Shift);
        Add(Noclip, Key.N);
        Add(Interact, Key.E);
        Add(Inventory, Key.C, Key.I);
        Add(BeaconTool, Key.B);
        Add(Spin, Key.R);
        Add(Help, Key.F1);
        Add(DebugHud, Key.F3);
        Add(Save, Key.F5);
        Add(ToggleHud, Key.F6);
        Add(DebugConsole, Key.Quoteleft, Key.Section);
        Add(FlightAssist, Key.H);
        Add(ShipUp, Key.Space);
        Add(ShipDown, Key.Ctrl);
        Add(RollLeft, Key.Z);
        Add(RollRight, Key.X);
        Add(PitchUp, Key.Up);
        Add(PitchDown, Key.Down);
        Add(YawLeft, Key.Left);
        Add(YawRight, Key.Right);
        Add(Dampeners, Key.G);
        Add(PilotView, Key.V);
    }

    static void Add(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        Defaults[action] = keys.Length > 0 ? keys[0] : Key.None;
    }

    static readonly Dictionary<string, Key> Defaults = new();

    /// <summary>
    /// A row of the Controls window: a label and the actions one key drives (Up moves the astronaut up and the ship up).
    /// The original's rows come first, in its order; the rest are ours. Number keys, Escape and the function keys are
    /// not rebindable, as in the original.
    /// </summary>
    public sealed record Binding(string Label, string[] Actions, bool Ours = false)
    {
        public string Name => Actions[0];
    }

    public static readonly IReadOnlyList<Binding> Bindings =
    [
        new("Use", [Interact]),
        new("Edit", [Edit]),
        new("Forward", [Forward]),
        new("Backward", [Back]),
        new("Strafe left", [Left]),
        new("Strafe right", [Right]),
        new("Boost", [Boost]),
        new("Up", [Up, ShipUp]),
        new("Down", [Down, ShipDown]),
        new("Orientate block", [Spin]),
        new("Inventory", [Inventory]),
        new("Beacon tool", [BeaconTool]),
        new("Roll left", [RollLeft]),
        new("Roll right", [RollRight]),
        new("Flashlight", [Flashlight]),
        new("Level view", [Level], Ours: true),
        new("Fixed camera", [FixedCamera], Ours: true),
        new("Flight assistant", [FlightAssist], Ours: true),
        new("Dampeners", [Dampeners], Ours: true),
        new("Pilot's view", [PilotView], Ours: true),
        new("Pitch up", [PitchUp], Ours: true),
        new("Pitch down", [PitchDown], Ours: true),
        new("Yaw left", [YawLeft], Ours: true),
        new("Yaw right", [YawRight], Ours: true),
        new("Noclip", [Noclip], Ours: true),
    ];

    public static Binding? FindBinding(string name) =>
        Bindings.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                     b.Label.Replace(" ", "").Equals(name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));

    /// <summary>The key a binding row has now (its first), or <see cref="Key.None"/>.</summary>
    public static Key KeyOf(Binding binding) =>
        InputMap.HasAction(binding.Name) && InputMap.ActionGetEvents(binding.Name).OfType<InputEventKey>().FirstOrDefault() is { } e
            ? (e.PhysicalKeycode != Key.None ? e.PhysicalKeycode : e.Keycode)
            : Key.None;

    public static Key DefaultKeyOf(Binding binding) => Defaults.GetValueOrDefault(binding.Name, Key.None);

    /// <summary>Whether <paramref name="key"/> may be bound: not a number key, Escape or a function key.</summary>
    public static bool Rebindable(Key key) =>
        key != Key.None && key != Key.Escape && !(key >= Key.Key0 && key <= Key.Key9) && !(key >= Key.F1 && key <= Key.F35) &&
        key != Key.Quoteleft && key != Key.Section;

    /// <summary>
    /// Binds <paramref name="key"/> to <paramref name="binding"/>, replacing its keys, and takes it off any other row
    /// that had it (that row then shows "None"), as in the original.
    /// </summary>
    public static void Rebind(Binding binding, Key key)
    {
        if (key != Key.None)
            foreach (var other in Bindings)
                if (other != binding && KeyOf(other) == key) SetKey(other, Key.None);
        SetKey(binding, key);
    }

    static void SetKey(Binding binding, Key key)
    {
        foreach (var action in binding.Actions)
        {
            InputMap.ActionEraseEvents(action);
            if (key != Key.None) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }

    /// <summary>Every row back to its default key (with the default's second keys, such as I for the inventory).</summary>
    public static void ResetBindings()
    {
        foreach (var binding in Bindings)
            foreach (var action in binding.Actions)
            {
                InputMap.ActionEraseEvents(action);
                if (Defaults.GetValueOrDefault(action) is var key and not Key.None)
                    InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
            }
        InputMap.ActionAddEvent(Inventory, new InputEventKey { PhysicalKeycode = Key.I });
    }

    /// <summary>A key's name for the Controls window and config files: "W", "Shift", "Space", "None".</summary>
    public static string KeyName(Key key) => key == Key.None ? "None" : OS.GetKeycodeString(key);

    public static Key ParseKey(string name) =>
        name.Equals("None", StringComparison.OrdinalIgnoreCase) ? Key.None : OS.FindKeycodeFromString(name.Trim());

    /// <summary>The changed bindings as <c>bind "key" "action"</c> lines for config.cfg.</summary>
    public static IEnumerable<string> BindLines() =>
        Bindings.Where(b => KeyOf(b) != DefaultKeyOf(b)).Select(b => $"bind \"{KeyName(KeyOf(b))}\" \"{b.Name}\"");

    /// <summary>Readable key name stored for console colour-group bindings (e.g. "W", "R", "Space", "Up").</summary>
    public static string ConsoleKeyName(Key key) => key == Key.None ? "" : OS.GetKeycodeString(key).Trim();

    /// <summary>
    /// Whether a console group's bound <paramref name="keyName"/> is held right now: either on the keyboard (when
    /// <paramref name="realInputAllowed"/> is true) or through a <c>--hold</c> action matching that key.
    /// </summary>
    public static bool IsConsoleKeyHeld(string keyName, Func<string, bool> heldAction, bool realInputAllowed)
    {
        if (string.IsNullOrWhiteSpace(keyName) ||
            string.Equals(keyName, ConsoleBinding.DefaultKey, StringComparison.OrdinalIgnoreCase))
            return false;

        if (realInputAllowed)
        {
            var code = OS.FindKeycodeFromString(keyName);
            if (code != Key.None && (Input.IsPhysicalKeyPressed(code) || Input.IsKeyPressed(code)))
                return true;
        }

        return keyName.ToUpperInvariant() switch
        {
            "W" => heldAction(Forward),
            "S" => heldAction(Back),
            "A" => heldAction(Left) || heldAction(YawLeft),
            "D" => heldAction(Right) || heldAction(YawRight),
            "SPACE" => heldAction(Up) || heldAction(ShipUp),
            "CTRL" => heldAction(Down) || heldAction(ShipDown),
            "SHIFT" => heldAction(Boost),
            "Z" => heldAction(RollLeft),
            "X" => heldAction(RollRight),
            "UP" => heldAction(PitchUp),
            "DOWN" => heldAction(PitchDown),
            "LEFT" => heldAction(YawLeft),
            "RIGHT" => heldAction(YawRight),
            _ => false,
        };
    }
}
