using Godot;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// What the player's hands do, which is the only thing survival and creative disagree on: how mining works, where
/// placed blocks come from, and what the hotbar holds. The camera owns aiming and input and calls in here, so the
/// two rule sets cannot drift apart in how they are controlled.
/// </summary>
public abstract class PlayMode
{
    protected PlayMode(VoxelWorld world, Hud hud)
    {
        World = world;
        Hud = hud;
    }

    protected VoxelWorld World { get; }
    protected Hud Hud { get; }
    protected BlockPalette Palette => World.Palette;

    /// <summary>Whether this mode has an inventory to open and crafting stations to use.</summary>
    public abstract bool HasInventory { get; }

    /// <summary>The key hints shown at the top of the screen.</summary>
    public abstract string Controls { get; }

    /// <summary>Extra text for the status line, e.g. the current drill power.</summary>
    public virtual string Status => "";

    /// <summary>Picks hotbar slot 0..8 (number keys 1-9).</summary>
    public abstract void Select(int slot);

    /// <summary>Moves the selection by <paramref name="delta"/> slots (mouse wheel).</summary>
    public abstract void Cycle(int delta);

    /// <summary>The primary button went down while aiming at <paramref name="target"/> (null when aiming at nothing).</summary>
    public virtual void PrimaryPressed(Vector3I? target) { }

    /// <summary>Called every frame. <paramref name="drilling"/> is true while the primary button is held in play
    /// (not while a window is open); <paramref name="target"/> is the aimed block or null.</summary>
    public virtual void Update(double delta, Vector3I? target, bool drilling) { }

    /// <summary>The secondary button: put the selected block into the empty cell <paramref name="cell"/>.</summary>
    public abstract void Place(Vector3 eye, Vector3I cell);

    /// <summary>
    /// Shared refusals for placing: outside the world, into a block, or into the player's own body. The body check
    /// covers the whole 0.6 × 1.8 × 0.6 box, not just the eye cell, so a block can't be placed at the player's feet
    /// and leave them embedded.
    /// </summary>
    protected bool CanPlaceAt(Vector3 eye, Vector3I cell)
    {
        if (!VoxelWorld.InBounds(cell) || World.Get(cell) != BlockPalette.Air) return false;
        if (!PlayerMotor.BodyOccupies(eye, cell)) return true;
        Hud.Flash("No room: you are in the way", warning: true);
        return false;
    }
}
