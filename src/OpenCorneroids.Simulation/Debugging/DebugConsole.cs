using System.Globalization;
using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Simulation.Debugging;

/// <summary>Summary of a floating ship in the world for <c>ships</c> and <c>tp ship</c>.</summary>
public readonly record struct DebugShipSummary(string Name, int BlockCount, float Mass, Vector3 Position, float Speed);

/// <summary>Result of running one or more console commands.</summary>
/// <param name="Lines">Output lines to print in the console log.</param>
/// <param name="IsError">True when any command reported an error or unknown syntax.</param>
/// <param name="ClearLog">True when <c>clear</c>/<c>cls</c> was run so prior log lines should be cleared first.</param>
public sealed record DebugConsoleResult(IReadOnlyList<string> Lines, bool IsError = false, bool ClearLog = false);

/// <summary>
/// Bridge implemented by the active world session so the Godot-free <see cref="DebugConsole"/> can inspect and
/// manipulate game state (mode, speed, teleportation, noclip, suit, inventory, ships, camera FOV, world saves).
/// </summary>
public interface IDebugConsoleHost
{
    ItemsetDef Itemset { get; }
    Inventory Inventory { get; }
    Suit? Suit { get; }
    bool Creative { get; }
    void SetCreative(bool creative);

    Vector3 Position { get; }
    Vector3 Velocity { get; }
    Vector3 SpawnPosition { get; }
    void Teleport(Vector3 position);

    float SpeedMultiplier { get; set; }
    bool Noclip { get; set; }
    bool FlightAssist { get; set; }
    bool FixedCamera { get; set; }
    float Fov { get; set; }

    IReadOnlyList<DebugShipSummary> Ships { get; }
    bool TryTeleportToShip(string? selector, out string message);
    string SpawnDemoShip();
    string LaunchTargetedOrNearestShip();
    int ClearShips();
    string ImportShip(string path);
    string ExportShip(string? selector, string path);
    string? SaveWorld();
}

/// <summary>
/// In-game developer/debug console command parser, history buffer, tab-completer and command executor.
/// Pure C# so every command and edge case is unit-tested outside Godot.
/// </summary>
public sealed class DebugConsole
{
    public const float MinSpeedMultiplier = 0.1f;
    public const float MaxSpeedMultiplier = 50f;
    public const float MinFov = 30f;
    public const float MaxFov = 130f;
    public const float DefaultFov = 75f;

    public sealed record CommandHelp(string Name, string Usage, string Summary, params string[] Aliases);

    public static readonly IReadOnlyList<CommandHelp> Catalog =
    [
        new("help", "help [command]", "List all commands or show detailed usage for one command.", "?"),
        new("mode", "mode [creative|survival|toggle]", "Switch between Creative and Survival mode (or toggle).", "gamemode", "gm", "creative", "survival"),
        new("speed", "speed [0.1..50|reset]", "Show or set the astronaut movement speed multiplier (default 1).", "flyspeed", "movespeed"),
        new("tp", "tp <x> <y> <z> | tp spawn | tp center | tp ship [name|#]", "Teleport to coordinates (supports ~ relative offsets), spawn, center, or a ship.", "teleport"),
        new("pos", "pos", "Show current position, velocity, mode, speed, noclip, and ship count.", "where", "coords", "status"),
        new("noclip", "noclip [on|off]", "Toggle or set flying through solid blocks.", "nc"),
        new("god", "god [on|off]", "Toggle or set unlimited suit health and power in survival.", "invincible"),
        new("give", "give <id|name> [count]", "Give items to the survival inventory by ID or name (e.g. give 51 1, give minigun, give 10:64).", "item"),
        new("items", "items [filter]", "List item IDs and names in the current itemset matching an optional filter.", "search"),
        new("kit", "kit", "Add the itemset's starting kit to the inventory."),
        new("clearinv", "clearinv", "Remove all items from the survival inventory.", "clearinventory"),
        new("heal", "heal", "Restore suit health and power to full."),
        new("health", "health <amount>", "Set suit health in survival.", "hp"),
        new("power", "power <amount>", "Set suit power in survival."),
        new("fov", "fov [30..130|reset]", "Show or set camera field of view in degrees (default 75)."),
        new("assist", "assist [on|off]", "Toggle or set the automatic ship Flight Assistant."),
        new("fixedcam", "fixedcam [on|off]", "Toggle or set fixed camera orientation when aboard a ship."),
        new("ships", "ships", "List active ships in the world with block count, mass, position and speed."),
        new("spawnship", "spawnship", "Build and launch a ready-to-fly demo ship near the player.", "demoship"),
        new("importship", "importship <path.sbs>", "Import and spawn a Spacebox structure (.sbs) into the world.", "sbsimport", "import"),
        new("exportship", "exportship [ship#|name] <path.sbs>", "Export a ship to a Spacebox structure file (.sbs).", "sbsexport", "export"),
        new("launch", "launch", "Launch the aimed structure (or nearest world console) as a ship."),
        new("clearships", "clearships", "Remove all floating ships from the world."),
        new("save", "save", "Save the world immediately."),
        new("clear", "clear", "Clear the debug console output log.", "cls"),
    ];

    readonly List<string> _history = [];
    int _historyIndex = -1;
    string _savedDraft = "";

    public IReadOnlyList<string> History => _history;

    /// <summary>Executes one command line (or semicolon-separated batch of commands) against <paramref name="host"/>.</summary>
    public DebugConsoleResult Execute(string rawLine, IDebugConsoleHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (string.IsNullOrWhiteSpace(rawLine))
            return new DebugConsoleResult([]);

        var trimmed = rawLine.Trim();
        if (_history.Count == 0 || !string.Equals(_history[^1], trimmed, StringComparison.Ordinal))
            _history.Add(trimmed);
        _historyIndex = -1;
        _savedDraft = "";

        var lines = new List<string>();
        bool anyError = false;
        bool clearLog = false;

        foreach (var segment in SplitStatements(trimmed))
        {
            var tokens = Tokenize(segment);
            if (tokens.Count == 0) continue;
            var (outLines, isError, clear) = ExecuteSingle(tokens, host);
            if (clear)
            {
                clearLog = true;
                lines.Clear();
            }
            lines.AddRange(outLines);
            anyError |= isError;
        }

        return new DebugConsoleResult(lines, anyError, clearLog);
    }

    /// <summary>Steps backward through command history (Up arrow).</summary>
    public string HistoryUp(string currentInput)
    {
        if (_history.Count == 0) return currentInput;
        if (_historyIndex < 0)
        {
            _savedDraft = currentInput;
            _historyIndex = _history.Count - 1;
        }
        else if (_historyIndex > 0)
        {
            _historyIndex--;
        }
        return _history[_historyIndex];
    }

    /// <summary>Steps forward through command history (Down arrow).</summary>
    public string HistoryDown(string currentInput)
    {
        if (_historyIndex < 0) return currentInput;
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            return _history[_historyIndex];
        }
        _historyIndex = -1;
        return _savedDraft;
    }

    /// <summary>Completes a partial command name (Tab key).</summary>
    public string Autocomplete(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "help ";
        var trimmed = input.TrimStart();
        if (trimmed.Contains(' ')) return input;

        var matches = Catalog
            .SelectMany(c => new[] { c.Name }.Concat(c.Aliases))
            .Where(name => name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.Length)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count > 0 ? matches[0] + " " : input;
    }

    static (IReadOnlyList<string> Lines, bool IsError, bool Clear) ExecuteSingle(List<string> tokens, IDebugConsoleHost host)
    {
        var cmd = tokens[0].ToLowerInvariant();
        var args = tokens.Skip(1).ToList();

        switch (cmd)
        {
            case "help" or "?":
                return RunHelp(args);

            case "clear" or "cls":
                return ([], false, true);

            case "creative":
                return SetMode(host, true);

            case "survival":
                return SetMode(host, false);

            case "mode" or "gamemode" or "gm":
                return RunMode(args, host);

            case "speed" or "flyspeed" or "movespeed":
                return RunSpeed(args, host);

            case "tp" or "teleport":
                return RunTeleport(args, host);

            case "pos" or "where" or "coords" or "status":
                return RunPos(host);

            case "noclip" or "nc":
                return RunToggle("Noclip", args, () => host.Noclip, v => host.Noclip = v);

            case "god" or "invincible":
                return RunGod(args, host);

            case "give" or "item":
                return RunGive(args, host);

            case "items" or "search":
                return RunItems(args, host);

            case "kit":
                return RunKit(host);

            case "clearinv" or "clearinventory":
                return RunClearInventory(host);

            case "heal":
                return RunHeal(host);

            case "health" or "hp":
                return RunSetSuitStat("Health", args, host, (suit, val) => suit.SetHealth(val), suit => (suit.Health, suit.MaxHealth));

            case "power":
                return RunSetSuitStat("Power", args, host, (suit, val) => suit.SetPowerLevel(val), suit => (suit.Power, suit.MaxPower));

            case "fov":
                return RunFov(args, host);

            case "assist":
                return RunToggle("Flight Assistant", args, () => host.FlightAssist, v => host.FlightAssist = v);

            case "fixedcam":
                return RunToggle("Fixed camera when aboard", args, () => host.FixedCamera, v => host.FixedCamera = v);

            case "ships":
                return RunShips(host);

            case "spawnship" or "demoship":
                return ([host.SpawnDemoShip()], false, false);

            case "importship" or "sbsimport" or "import":
                return RunImportShip(args, host);

            case "exportship" or "sbsexport" or "export":
                return RunExportShip(args, host);

            case "launch":
                return ([host.LaunchTargetedOrNearestShip()], false, false);

            case "clearships":
            {
                int removed = host.ClearShips();
                return ([removed == 0 ? "No ships in the world." : $"Removed {removed} ship(s)."], false, false);
            }

            case "save":
            {
                var error = host.SaveWorld();
                return error is null
                    ? (["World saved."], false, false)
                    : ([ $"Save failed: {error}"], true, false);
            }

            default:
                return ([ $"Unknown command '{tokens[0]}'. Type 'help' for a list of commands."], true, false);
        }
    }

    static (IReadOnlyList<string>, bool, bool) RunHelp(List<string> args)
    {
        if (args.Count > 0)
        {
            var query = args[0].ToLowerInvariant();
            var match = Catalog.FirstOrDefault(c =>
                c.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                c.Aliases.Any(a => a.Equals(query, StringComparison.OrdinalIgnoreCase)));
            if (match is null)
                return ([ $"Unknown command '{args[0]}'. Type 'help' for all commands."], true, false);
            var aliases = match.Aliases.Length > 0 ? $" (aliases: {string.Join(", ", match.Aliases)})" : "";
            return ([ $"{match.Usage}{aliases}", $"  {match.Summary}"], false, false);
        }

        var output = new List<string>(Catalog.Count + 1)
        {
            "Available commands (type 'help <command>' for details):"
        };
        foreach (var entry in Catalog)
            output.Add($"  {entry.Usage,-46} - {entry.Summary}");
        return (output, false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunMode(List<string> args, IDebugConsoleHost host)
    {
        if (args.Count == 0)
        {
            string current = host.Creative ? "creative" : "survival";
            return ([ $"Current mode: {current}. Usage: mode <creative|survival|toggle>"], false, false);
        }

        var arg = args[0].ToLowerInvariant();
        return arg switch
        {
            "creative" or "c" or "1" => SetMode(host, true),
            "survival" or "s" or "0" => SetMode(host, false),
            "toggle" or "t" => SetMode(host, !host.Creative),
            _ => ([ $"Unknown mode '{args[0]}'. Expected: creative, survival, or toggle."], true, false)
        };
    }

    static (IReadOnlyList<string>, bool, bool) SetMode(IDebugConsoleHost host, bool creative)
    {
        host.SetCreative(creative);
        return ([ $"Player mode set to {(creative ? "Creative" : "Survival")}."], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunSpeed(List<string> args, IDebugConsoleHost host)
    {
        if (args.Count == 0)
        {
            return ([FormattableString.Invariant(
                $"Speed multiplier: {host.SpeedMultiplier:0.##}x (cruise {4f * host.SpeedMultiplier:0.#} m/s, boost {8f * host.SpeedMultiplier:0.#} m/s)")], false, false);
        }

        if (args[0].Equals("reset", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            host.SpeedMultiplier = 1f;
            return (["Speed multiplier reset to 1x (4 m/s cruise, 8 m/s boost)."], false, false);
        }

        if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float mult) || !float.IsFinite(mult))
            return ([ $"Invalid speed '{args[0]}'. Usage: speed <0.1..50|reset>"], true, false);

        float clamped = Math.Clamp(mult, MinSpeedMultiplier, MaxSpeedMultiplier);
        host.SpeedMultiplier = clamped;
        return ([FormattableString.Invariant(
            $"Speed multiplier set to {clamped:0.##}x (cruise {4f * clamped:0.#} m/s, boost {8f * clamped:0.#} m/s).")], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunTeleport(List<string> args, IDebugConsoleHost host)
    {
        if (args.Count == 0)
            return (["Usage: tp <x> <y> <z> | tp spawn | tp center | tp ship [name|#]"], true, false);

        var first = args[0].ToLowerInvariant();
        if (first == "spawn")
        {
            var sp = host.SpawnPosition;
            host.Teleport(sp);
            return ([FormattableString.Invariant($"Teleported to spawn ({sp.X:0.#}, {sp.Y:0.#}, {sp.Z:0.#}).")], false, false);
        }
        if (first is "center" or "asteroid")
        {
            var center = new Vector3(64f, 74f, 110f);
            host.Teleport(center);
            return ([FormattableString.Invariant($"Teleported near world center ({center.X:0.#}, {center.Y:0.#}, {center.Z:0.#}).")], false, false);
        }
        if (first == "ship")
        {
            string? selector = args.Count > 1 ? string.Join(' ', args.Skip(1)) : null;
            bool ok = host.TryTeleportToShip(selector, out var msg);
            return ([msg], !ok, false);
        }

        if (args.Count < 3)
            return (["Usage: tp <x> <y> <z> (use ~ for relative coordinates, e.g. tp ~ ~10 ~-5)"], true, false);

        var cur = host.Position;
        if (!TryParseCoord(args[0], cur.X, out float x) ||
            !TryParseCoord(args[1], cur.Y, out float y) ||
            !TryParseCoord(args[2], cur.Z, out float z))
        {
            return ([ $"Invalid coordinates '{args[0]} {args[1]} {args[2]}'. Expected numbers or ~offsets."], true, false);
        }

        var target = new Vector3(x, y, z);
        host.Teleport(target);
        return ([FormattableString.Invariant($"Teleported to ({target.X:0.##}, {target.Y:0.##}, {target.Z:0.##}).")], false, false);
    }

    static bool TryParseCoord(string token, float current, out float result)
    {
        if (token.StartsWith('~'))
        {
            if (token.Length == 1)
            {
                result = current;
                return true;
            }
            if (float.TryParse(token[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out float offset) && float.IsFinite(offset))
            {
                result = current + offset;
                return true;
            }
            result = 0f;
            return false;
        }
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && float.IsFinite(result);
    }

    static (IReadOnlyList<string>, bool, bool) RunPos(IDebugConsoleHost host)
    {
        var p = host.Position;
        var v = host.Velocity;
        var mode = host.Creative ? "Creative" : "Survival";
        var noclip = host.Noclip ? "ON" : "OFF";
        var assist = host.FlightAssist ? "ON" : "OFF";
        return ([
            FormattableString.Invariant($"Position: ({p.X:0.##}, {p.Y:0.##}, {p.Z:0.##})  Speed: {v.Length():0.##} m/s ({host.SpeedMultiplier:0.##}x)"),
            FormattableString.Invariant($"Mode: {mode}  Noclip: {noclip}  FlightAssist: {assist}  Ships: {host.Ships.Count}")
        ], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunToggle(string label, List<string> args, Func<bool> get, Action<bool> set)
    {
        bool next;
        if (args.Count == 0 || args[0].Equals("toggle", StringComparison.OrdinalIgnoreCase))
        {
            next = !get();
        }
        else if (TryParseBool(args[0], out bool parsed))
        {
            next = parsed;
        }
        else
        {
            return ([ $"Usage: {label.ToLowerInvariant()} [on|off]"], true, false);
        }

        set(next);
        return ([ $"{label}: {(next ? "ON" : "OFF")}."], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunGod(List<string> args, IDebugConsoleHost host)
    {
        if (host.Suit is not { } suit)
            return (["God mode applies to Survival mode (switch with 'mode survival' first)."], true, false);

        bool next;
        if (args.Count == 0 || args[0].Equals("toggle", StringComparison.OrdinalIgnoreCase))
            next = !suit.GodMode;
        else if (TryParseBool(args[0], out bool parsed))
            next = parsed;
        else
            return (["Usage: god [on|off]"], true, false);

        suit.GodMode = next;
        if (next) suit.RestoreFull();
        return ([ $"God mode (unlimited health & suit power): {(next ? "ON" : "OFF")}."], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunGive(List<string> args, IDebugConsoleHost host)
    {
        if (args.Count == 0)
            return (["Usage: give <itemId|name> [count] (e.g. give 51 1, give minigun, give 10:64)"], true, false);

        int count = 1;
        string itemQuery = args[0];

        if (args.Count == 1 && itemQuery.Contains(':'))
        {
            var parts = itemQuery.Split(':', 2);
            itemQuery = parts[0];
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count <= 0)
                return ([ $"Invalid count '{parts[1]}'. Expected a positive integer."], true, false);
        }
        else if (args.Count >= 2)
        {
            if (int.TryParse(args[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCount) && parsedCount > 0)
            {
                count = parsedCount;
                itemQuery = string.Join(' ', args.Take(args.Count - 1));
            }
            else
            {
                itemQuery = string.Join(' ', args);
            }
        }

        var item = FindItem(host.Itemset, itemQuery);
        if (item is null)
            return ([ $"Item '{itemQuery}' not found. Use 'items <text>' to search item IDs and names."], true, false);

        int leftOver = host.Inventory.Add(item.ItemId, count);
        int added = count - leftOver;
        if (added <= 0)
            return ([ $"Inventory is full; could not add {item.Name}."], true, false);

        string suffix = host.Creative ? " (switch to 'mode survival' to use inventory)" : "";
        return leftOver > 0
            ? ([ $"Added {added}x {item.Name} (#{item.ItemId}); {leftOver} did not fit.{suffix}"], false, false)
            : ([ $"Added {added}x {item.Name} (#{item.ItemId}).{suffix}"], false, false);
    }

    static ItemDef? FindItem(ItemsetDef itemset, string query)
    {
        if (int.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) &&
            itemset.ItemsById.TryGetValue(id, out var byId))
            return byId;

        var normalized = query.Replace('_', ' ').Trim();
        return itemset.Items.FirstOrDefault(i => i.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? itemset.Items.FirstOrDefault(i => i.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            ?? itemset.Items.FirstOrDefault(i => i.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }

    static (IReadOnlyList<string>, bool, bool) RunItems(List<string> args, IDebugConsoleHost host)
    {
        string filter = args.Count > 0 ? string.Join(' ', args).Replace('_', ' ').Trim() : "";
        var matches = host.Itemset.Items
            .Where(i => string.IsNullOrEmpty(filter) ||
                        i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        i.ItemId.ToString(CultureInfo.InvariantCulture) == filter)
            .OrderBy(i => i.ItemId)
            .ToList();

        if (matches.Count == 0)
            return ([ $"No items matching '{filter}'."], false, false);

        const int maxLines = 25;
        var lines = new List<string> { $"Items matching '{(string.IsNullOrEmpty(filter) ? "*" : filter)}' ({matches.Count} total):" };
        foreach (var item in matches.Take(maxLines))
            lines.Add($"  #{item.ItemId,-4} {item.Name}");
        if (matches.Count > maxLines)
            lines.Add($"  ... and {matches.Count - maxLines} more (narrow with 'items <name>').");
        return (lines, false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunKit(IDebugConsoleHost host)
    {
        int leftOver = StartingKit.Give(host.Inventory, host.Itemset, Crafting.MainInventory);
        return leftOver == 0
            ? (["Starting kit added to inventory."], false, false)
            : ([ $"Starting kit added ({leftOver} item(s) did not fit)."], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunClearInventory(IDebugConsoleHost host)
    {
        for (int i = 0; i < host.Inventory.Slots.Count; i++)
            host.Inventory.SetSlot(i, null);
        return (["Inventory cleared."], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunHeal(IDebugConsoleHost host)
    {
        if (host.Suit is not { } suit)
            return (["Suit health/power only exist in Survival mode (switch with 'mode survival')."], true, false);
        suit.RestoreFull();
        return ([ $"Restored Health ({suit.MaxHealth}/{suit.MaxHealth}) and Power ({suit.MaxPower}/{suit.MaxPower})."], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunSetSuitStat(
        string name,
        List<string> args,
        IDebugConsoleHost host,
        Action<Suit, double> setter,
        Func<Suit, (double Current, int Max)> getter)
    {
        if (host.Suit is not { } suit)
            return ([ $"{name} only exists in Survival mode (switch with 'mode survival')."], true, false);

        if (args.Count == 0)
        {
            var (cur, max) = getter(suit);
            return ([FormattableString.Invariant($"{name}: {cur:0.#} / {max}")], false, false);
        }

        if (!double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double val) || !double.IsFinite(val))
            return ([ $"Invalid value '{args[0]}'. Usage: {name.ToLowerInvariant()} <amount>"], true, false);

        setter(suit, val);
        var (updated, updatedMax) = getter(suit);
        return ([FormattableString.Invariant($"{name} set to {updated:0.#} / {updatedMax}.")], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunFov(List<string> args, IDebugConsoleHost host)
    {
        if (args.Count == 0)
            return ([FormattableString.Invariant($"Camera FOV: {host.Fov:0.#}° (default {DefaultFov:0}°)")], false, false);

        if (args[0].Equals("reset", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            host.Fov = DefaultFov;
            return ([FormattableString.Invariant($"Camera FOV reset to {DefaultFov:0}°.")], false, false);
        }

        if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float fov) || !float.IsFinite(fov))
            return ([ $"Invalid FOV '{args[0]}'. Usage: fov <30..130|reset>"], true, false);

        float clamped = Math.Clamp(fov, MinFov, MaxFov);
        host.Fov = clamped;
        return ([FormattableString.Invariant($"Camera FOV set to {clamped:0.#}°.")], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunShips(IDebugConsoleHost host)
    {
        var ships = host.Ships;
        if (ships.Count == 0)
            return (["No active ships in the world. Use 'spawnship' to build and launch one."], false, false);

        var lines = new List<string>(ships.Count + 1) { $"Active ships ({ships.Count}):" };
        for (int i = 0; i < ships.Count; i++)
        {
            var s = ships[i];
            lines.Add(FormattableString.Invariant(
                $"  #{i + 1} {s.Name}: {s.BlockCount} blocks, mass {s.Mass:0.#}, pos ({s.Position.X:0.#}, {s.Position.Y:0.#}, {s.Position.Z:0.#}), speed {s.Speed:0.##} m/s"));
        }
        return (lines, false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunImportShip(List<string> args, IDebugConsoleHost host)
    {
        if (args.Count == 0)
            return (["Usage: importship <path.sbs>"], true, false);

        string path = string.Join(' ', args);
        var message = host.ImportShip(path);
        bool isError = message.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
                       message.StartsWith("File not found", StringComparison.OrdinalIgnoreCase) ||
                       message.StartsWith("Could not", StringComparison.OrdinalIgnoreCase) ||
                       message.StartsWith("Failed", StringComparison.OrdinalIgnoreCase);
        return ([message], isError, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunExportShip(List<string> args, IDebugConsoleHost host)
    {
        if (args.Count == 0)
            return (["Usage: exportship [ship#|name] <path.sbs>"], true, false);

        string? selector = null;
        string path;
        if (args.Count == 1)
        {
            path = args[0];
        }
        else
        {
            if (args[0].StartsWith('#') || int.TryParse(args[0], CultureInfo.InvariantCulture, out _))
            {
                selector = args[0];
                path = string.Join(' ', args.Skip(1));
            }
            else
            {
                selector = args[0];
                path = string.Join(' ', args.Skip(1));
            }
        }

        var message = host.ExportShip(selector, path);
        bool isError = message.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
                       message.StartsWith("No ship", StringComparison.OrdinalIgnoreCase) ||
                       message.StartsWith("Could not", StringComparison.OrdinalIgnoreCase) ||
                       message.StartsWith("Failed", StringComparison.OrdinalIgnoreCase);
        return ([message], isError, false);
    }

    static bool TryParseBool(string token, out bool value)
    {
        switch (token.ToLowerInvariant())
        {
            case "on" or "true" or "1" or "yes" or "enable":
                value = true;
                return true;
            case "off" or "false" or "0" or "no" or "disable":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    static IEnumerable<string> SplitStatements(string line)
    {
        int start = 0;
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (line[i] == ';' && !inQuotes)
            {
                yield return line[start..i];
                start = i + 1;
            }
        }
        if (start <= line.Length) yield return line[start..];
    }

    static List<string> Tokenize(string statement)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < statement.Length)
        {
            while (i < statement.Length && char.IsWhiteSpace(statement[i])) i++;
            if (i >= statement.Length) break;

            if (statement[i] == '"')
            {
                i++;
                int start = i;
                while (i < statement.Length && statement[i] != '"') i++;
                tokens.Add(statement[start..i]);
                if (i < statement.Length) i++;
            }
            else
            {
                int start = i;
                while (i < statement.Length && !char.IsWhiteSpace(statement[i])) i++;
                tokens.Add(statement[start..i]);
            }
        }
        return tokens;
    }
}
