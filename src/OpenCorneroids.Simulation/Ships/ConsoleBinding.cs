using System.Numerics;
using OpenCorneroids.Content;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// One of the 10 colour-coded trigger groups on a console block, as in the original.
/// </summary>
public readonly record struct ConsoleColorGroup(int Index, string Name, Rgb8 Color);

/// <summary>
/// One colour group on a specific console: its assigned key name (<c>"None"</c> when unbound) and the grid cells of
/// controllable blocks bound to it.
/// </summary>
public sealed class ConsoleKeyGroup
{
    readonly List<Int3> _targets = [];

    internal ConsoleKeyGroup(int index)
    {
        Index = index;
    }

    public int Index { get; }
    public ConsoleColorGroup ColorGroup => ConsoleBinding.ColorGroups[Index];
    public string Key { get; internal set; } = ConsoleBinding.DefaultKey;
    public IReadOnlyList<Int3> Targets => _targets;

    internal bool Contains(Int3 cell) => _targets.Contains(cell);

    internal void Add(Int3 cell)
    {
        if (!_targets.Contains(cell))
            _targets.Add(cell);
    }

    internal bool Remove(Int3 cell) => _targets.Remove(cell);
}

/// <summary>
/// Binding state for one console block at <see cref="ConsoleCell"/> on a grid.
/// </summary>
public sealed class ConsoleBindingState
{
    readonly ConsoleKeyGroup[] _groups;

    public ConsoleBindingState(Int3 consoleCell, int buttons, bool gunsOnly = false)
    {
        ConsoleCell = consoleCell;
        GroupCount = ConsoleBinding.ClampGroupCount(buttons);
        GunsOnly = gunsOnly;
        _groups = new ConsoleKeyGroup[GroupCount];
        for (int i = 0; i < GroupCount; i++)
            _groups[i] = new ConsoleKeyGroup(i);
    }

    public Int3 ConsoleCell { get; }
    public int GroupCount { get; }
    public bool GunsOnly { get; }
    public IReadOnlyList<ConsoleKeyGroup> Groups => _groups;

    /// <summary>Optional camera block cell bound to this console via "Set camera".</summary>
    public Int3? Camera { get; internal set; }

    /// <summary>Returns the group index (<c>0..GroupCount-1</c>) that <paramref name="target"/> belongs to, or −1.</summary>
    public int GroupOf(Int3 target)
    {
        for (int i = 0; i < _groups.Length; i++)
            if (_groups[i].Contains(target))
                return i;
        return -1;
    }

    public bool HasAnyBindings
    {
        get
        {
            if (Camera is not null) return true;
            foreach (var g in _groups)
                if (!string.Equals(g.Key, ConsoleBinding.DefaultKey, StringComparison.OrdinalIgnoreCase) || g.Targets.Count > 0)
                    return true;
            return false;
        }
    }

    internal ConsoleKeyGroup GroupAt(int index) => _groups[index];
}

/// <summary>
/// Console colour-group definitions, key bindings, camera bindings, gun aiming, and free-space placement geometry, as
/// in the original.
/// </summary>
public static class ConsoleBinding
{
    public const int MaxColorGroups = 10;
    public const string DefaultKey = "None";

    /// <summary>Maximum distance (units) at which the console/camera binding tool can highlight and bind a block, as in the original.</summary>
    public const float MaxBindDistance = 3.0f;

    /// <summary>Maximum distance (units) to the closest block of an existing object when placing a block in empty space, as in the original.</summary>
    public const float MaxAttachmentDistance = 16.0f;

    /// <summary>
    /// The 10 console colour groups in cycling order, as in the original.
    /// </summary>
    public static readonly IReadOnlyList<ConsoleColorGroup> ColorGroups =
    [
        new(0, "Red",       new Rgb8(255,   0,   0)),
        new(1, "Green",     new Rgb8(  0, 128,   0)),
        new(2, "LightBlue", new Rgb8(173, 216, 230)),
        new(3, "Yellow",    new Rgb8(255, 255,   0)),
        new(4, "Violet",    new Rgb8(238, 130, 238)),
        new(5, "Orange",    new Rgb8(255, 165,   0)),
        new(6, "Brown",     new Rgb8(165,  42,  42)),
        new(7, "Turquoise", new Rgb8( 64, 224, 208)),
        new(8, "Pink",      new Rgb8(255, 192, 203)),
        new(9, "DarkBlue",  new Rgb8(  0,   0, 139)),
    ];

    public static int ClampGroupCount(int buttons) => Math.Clamp(buttons, 0, MaxColorGroups);

    /// <summary>
    /// Empty-space placement distance from the player's eye for a block of size <paramref name="blockSize"/>, as in the
    /// original: <c>sqrt(0.25 * size.LengthSquared()) + 2.0</c>.
    /// </summary>
    public static float PlacementReach(Vector3 blockSize) =>
        MathF.Sqrt(0.25f * blockSize.LengthSquared()) + 2.0f;

    /// <summary>
    /// Resolves a cannon's firing direction when aimed by a gun console, as in the original: if <paramref name="aimForward"/>
    /// lies strictly in the cannon's forward hemisphere (<c>dot &gt; 0</c>), fires along <paramref name="aimForward"/>;
    /// otherwise fires straight along <paramref name="barrelForward"/>.
    /// </summary>
    public static Vector3 GunAimDirection(Vector3 barrelForward, Vector3 aimForward)
    {
        float barrelLen = barrelForward.Length();
        var normalBarrel = barrelLen > 1e-6f ? barrelForward / barrelLen : -Vector3.UnitZ;
        float aimLen = aimForward.Length();
        if (aimLen <= 1e-6f) return normalBarrel;
        var normalAim = aimForward / aimLen;
        return Vector3.Dot(normalBarrel, normalAim) > 0f ? normalAim : normalBarrel;
    }
}

/// <summary>
/// Tracks console colour-group bindings, key assignments, and bound cameras for all consoles on one grid.
/// </summary>
public sealed class ShipConsoleBindings
{
    readonly Dictionary<Int3, ConsoleBindingState> _consoles = [];

    public IReadOnlyDictionary<Int3, ConsoleBindingState> Consoles => _consoles;

    public ConsoleBindingState? TryGet(Int3 consoleCell) =>
        _consoles.TryGetValue(consoleCell, out var state) ? state : null;

    public ConsoleBindingState GetOrCreate(Int3 consoleCell, int buttons, bool gunsOnly = false)
    {
        int clamped = ConsoleBinding.ClampGroupCount(buttons);
        if (_consoles.TryGetValue(consoleCell, out var existing) && existing.GroupCount == clamped && existing.GunsOnly == gunsOnly)
            return existing;

        var created = new ConsoleBindingState(consoleCell, clamped, gunsOnly);
        if (existing is not null)
        {
            created.Camera = existing.Camera;
            int copy = Math.Min(existing.GroupCount, created.GroupCount);
            for (int i = 0; i < copy; i++)
            {
                created.GroupAt(i).Key = existing.Groups[i].Key;
                foreach (var t in existing.Groups[i].Targets)
                    created.GroupAt(i).Add(t);
            }
        }
        _consoles[consoleCell] = created;
        return created;
    }

    /// <summary>
    /// Cycles <paramref name="targetCell"/> through <paramref name="consoleCell"/>'s colour groups (0..GroupCount−1),
    /// unbinding it after the last colour group. Binding a block to a console unbinds it from any other console on the
    /// same grid, as in the original. Returns the new group index, or −1 when unbound or invalid.
    /// </summary>
    public int CycleTarget(Int3 consoleCell, int buttons, bool gunsOnly, Int3 targetCell, bool isThruster, bool isGun)
    {
        if (targetCell == consoleCell) return -1;
        if (gunsOnly ? !isGun : (!isThruster && !isGun)) return -1;

        var console = GetOrCreate(consoleCell, buttons, gunsOnly);
        if (console.GroupCount <= 0) return -1;

        int current = console.GroupOf(targetCell);

        // Unbind from any other console on the same grid.
        foreach (var (cell, other) in _consoles)
        {
            if (cell == consoleCell) continue;
            for (int i = 0; i < other.GroupCount; i++)
                other.GroupAt(i).Remove(targetCell);
        }

        if (current < 0)
        {
            console.GroupAt(0).Add(targetCell);
            return 0;
        }

        console.GroupAt(current).Remove(targetCell);
        int next = current + 1;
        if (next < console.GroupCount)
        {
            console.GroupAt(next).Add(targetCell);
            return next;
        }
        return -1;
    }

    /// <summary>
    /// Toggles binding <paramref name="cameraCell"/> to <paramref name="consoleCell"/>. Returns <c>true</c> if bound,
    /// <c>false</c> if unbound.
    /// </summary>
    public bool ToggleCamera(Int3 consoleCell, int buttons, bool gunsOnly, Int3 cameraCell)
    {
        if (cameraCell == consoleCell) return false;
        var console = GetOrCreate(consoleCell, buttons, gunsOnly);
        if (console.Camera == cameraCell)
        {
            console.Camera = null;
            return false;
        }
        console.Camera = cameraCell;
        return true;
    }

    /// <summary>
    /// Assigns <paramref name="key"/> to colour group <paramref name="groupIndex"/> on <paramref name="consoleCell"/>.
    /// Multiple groups on the same console may share the same key, as in the original.
    /// </summary>
    public void AssignKey(Int3 consoleCell, int buttons, bool gunsOnly, int groupIndex, string key)
    {
        var console = GetOrCreate(consoleCell, buttons, gunsOnly);
        if ((uint)groupIndex >= (uint)console.GroupCount) return;
        console.GroupAt(groupIndex).Key = string.IsNullOrWhiteSpace(key) ? ConsoleBinding.DefaultKey : key.Trim();
    }

    /// <summary>
    /// Returns all target block cells bound to groups on <paramref name="consoleCell"/> whose assigned key is currently
    /// in <paramref name="heldKeys"/>.
    /// </summary>
    public HashSet<Int3> ActiveTargets(Int3 consoleCell, IReadOnlySet<string> heldKeys)
    {
        var active = new HashSet<Int3>();
        if (!_consoles.TryGetValue(consoleCell, out var console) || heldKeys.Count == 0)
            return active;

        for (int i = 0; i < console.GroupCount; i++)
        {
            var group = console.Groups[i];
            if (string.Equals(group.Key, ConsoleBinding.DefaultKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (heldKeys.Contains(group.Key))
                foreach (var target in group.Targets)
                    active.Add(target);
        }
        return active;
    }

    /// <summary>
    /// Cleans up bindings when a block/object at <paramref name="cell"/> is removed from the grid.
    /// </summary>
    public void RemoveBlock(Int3 cell)
    {
        _consoles.Remove(cell);
        foreach (var console in _consoles.Values)
        {
            if (console.Camera == cell)
                console.Camera = null;
            for (int i = 0; i < console.GroupCount; i++)
                console.GroupAt(i).Remove(cell);
        }
    }

    /// <summary>
    /// Moves console bindings belonging to <paramref name="cells"/> into a new <see cref="ShipConsoleBindings"/> shifted
    /// by <c>-shift</c> (used when launching a structure from the static world into a ship).
    /// </summary>
    public ShipConsoleBindings ExtractAndShift(IReadOnlySet<Int3> cells, Int3 shift)
    {
        var extracted = new ShipConsoleBindings();
        var toRemove = new List<Int3>();
        foreach (var (cell, console) in _consoles)
        {
            if (!cells.Contains(cell)) continue;
            toRemove.Add(cell);
            var copy = extracted.GetOrCreate(cell - shift, console.GroupCount, console.GunsOnly);
            if (console.Camera is { } cam && cells.Contains(cam))
                copy.Camera = cam - shift;
            for (int i = 0; i < console.GroupCount; i++)
            {
                copy.GroupAt(i).Key = console.Groups[i].Key;
                foreach (var target in console.Groups[i].Targets)
                    if (cells.Contains(target))
                        copy.GroupAt(i).Add(target - shift);
            }
        }
        foreach (var cell in toRemove)
            _consoles.Remove(cell);
        return extracted;
    }

    public IReadOnlyList<SavedConsole> ToSnapshot()
    {
        var list = new List<SavedConsole>();
        foreach (var (_, console) in _consoles.OrderBy(kv => kv.Key.Z).ThenBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
        {
            if (!console.HasAnyBindings) continue;
            var groups = new List<SavedConsoleGroup>();
            for (int i = 0; i < console.GroupCount; i++)
            {
                var g = console.Groups[i];
                if (!string.Equals(g.Key, ConsoleBinding.DefaultKey, StringComparison.OrdinalIgnoreCase) || g.Targets.Count > 0)
                    groups.Add(new SavedConsoleGroup(i, g.Key, g.Targets.ToArray()));
            }
            list.Add(new SavedConsole(console.ConsoleCell, groups, console.Camera));
        }
        return list;
    }

    public void LoadSnapshot(IEnumerable<SavedConsole>? saved, Func<Int3, (int Buttons, bool GunsOnly)?> resolveConsole)
    {
        _consoles.Clear();
        if (saved is null) return;
        foreach (var entry in saved)
        {
            var spec = resolveConsole(entry.Cell);
            if (spec is null) continue;
            var console = GetOrCreate(entry.Cell, spec.Value.Buttons, spec.Value.GunsOnly);
            console.Camera = entry.Camera;
            foreach (var g in entry.Groups)
            {
                if ((uint)g.Index >= (uint)console.GroupCount) continue;
                console.GroupAt(g.Index).Key = string.IsNullOrWhiteSpace(g.Key) ? ConsoleBinding.DefaultKey : g.Key;
                foreach (var target in g.Targets)
                    console.GroupAt(g.Index).Add(target);
            }
        }
    }
}
