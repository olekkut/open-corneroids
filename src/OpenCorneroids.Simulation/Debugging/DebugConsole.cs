using System.Globalization;
using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Settings;

namespace OpenCorneroids.Simulation.Debugging;

/// <summary>Summary of a floating ship in the world for <c>ships</c> and <c>tp ship</c>.</summary>
public readonly record struct DebugShipSummary(string Name, int BlockCount, float Mass, Vector3 Position, float Speed);

/// <summary>Result of running one or more console commands.</summary>
/// <param name="Lines">Output lines to print in the console log.</param>
/// <param name="IsError">True when any command reported an error or unknown syntax.</param>
/// <param name="ClearLog">True when <c>clear</c>/<c>cls</c> was run so prior log lines should be cleared first.</param>
public sealed record DebugConsoleResult(IReadOnlyList<string> Lines, bool IsError = false, bool ClearLog = false)
{
    /// <summary>For each of <see cref="Lines"/>, whether it reports an error, so one failed command in a batch does not
    /// colour the rest. Empty means every line takes <see cref="IsError"/>.</summary>
    public IReadOnlyList<bool> LineErrors { get; init; } = [];

    public bool IsErrorLine(int index) => index < LineErrors.Count ? LineErrors[index] : IsError;
}

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

    bool Noclip { get; set; }

    /// <summary>Respawns a dead player as the death window's button does; false when the player is not dead.</summary>
    bool Respawn();

    /// <summary>Moves the player to <paramref name="stopShort"/> metres before the block they aim at (up to 256 m away).</summary>
    bool TeleportToAim(float stopShort, out string message);

    /// <summary><c>bind &lt;key&gt; &lt;action&gt;</c>: binds a key as the Controls window does; null, or what was wrong.</summary>
    string? Bind(string arguments);

    /// <summary>The Controls window's rows as "action = key" lines.</summary>
    IReadOnlyList<string> Bindings { get; }

    /// <summary>Creatures alive in the world.</summary>
    int CreatureCount { get; }

    /// <summary>Removes every creature; returns how many there were.</summary>
    int ClearCreatures();

    /// <summary>Spawns a creature where the player aims; returns what happened.</summary>
    string SpawnCreature(Creatures.CreatureKind kind);

    /// <summary>The settings of this session: world rules and player preferences.</summary>
    CvarStore Cvars { get; }

    /// <summary>Reads a config file for <c>exec</c>; <paramref name="name"/> is as typed (".cfg" may be left out).</summary>
    bool TryReadConfig(string name, out string text, out string error);

    /// <summary>Writes the player's preferences to their config file now; null, or what went wrong.</summary>
    string? WriteUserConfig();

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
    /// <summary>How deep <c>exec</c> may nest config files inside config files.</summary>
    public const int MaxExecDepth = 8;

    public sealed record CommandHelp(string Name, string Usage, string Summary, params string[] Aliases);

    /// <summary>A named set of settings: a few console lines, run like a config file.</summary>
    public sealed record Preset(string Name, string Summary, string Commands);

    public static readonly IReadOnlyList<Preset> Presets =
    [
        new("original", "The original game's rules and ship controls (the default).",
            "reset rules; cl_flight_assist 0; cl_fixed_camera 0"),
        new("assisted", "Ours: the automatic flight assistant and a level camera aboard ships; rules unchanged.",
            "cl_flight_assist 1; cl_fixed_camera 1"),
    ];

    /// <summary>Commands that only work while <c>sv_cheats</c> is 1.</summary>
    static readonly HashSet<string> CheatCommands =
    [
        "noclip", "nc", "god", "invincible", "give", "item", "kit", "heal", "health", "hp", "power", "tp", "teleport",
        "spawn", "summon",
    ];

    public static readonly IReadOnlyList<CommandHelp> Catalog =
    [
        new("help", "help [command|setting]", "List all commands or show detailed usage for one command or setting.", "?"),
        new("<setting>", "<setting> [value]", "Show a setting (with its default, range and meaning) or set it, e.g. 'sv_ricochet 1'."),
        new("find", "find <text>", "List settings whose name or description contains the text.", "search"),
        new("cvarlist", "cvarlist [prefix]", "List every setting and its value (e.g. 'cvarlist sv_ship').", "cvars"),
        new("differences", "differences", "List the settings that differ from the original preset.", "diff"),
        new("toggle", "toggle <setting>", "Flip an on/off setting."),
        new("reset", "reset <setting|rules|prefs|all>", "Set one setting, all world rules (sv_), all preferences (cl_) or everything back to default."),
        new("preset", "preset [name]", "List presets or apply one ('preset original' restores the original game)."),
        new("exec", "exec <file[.cfg]>", "Run the commands in a config file from the game's config folder."),
        new("writeconfig", "writeconfig", "Save your preferences (cl_ settings) to config.cfg now.", "host_writeconfig"),
        new("bind", "bind [<key> <action>]", "List the key bindings, or bind a key to an action, e.g. bind Q interact (saved in config.cfg)."),
        new("mode", "mode [creative|survival|toggle]", "Switch between Creative and Survival mode (or toggle).", "gamemode", "gm", "creative", "survival"),
        new("speed", "speed [0.1..50|reset]", "Show or set the astronaut speed multiplier, sv_player_speed_scale (cheat).", "flyspeed", "movespeed"),
        new("tp", "tp <x> <y> <z> | tp spawn | tp center | tp aim [m] | tp ship [name|#]", "Teleport to coordinates (supports ~ relative offsets), spawn, center, just short of the aimed block, or a ship (cheat).", "teleport"),
        new("pos", "pos", "Show current position, velocity, mode, speed, noclip, and ship count.", "where", "coords", "status"),
        new("noclip", "noclip [on|off]", "Toggle or set flying through solid blocks (cheat).", "nc"),
        new("god", "god [on|off]", "Toggle or set unlimited suit health and power in survival (cheat).", "invincible"),
        new("give", "give <id|name> [count]", "Give items by ID or name, e.g. give 51 1, give minigun, give 10:64 (cheat).", "item"),
        new("items", "items [filter]", "List item IDs and names in the current itemset matching an optional filter."),
        new("kit", "kit", "Add the itemset's starting kit to the inventory (cheat)."),
        new("clearinv", "clearinv", "Remove all items from the survival inventory.", "clearinventory"),
        new("heal", "heal", "Restore suit health and power to full (cheat)."),
        new("kill", "kill", "Die at once (survival), as Source's kill does; respawn from the death window.", "suicide"),
        new("respawn", "respawn", "Respawn after dying, as the death window's button does."),
        new("creatures", "creatures [clear]", "Count the creatures in the world, or remove them all."),
        new("spawn", "spawn <parasite|egg|needle|scavenger>", "Spawn a creature on the aimed asteroid, or ahead for flyers (cheat).", "summon"),
        new("health", "health <amount>", "Set suit health in survival (cheat).", "hp"),
        new("power", "power <amount>", "Set suit power in survival (cheat)."),
        new("fov", "fov [45..135|reset]", "Show or set the field of view, cl_fov (default 90)."),
        new("assist", "assist [on|off]", "Toggle or set the automatic flight assistant, cl_flight_assist."),
        new("fixedcam", "fixedcam [on|off]", "Toggle or set the level camera aboard ships, cl_fixed_camera."),
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

        var output = new Output();
        var (anyError, clearLog) = RunLine(trimmed, host, 0, output);
        return new DebugConsoleResult(output.Lines, anyError, clearLog) { LineErrors = output.Errors };
    }

    /// <summary>Output lines, each marked with whether it reports an error.</summary>
    sealed class Output
    {
        public List<string> Lines { get; } = [];
        public List<bool> Errors { get; } = [];

        public void Add(IEnumerable<string> lines, bool error)
        {
            foreach (var line in lines)
            {
                Lines.Add(line);
                Errors.Add(error);
            }
        }

        public void Add(string line, bool error = false) => Add([line], error);

        public void Clear()
        {
            Lines.Clear();
            Errors.Clear();
        }
    }

    /// <summary>Runs one line of statements into <paramref name="output"/>; <paramref name="depth"/> counts nested execs.</summary>
    static (bool Error, bool Clear) RunLine(string line, IDebugConsoleHost host, int depth, Output output)
    {
        bool anyError = false, clearLog = false;
        foreach (var segment in SplitStatements(line))
        {
            var tokens = Tokenize(segment);
            if (tokens.Count == 0) continue;
            bool cheatsBefore = host.Cvars.CheatsEnabled;
            var cmd = tokens[0].ToLowerInvariant();
            var args = tokens.Skip(1).ToList();
            // exec and preset run further lines, whose output keeps its own error marks.
            if (cmd == "exec" && host.Cvars.Find(cmd) is null)
                anyError |= RunExec(args, host, depth, output);
            else if (cmd == "preset" && host.Cvars.Find(cmd) is null)
                anyError |= RunPreset(args, host, depth, output);
            else
            {
                var (outLines, isError, clear) = ExecuteSingle(tokens, host);
                if (clear)
                {
                    clearLog = true;
                    output.Clear();
                }
                output.Add(outLines, isError);
                anyError |= isError;
            }
            if (cheatsBefore && !host.Cvars.CheatsEnabled) output.Add(UndoCheats(host), false);
        }
        return (anyError, clearLog);
    }

    /// <summary>With sv_cheats back at 0, noclip and god mode end too; cheat settings were already reset by the store.</summary>
    static IEnumerable<string> UndoCheats(IDebugConsoleHost host)
    {
        if (host.Noclip)
        {
            host.Noclip = false;
            yield return "Cheats off: noclip ended.";
        }
        if (host.Suit is { GodMode: true } suit)
        {
            suit.GodMode = false;
            yield return "Cheats off: god mode ended.";
        }
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

    /// <summary>
    /// Completes a partial command or setting name (Tab key). With several matches it completes as far as they agree
    /// (so "sv_ship_" stays open for the player to choose), and a full name gets a trailing space.
    /// </summary>
    public string Autocomplete(string input, CvarStore? cvars = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return "help ";
        var trimmed = input.TrimStart();
        if (trimmed.Contains(' ')) return input;

        var matches = Catalog
            .SelectMany(c => new[] { c.Name }.Concat(c.Aliases))
            .Where(name => !name.StartsWith('<'))
            .Concat(cvars?.All.Select(c => c.Name) ?? [])
            .Where(name => name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name.Length)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0) return input;
        // A command name that is itself complete wins (as before), unless only settings match.
        var prefix = matches.Aggregate((a, b) => CommonPrefix(a, b));
        if (matches.Count == 1 || prefix.Length <= trimmed.Length)
            return matches[0] + " ";
        return prefix;
    }

    static string CommonPrefix(string a, string b)
    {
        int n = 0;
        while (n < a.Length && n < b.Length && char.ToLowerInvariant(a[n]) == char.ToLowerInvariant(b[n])) n++;
        return a[..n];
    }

    static (IReadOnlyList<string> Lines, bool IsError, bool Clear) ExecuteSingle(List<string> tokens, IDebugConsoleHost host)
    {
        var cmd = tokens[0].ToLowerInvariant();
        var args = tokens.Skip(1).ToList();

        if (CheatCommands.Contains(cmd) && !host.Cvars.CheatsEnabled)
            return ([ $"'{tokens[0]}' is a cheat; set sv_cheats 1 first."], true, false);

        if (host.Cvars.Find(cmd) is { } cvar)
            return RunCvar(cvar, args, host.Cvars);

        switch (cmd)
        {
            case "help" or "?":
                return RunHelp(args, host.Cvars);

            case "find" or "search":
                return RunFind(args, host.Cvars);

            case "cvarlist" or "cvars":
                return RunCvarList(args, host.Cvars);

            case "differences" or "diff":
                return RunDifferences(host.Cvars);

            case "toggle":
                return RunToggleCvar(args, host.Cvars);

            case "reset":
                return RunReset(args, host.Cvars);

            case "bind":
            {
                if (args.Count == 0) return (["Key bindings:", .. host.Bindings.Select(b => "  " + b)], false, false);
                var error = host.Bind(string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
                return error is null ? ([ $"Bound {args[0]} to {string.Join(' ', args.Skip(1))}."], false, false) : ([ $"bind: {error}"], true, false);
            }

            case "writeconfig" or "host_writeconfig":
            {
                var error = host.WriteUserConfig();
                return error is null ? (["Preferences saved to config.cfg."], false, false) : ([ $"Could not save preferences: {error}"], true, false);
            }

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

            case "items":
                return RunItems(args, host);

            case "kit":
                return RunKit(host);

            case "clearinv" or "clearinventory":
                return RunClearInventory(host);

            case "heal":
                return RunHeal(host);

            case "kill" or "suicide":
                if (host.Suit is not { } victim)
                    return (["Only a survival player can die (switch with 'mode survival')."], true, false);
                if (victim.IsDead) return (["Already dead."], false, false);
                victim.Die();
                return (["You died."], false, false);

            case "respawn":
                return host.Respawn() ? (["Respawned."], false, false) : (["You are not dead."], true, false);

            case "creatures":
                if (args.Count > 0 && args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
                    return ([ $"Removed {host.ClearCreatures()} creature(s)."], false, false);
                return ([ $"{host.CreatureCount} creature(s) in the world."], false, false);

            case "spawn" or "summon":
            {
                Creatures.CreatureKind? kind = args.Count == 0 ? null : args[0].ToLowerInvariant() switch
                {
                    "parasite" or "mineralparasite" or "mineral" => Creatures.CreatureKind.MineralParasite,
                    "egg" or "swarmegg" => Creatures.CreatureKind.SwarmEgg,
                    "needle" => Creatures.CreatureKind.Needle,
                    "scavenger" or "flyer" => Creatures.CreatureKind.Scavenger,
                    _ => null,
                };
                return kind is { } k
                    ? ([host.SpawnCreature(k)], false, false)
                    : (["Usage: spawn <parasite|egg|needle|scavenger>"], true, false);
            }

            case "health" or "hp":
                return RunSetSuitStat("Health", args, host, (suit, val) => suit.SetHealth(val), suit => (suit.Health, suit.MaxHealth));

            case "power":
                return RunSetSuitStat("Power", args, host, (suit, val) => suit.SetPowerLevel(val), suit => (suit.Power, suit.MaxPower));

            case "fov":
                return RunFov(args, host);

            case "assist":
                return RunToggle("Flight Assistant", args, () => host.Cvars.Bool(Cvars.FlightAssist), v => host.Cvars.Set(Cvars.FlightAssist, v));

            case "fixedcam":
                return RunToggle("Fixed camera when aboard", args, () => host.Cvars.Bool(Cvars.FixedCamera), v => host.Cvars.Set(Cvars.FixedCamera, v));

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

    static (IReadOnlyList<string>, bool, bool) RunHelp(List<string> args, CvarStore cvars)
    {
        if (args.Count > 0 && cvars.Find(args[0]) is { } setting)
            return RunCvar(setting, [], cvars);
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
        output.Add($"Settings: {cvars.All.Count} (type 'cvarlist' or 'find <text>'; Tab completes names).");
        return (output, false, false);
    }

    // ---- settings ----

    static string Describe(Cvar cvar, CvarStore cvars)
    {
        var tags = new List<string> { $"default {cvar.Format(cvar.Default)}", cvar.RangeText };
        if (cvar.Flags.HasFlag(CvarFlags.World)) tags.Add("world rule");
        if (cvar.Flags.HasFlag(CvarFlags.User)) tags.Add("preference");
        if (cvar.IsCheat) tags.Add("cheat");
        return $"{cvar.Name} = {cvar.Format(cvars.Get(cvar))}  ({string.Join(", ", tags)})";
    }

    static (IReadOnlyList<string>, bool, bool) RunCvar(Cvar cvar, List<string> args, CvarStore cvars)
    {
        if (args.Count == 0)
            return ([Describe(cvar, cvars), $"  {cvar.Description}"], false, false);
        if (args[0].Equals("default", StringComparison.OrdinalIgnoreCase) || args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            cvars.Reset(cvar);
            return ([ $"{cvar.Name} = {cvar.Format(cvars.Get(cvar))} (default)"], false, false);
        }
        if (!cvars.TrySet(cvar, string.Join(' ', args), out var error))
            return ([error!], true, false);
        return ([ $"{cvar.Name} = {cvar.Format(cvars.Get(cvar))}"], false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunFind(List<string> args, CvarStore cvars)
    {
        if (args.Count == 0) return (["Usage: find <text>"], true, false);
        var text = string.Join(' ', args);
        var matches = cvars.All.Where(c =>
            c.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            c.Description.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) return ([ $"No settings matching '{text}'."], false, false);
        var lines = new List<string> { $"Settings matching '{text}' ({matches.Count}):" };
        foreach (var c in matches)
            lines.Add($"  {c.Name} = {c.Format(cvars.Get(c))}  - {c.Description}");
        return (lines, false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunCvarList(List<string> args, CvarStore cvars)
    {
        var prefix = args.Count > 0 ? args[0] : "";
        var list = cvars.All.Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) return ([ $"No settings start with '{prefix}'."], false, false);
        var lines = new List<string>(list.Count + 1) { $"{list.Count} settings{(prefix.Length > 0 ? $" starting with '{prefix}'" : "")}:" };
        foreach (var c in list)
            lines.Add($"  {c.Name,-32} {c.Format(cvars.Get(c)),-8}{(cvars.IsDefault(c) ? "" : $" (default {c.Format(c.Default)})")}");
        return (lines, false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunDifferences(CvarStore cvars)
    {
        var changed = cvars.Differences();
        if (changed.Count == 0)
            return ([ cvars.CheatsUsed ? "Every setting is at its default (cheats were used in this world)." : "Every setting is at its default: the original preset."], false, false);
        var lines = new List<string> { $"{changed.Count} setting(s) differ from the defaults:" };
        foreach (var c in changed)
            lines.Add($"  {c.Name} = {c.Format(cvars.Get(c))}  (default {c.Format(c.Default)})");
        if (cvars.CheatsUsed) lines.Add("Cheats were used in this world.");
        return (lines, false, false);
    }

    static (IReadOnlyList<string>, bool, bool) RunToggleCvar(List<string> args, CvarStore cvars)
    {
        if (args.Count == 0) return (["Usage: toggle <setting>"], true, false);
        if (cvars.Find(args[0]) is not { } cvar) return ([ $"Unknown setting '{args[0]}'."], true, false);
        if (cvar.Kind != CvarKind.Bool) return ([ $"{cvar.Name} is not an on/off setting; set it with '{cvar.Name} <value>'."], true, false);
        return RunCvar(cvar, [cvars.Bool(cvar) ? "0" : "1"], cvars);
    }

    static (IReadOnlyList<string>, bool, bool) RunReset(List<string> args, CvarStore cvars)
    {
        if (args.Count == 0) return (["Usage: reset <setting|rules|prefs|all>"], true, false);
        switch (args[0].ToLowerInvariant())
        {
            case "rules":
                cvars.ResetAll(CvarFlags.World);
                return (["World rules (sv_) reset to the original game's."], false, false);
            case "prefs" or "preferences":
                cvars.ResetAll(CvarFlags.User);
                return (["Preferences (cl_) reset to defaults."], false, false);
            case "all":
                cvars.ResetAll();
                return (["Every setting reset to its default."], false, false);
        }
        if (cvars.Find(args[0]) is not { } cvar) return ([ $"Unknown setting '{args[0]}'."], true, false);
        return RunCvar(cvar, ["default"], cvars);
    }

    /// <returns>Whether anything went wrong.</returns>
    static bool RunPreset(List<string> args, IDebugConsoleHost host, int depth, Output output)
    {
        if (args.Count == 0)
        {
            output.Add("Presets (preset <name>):");
            output.Add(Presets.Select(p => $"  {p.Name,-10} - {p.Summary}"), false);
            output.Add("Your own: put commands in a .cfg file in the config folder and 'exec' it.");
            return false;
        }
        var preset = Presets.FirstOrDefault(p => p.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            output.Add($"Unknown preset '{args[0]}'. Type 'preset' for the list.", true);
            return true;
        }
        var (error, _) = RunLine(preset.Commands, host, depth + 1, output);
        output.Add(error ? $"Preset '{preset.Name}' applied with errors." : $"Preset '{preset.Name}' applied.", error);
        return error;
    }

    /// <returns>Whether anything went wrong.</returns>
    static bool RunExec(List<string> args, IDebugConsoleHost host, int depth, Output output)
    {
        string? problem = null;
        string name = string.Join(' ', args), text = "";
        if (args.Count == 0) problem = "Usage: exec <file[.cfg]>";
        else if (depth >= MaxExecDepth) problem = $"exec: config files nest more than {MaxExecDepth} deep; stopped.";
        else if (!host.TryReadConfig(name, out text, out var readError)) problem = $"exec: {readError}";
        if (problem is not null)
        {
            output.Add(problem, true);
            return true;
        }

        bool anyError = false;
        int lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;
            var (error, _) = RunLine(line, host, depth + 1, output);
            if (error) output.Add($"  (in {name}, line {lineNumber})", true);
            anyError |= error;
        }
        output.Add($"Executed {name}.");
        return anyError;
    }

    /// <summary>A line without its <c>//</c> comment (outside quotes).</summary>
    static string StripComment(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (!inQuotes && line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/') return line[..i];
        }
        return line;
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

    static string SpeedText(CvarStore cvars)
    {
        float scale = cvars.Float(Cvars.PlayerSpeedScale);
        return FormattableString.Invariant(
            $"{scale:0.##}x (cruise {cvars.Float(Cvars.PlayerSpeed) * scale:0.#} m/s, boost {cvars.Float(Cvars.PlayerBoostSpeed) * scale:0.#} m/s)");
    }

    static (IReadOnlyList<string>, bool, bool) RunSpeed(List<string> args, IDebugConsoleHost host)
    {
        var cvars = host.Cvars;
        var setting = Cvars.PlayerSpeedScale;
        if (args.Count == 0)
            return ([ $"Speed multiplier: {SpeedText(cvars)}"], false, false);

        if (args[0].Equals("reset", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            cvars.Reset(setting);
            return ([ $"Speed multiplier reset to {SpeedText(cvars)}."], false, false);
        }

        if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float mult) || !float.IsFinite(mult))
            return ([ $"Invalid speed '{args[0]}'. Usage: speed <0.1..50|reset>"], true, false);

        // The command clamps for convenience; the setting itself refuses out-of-range values.
        double clamped = Math.Clamp(mult, setting.Min, setting.Max);
        if (!cvars.TrySet(setting, clamped, out var error))
            return ([error!], true, false);
        return ([ $"Speed multiplier set to {SpeedText(cvars)}."], false, false);
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
        if (first == "aim")
        {
            float back = 4;
            if (args.Count > 1 && (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out back) || !float.IsFinite(back) || back < 0))
                return (["Usage: tp aim [metres short, default 4]"], true, false);
            bool ok = host.TeleportToAim(back, out var msg);
            return ([msg], !ok, false);
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
        var assist = host.Cvars.Bool(Cvars.FlightAssist) ? "ON" : "OFF";
        return ([
            FormattableString.Invariant($"Position: ({p.X:0.##}, {p.Y:0.##}, {p.Z:0.##})  Speed: {v.Length():0.##} m/s ({host.Cvars.Float(Cvars.PlayerSpeedScale):0.##}x)"),
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
        if (suit.IsDead) return (["Dead: use the Respawn button first."], true, false);
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
        var setting = Cvars.Fov;
        if (args.Count == 0)
            return ([FormattableString.Invariant($"Camera FOV: {host.Cvars.Get(setting):0.#}° (default {setting.Default:0}°)")], false, false);

        if (args[0].Equals("reset", StringComparison.OrdinalIgnoreCase) ||
            args[0].Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            host.Cvars.Reset(setting);
            return ([FormattableString.Invariant($"Camera FOV reset to {setting.Default:0}°.")], false, false);
        }

        if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float fov) || !float.IsFinite(fov))
            return ([ $"Invalid FOV '{args[0]}'. Usage: fov <45..135|reset>"], true, false);

        double clamped = Math.Clamp(fov, setting.Min, setting.Max);
        host.Cvars.Set(setting, clamped);
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
