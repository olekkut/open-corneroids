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
    }

    static void Add(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
