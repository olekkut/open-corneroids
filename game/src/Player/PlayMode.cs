using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Ui;
using OpenCorneroids.Game.World;
using OpenCorneroids.Simulation.Shapes;
using OpenCorneroids.Simulation.Ships;
using Orientation = OpenCorneroids.Simulation.Shapes.Orientation;

namespace OpenCorneroids.Game.Player;

/// <summary>Where the selected multi-block would go: what the ghost preview shows and what a placement action places.</summary>
/// <param name="Grid">The grid it would go into (the aimed one); <see cref="Origin"/> and <see cref="Orientation"/>
/// are in its local cells and axes.</param>
/// <param name="Problem">Why it can't be placed there, or null when it can.</param>
public readonly record struct ObjectPlan(BlockGrid Grid, ushort Block, BlockShape Shape, Int3 Origin, Orientation Orientation, string? Problem)
{
    public bool Placeable => Problem is null;
}

/// <summary>
/// Where a block placed in empty space (no block face hit within reach) would go, as in the original: at
/// <see cref="ConsoleBinding.PlacementReach"/> in front of the eye, attaching to the nearest grid within
/// <see cref="ConsoleBinding.MaxAttachmentDistance"/> (16 units) with a red line to <see cref="LineTargetWorld"/>, or
/// starting a new floating ship when nothing is within 16 units (<see cref="TargetGrid"/> is null).
/// </summary>
public readonly record struct SpacePlacementPlan(
    ushort Block,
    Vector3 WorldCenter,
    BlockGrid? TargetGrid,
    Vector3I LocalOrigin,
    BlockShape? Shape,
    Orientation Orientation,
    Vector3? LineTargetWorld,
    string? Problem)
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

    /// <summary>Where shots go; set by the world. Null (no weapons) until then.</summary>
    public Projectiles? Shots { get; set; }

    /// <summary>The player's eye, view direction and velocity this frame; the camera sets them before
    /// <see cref="Update"/>, so a mode can fire from where the player looks.</summary>
    public (Vector3 Eye, Vector3 Forward, Vector3 Velocity) View { get; set; }

    /// <summary>How far along the view the aimed block starts (the ray's hit), set with <see cref="View"/>; infinite
    /// when nothing is aimed at.</summary>
    public float AimDistance { get; set; } = float.PositiveInfinity;

    /// <summary>Gives the player a velocity change (a weapon's recoil); set by the camera.</summary>
    public Action<Vector3>? Push { get; set; }

    /// <summary>The item to draw in the player's hand, or null; survival shows the selected tool or weapon.</summary>
    public virtual ItemDef? HeldItem => null;

    /// <summary>How the next block is turned (R spins, middle mouse changes the up axis), relative to the axes of the
    /// grid it goes into, so on a turned ship it turns with the ship.</summary>
    public Orientation Orientation { get; set; }

    /// <summary>Whether this mode has an inventory to open and crafting stations to use.</summary>
    public abstract bool HasInventory { get; }

    /// <summary>The key hints shown at the top of the screen.</summary>
    public abstract string Controls { get; }

    /// <summary>Extra text for the status line, e.g. the current drill power.</summary>
    public virtual string Status => "";

    /// <summary>The palette index the placement button would place, or air when the selection is not a placeable block.</summary>
    public abstract ushort SelectedBlock { get; }

    /// <summary>Selects <paramref name="block"/> if this mode can (creative: always; survival: when a hotbar slot holds it).</summary>
    public abstract bool TrySelectBlock(ushort block);

    /// <summary>Picks a hotbar slot from 0 (number keys from 1).</summary>
    public abstract void Select(int slot);

    /// <summary>Moves the selection by <paramref name="delta"/> slots (mouse wheel).</summary>
    public abstract void Cycle(int delta);

    /// <summary>Empties the player's hand / deselects the hotbar, as when equipping a console's binding tool.</summary>
    public virtual void Deselect() { }

    /// <summary>Detaches any event listeners when switching to another mode.</summary>
    public virtual void Detach() { }

    /// <summary>The primary button went down while aiming at <paramref name="target"/> (null when aiming at nothing).</summary>
    public virtual void PrimaryPressed(BlockTarget? target) { }

    /// <summary>The primary button came up; in survival that places a held block or uses a held consumable, as in the
    /// original.</summary>
    public virtual void PrimaryReleased(Vector3 eye, Aim? aim) { }

    /// <summary>The secondary button went down. Creative places with it (ours); survival does nothing.</summary>
    public virtual void SecondaryPressed(Vector3 eye, Aim? aim)
    {
        if (UseSelected()) return;
        if (aim is { } a) Place(eye, a);
        else PlaceInSpace(eye);
    }

    /// <summary>The secondary button came up; in survival that empties the hand, as in the original.</summary>
    public virtual void SecondaryReleased() { }

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

    /// <summary>Places the selected block against the aimed solid cell, starting in the empty cell in front of the
    /// aimed face, in the aimed grid.</summary>
    public abstract void Place(Vector3 eye, Aim aim);

    /// <summary>Places the selected block in empty space when no block face is aimed at: attaches to the nearest grid
    /// within 16 units, or starts a new floating ship beyond 16 units.</summary>
    public virtual void PlaceInSpace(Vector3 eye) { }

    /// <summary>
    /// Where the selected block would go when aiming into empty space (no block face hit within reach), or null when no
    /// placeable block is selected.
    /// </summary>
    public SpacePlacementPlan? PlanSpacePlacement(Vector3 eye, Vector3 forward)
    {
        var block = SelectedBlock;
        if (block == BlockPalette.Air) return null;
        var shape = Palette.Shape(block);
        var size = shape?.Size ?? new Int3(1, 1, 1);
        float reach = ConsoleBinding.PlacementReach(size.ToVector3());
        var worldPos = eye + forward * reach;

        if (Grids.NearestAttachment(worldPos) is { } attach)
        {
            var grid = attach.Grid;
            var localPos = grid.ToLocal(worldPos);
            if (shape is null)
            {
                var cell = (Vector3I)localPos.Floor();
                var center = grid.ToGlobal((Vector3)cell + new Vector3(0.5f, 0.5f, 0.5f));
                var problem = PlacementProblem(eye, grid, cell);
                return new SpacePlacementPlan(block, center, grid, cell, null, Orientation, attach.LineTargetWorld, problem);
            }
            var rotSize = shape.RotatedSize(Orientation).ToGodot();
            var originCell = (Vector3I)(localPos - (Vector3)rotSize * 0.5f).Round();
            var origin = originCell.ToInt3();
            var box = shape.ModelBox(origin, Orientation);
            var objCenter = grid.ToGlobal(box.Center.ToGodot());
            var objProblem = grid.ObjectProblem(block, origin, Orientation)
                             ?? Grids.OverlapProblem(grid, shape.Footprint(origin, Orientation).Select(c => c.ToGodot()));
            if (objProblem is null)
            {
                var model = shape.ModelCells(origin, Orientation).Select(c => c.ToGodot()).ToHashSet();
                if (PlayerMotor.BodyOverlaps(grid, eye, model.Contains)) objProblem = "No room: you are in the way";
            }
            return new SpacePlacementPlan(block, objCenter, grid, originCell, shape, Orientation, attach.LineTargetWorld, objProblem);
        }

        long shipCells = 0;
        foreach (var existing in Grids.Ships) shipCells += (long)existing.GridSize * existing.GridSize * existing.GridSize;
        var fleetProblem = Launch.FleetProblem(Grids.Ships.Count, shipCells);
        return new SpacePlacementPlan(block, worldPos, null, Vector3I.Zero, shape, Orientation, null, fleetProblem);
    }

    /// <summary>Executes a space placement plan once <paramref name="pay"/> agrees.</summary>
    protected bool TryPlaceInSpace(Vector3 eye, Vector3 forward, ushort block, Func<bool> pay)
    {
        if (PlanSpacePlacement(eye, forward) is not { } plan || plan.Block != block) return false;
        if (plan.Problem is { } problem)
        {
            Hud.Flash(problem, warning: true);
            return false;
        }
        if (plan.TargetGrid is { } grid)
        {
            if (plan.Shape is null)
                return CanPlaceAt(eye, grid, plan.LocalOrigin) && pay() && grid.Set(plan.LocalOrigin, block, plan.Orientation);
            return pay() && grid.AddObject(block, plan.LocalOrigin.ToInt3(), plan.Orientation) is not null;
        }
        if (!pay()) return false;
        var (ship, createProblem) = Grids.CreateFloatingShip(block, plan.WorldCenter, plan.Orientation);
        if (createProblem is not null)
        {
            Hud.Flash(createProblem, warning: true);
            return false;
        }
        return ship is not null;
    }

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
            return CanPlaceAt(eye, aim.Grid, aim.Before) && pay() && aim.Grid.Set(aim.Before, block, Orientation);

        if (PlanObject(eye, aim) is not { } plan) return false;
        if (plan.Problem is { } problem)
        {
            Hud.Flash(problem, warning: true);
            return false;
        }
        return pay() && plan.Grid.AddObject(plan.Block, plan.Origin, plan.Orientation) is not null;
    }

    /// <summary>Why a grid block can't go into <paramref name="cell"/>, or null if it can; the checks of
    /// <see cref="CanPlaceAt"/> without a message, for the preview.</summary>
    public string? PlacementProblem(Vector3 eye, BlockGrid grid, Vector3I cell) =>
        grid.ReservedProblem(cell) ?? Grids.OverlapProblem(grid, [cell]) ??
        (PlayerMotor.BodyOccupies(grid, eye, cell) ? "No room: you are in the way" : null);

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
