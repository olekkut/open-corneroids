using Godot;

namespace OpenCorneroids.Game;

/// <summary>
/// Input actions, registered in code. Physical keys, so WASD stays under the same fingers on Ukrainian,
/// Polish or any other keyboard layout.
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
    /// <summary>Use the targeted block, e.g. open a crafting station. F, because Space/E/Ctrl/Q are movement.</summary>
    public const string Interact = "interact";
    /// <summary>Opens and closes the inventory window.</summary>
    public const string Inventory = "toggle_inventory";
    /// <summary>Spins the multi-block about to be placed a quarter turn about its up axis.</summary>
    public const string Spin = "turn_spin";
    /// <summary>Points the up axis of the multi-block about to be placed to the next of the six directions.</summary>
    public const string TurnUp = "turn_up";
    /// <summary>Saves the world (quicksave). F5, as in most PC games.</summary>
    public const string Save = "save_world";

    // Piloting a ship. Forward, back and strafing reuse the move actions; up and down get their own actions because
    // Q and E roll the ship instead.
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
        Add(Up, Key.Space, Key.E);
        Add(Down, Key.Ctrl, Key.Q);
        Add(Boost, Key.Shift);
        Add(Noclip, Key.N);
        Add(Interact, Key.F);
        Add(Inventory, Key.I, Key.Tab);
        Add(Spin, Key.R);
        Add(TurnUp, Key.T);
        Add(Save, Key.F5);
        Add(ShipUp, Key.Space);
        Add(ShipDown, Key.Ctrl);
        Add(RollLeft, Key.Q);
        Add(RollRight, Key.E);
        Add(PitchUp, Key.Up);
        Add(PitchDown, Key.Down);
        Add(YawLeft, Key.Left);
        Add(YawRight, Key.Right);
        Add(Dampeners, Key.Z);
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
