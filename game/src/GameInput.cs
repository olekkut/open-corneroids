using Godot;

namespace OpenCorneroids.Game;

/// <summary>
/// Input actions, registered in code. Physical keys, so WASD stays under the same fingers on Ukrainian,
/// Polish or any other keyboard layout. The defaults are the original's: Z/X roll, E use, F edit,
/// C inventory, R orientate the block to place, T flashlight. Keys the original lacks are ours: L levels the view, N
/// noclip, I opens the inventory too, F5 saves, G toggles the flight assistant's dampeners, V the pilot's view.
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
    /// <summary>Edit the targeted block (a console's bindings). F, as in the original.</summary>
    public const string Edit = "edit_block";
    /// <summary>The helmet's flashlight. T, as in the original.</summary>
    public const string Flashlight = "toggle_flashlight";
    /// <summary>Opens and closes the inventory window.</summary>
    public const string Inventory = "toggle_inventory";
    /// <summary>Turns the astronaut back upright (keeping the heading): no roll, and a level view if within reach.</summary>
    public const string Level = "level_view";

    /// <summary>Turns the block about to be placed to its next orientation (R, or the middle mouse button).</summary>
    public const string Spin = "turn_spin";
    /// <summary>Saves the world (quicksave). F5, as in most PC games.</summary>
    public const string Save = "save_world";

    // Piloting a ship with the flight assistant. Forward, back and strafing reuse the move actions; roll is shared
    // with the astronaut (Z/X).
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
    /// <summary>Pilot's view: from the seat, or chasing the ship from behind.</summary>
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
        Add(Edit, Key.F);
        Add(Flashlight, Key.T);
        Add(Boost, Key.Shift);
        Add(Noclip, Key.N);
        Add(Interact, Key.E);
        Add(Inventory, Key.C, Key.I);
        Add(Spin, Key.R);
        Add(Save, Key.F5);
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
    }
}
