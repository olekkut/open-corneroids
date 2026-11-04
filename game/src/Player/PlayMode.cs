using Godot;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Shapes;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.Player;

/// <summary>Where the selected multi-block would go: what the ghost preview shows and what a right click places.</summary>
/// <param name="Grid">The grid it would go into (the aimed one); <see cref="Origin"/> and <see cref="Orientation"/>
/// are in its local cells and axes.</param>
/// <param name="Problem">Why it can't be placed there, or null when it can.</param>
public readonly record struct ObjectPlan(BlockGrid Grid, ushort Block, BlockShape Shape, Int3 Origin, Orientation Orientation, string? Problem)
{
    public bool Placeable => Problem is null;
}

/// <summary>
/// What the player's hands do, which is the only thing survival and creative disagree on: how mining works, where
/// placed blocks come from, and what the hotbar holds. The camera owns aiming and input and calls in here, so the
/// two rule sets cannot drift apart in how they are controlled.
/// </summary>
public abstract class PlayMode
{
    protected PlayMode(GridSet grids, Hud hud)
    {
        Grids = grids;
        Hud = hud;
    }

    /// <summary>The world and the ships; mining and placing work the same in each.</summary>
    protected GridSet Grids { get; }
    protected Hud Hud { get; }
    protected BlockPalette Palette => Grids.Palette;

    /// <summary>How the next multi-block is turned (R spins, T changes the up axis), relative to the axes of the grid it
    /// goes into, so on a turned ship it turns with the ship. Single-cell blocks are never turned: the chunk grid has
    /// no orientation, and their faces look the same from every side but top and bottom.</summary>
    public Orientation Orientation { get; set; }

    /// <summary>Whether this mode has an inventory to open and crafting stations to use.</summary>
    public abstract bool HasInventory { get; }

    /// <summary>The key hints shown at the top of the screen.</summary>
    public abstract string Controls { get; }

    /// <summary>Extra text for the status line, e.g. the current drill power.</summary>
    public virtual string Status => "";

    /// <summary>The palette index the secondary button would place, or air when the selection is not a placeable block.</summary>
    public abstract ushort SelectedBlock { get; }

    /// <summary>Selects <paramref name="block"/> if this mode can (creative: always; survival: when a hotbar slot holds it).</summary>
    public abstract bool TrySelectBlock(ushort block);

    /// <summary>Picks a hotbar slot from 0 (number keys from 1).</summary>
    public abstract void Select(int slot);

    /// <summary>Moves the selection by <paramref name="delta"/> slots (mouse wheel).</summary>
    public abstract void Cycle(int delta);

    /// <summary>The primary button went down while aiming at <paramref name="target"/> (null when aiming at nothing).</summary>
    public virtual void PrimaryPressed(BlockTarget? target) { }

    /// <summary>Called every frame. <paramref name="drilling"/> is true while the primary button is held in play
    /// (not while a window is open); <paramref name="target"/> is the aimed block or null.</summary>
    public virtual void Update(double delta, BlockTarget? target, bool drilling) { }

    /// <summary>
    /// The secondary button with an item selected that is used rather than placed (a consumable, something to wear).
    /// Returns true when the press was handled, so it doesn't also try to place; called whether or not a block is aimed at.
    /// </summary>
    public virtual bool UseSelected() => false;

    /// <summary>Whether boosting may go on for <paramref name="delta"/> more seconds; survival pays suit power for it.</summary>
    public virtual bool TryBoost(double delta) => true;

    /// <summary>The secondary button: put the selected block against the aimed solid cell, starting in the empty cell
    /// in front of the aimed face, in the aimed grid.</summary>
    public abstract void Place(Vector3 eye, Aim aim);

    /// <summary>
    /// Where the selected block would go if it is a multi-block, or null (a single-cell block, nothing selected, or no
    /// face aimed at). The footprint is anchored with <see cref="BlockShape.AnchorFor"/>: it starts in front of the
    /// aimed face, grows away from it and is centred across it.
    /// </summary>
    public ObjectPlan? PlanObject(Vector3 eye, Aim aim)
    {
        var block = SelectedBlock;
        if (block == BlockPalette.Air || Palette.Shape(block) is not { } shape) return null;
        var normal = (aim.Before - aim.Hit).ToInt3();
        // The ray started inside a solid cell: there is no face to place against.
        if (AxisDirections.FromInt3(normal) is null) return null;
        var origin = shape.AnchorFor(aim.Before.ToInt3(), normal, Orientation);
        var problem = aim.Grid.ObjectProblem(block, origin, Orientation)
                      ?? Grids.OverlapProblem(aim.Grid, shape.Footprint(origin, Orientation).Select(c => c.ToGodot()));
        if (problem is null)
        {
            var model = shape.ModelCells(origin, Orientation).Select(c => c.ToGodot()).ToHashSet();
            if (PlayerMotor.BodyOverlaps(aim.Grid, eye, model.Contains)) problem = "No room: you are in the way";
        }
        return new ObjectPlan(aim.Grid, block, shape, origin, Orientation, problem);
    }

    /// <summary>
    /// Places <paramref name="block"/> (a grid block into the aimed empty cell, or a multi-block as
    /// <see cref="PlanObject"/> has it) in the aimed grid once every check has passed and <paramref name="pay"/> agreed,
    /// so a refused placement never costs an item.
    /// </summary>
    protected bool TryPlace(Vector3 eye, Aim aim, ushort block, Func<bool> pay)
    {
        if (!Palette.IsObject(block))
            return CanPlaceAt(eye, aim.Grid, aim.Before) && pay() && aim.Grid.Set(aim.Before, block);

        if (PlanObject(eye, aim) is not { } plan) return false;
        if (plan.Problem is { } problem)
        {
            Hud.Flash(problem, warning: true);
            return false;
        }
        return pay() && plan.Grid.AddObject(plan.Block, plan.Origin, plan.Orientation) is not null;
    }

    /// <summary>
    /// Shared refusals for placing a grid block: outside the world, into a block or an object's footprint (thruster
    /// exhaust and door travel are reserved even though they are not solid) of this grid or of another grid that reaches
    /// into the cell (a ship beside rock, see <see cref="GridSet.OverlapProblem"/>), or into the player's own body. The body
    /// check covers the whole 0.6 × 1.8 × 0.6 box, not just the eye cell, so a block can't be placed at the player's
    /// feet and leave them embedded. Each refusal says why, since a reserved cell looks like empty space.
    /// </summary>
    protected bool CanPlaceAt(Vector3 eye, BlockGrid grid, Vector3I cell)
    {
        if ((grid.ReservedProblem(cell) ?? Grids.OverlapProblem(grid, [cell])) is { } problem)
        {
            Hud.Flash(problem, warning: true);
            return false;
        }
        if (!PlayerMotor.BodyOccupies(grid, eye, cell)) return true;
        Hud.Flash("No room: you are in the way", warning: true);
        return false;
    }
}
