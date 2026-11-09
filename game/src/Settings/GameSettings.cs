using Godot;
using OpenCorneroids.Simulation.Settings;

namespace OpenCorneroids.Game.Settings;

/// <summary>
/// The game's one settings store (<see cref="CvarStore"/>) and the files around it, all in the user data folder
/// (<c>%APPDATA%\OpenCorneroids</c> on Windows):
/// <list type="bullet">
/// <item><c>config.cfg</c>: the player's preferences (<c>cl_</c>), read at start and written whenever one changes;</item>
/// <item><c>autoexec.cfg</c>: optional commands of the player's own, run each time a world starts;</item>
/// <item>any other <c>.cfg</c>: run on demand with <c>exec</c>.</item>
/// </list>
/// World rules (<c>sv_</c>) are not kept here: each world saves its own.
/// </summary>
public static class GameSettings
{
    public const string ConfigFile = "config.cfg";
    public const string AutoexecFile = "autoexec.cfg";

    public static CvarStore Cvars { get; } = new();

    static readonly Cvar[] DisplayCvars =
    [
        Simulation.Settings.Cvars.Fullscreen, Simulation.Settings.Cvars.ResolutionWidth,
        Simulation.Settings.Cvars.ResolutionHeight, Simulation.Settings.Cvars.VSync,
    ];

    static bool _loading;
    static bool _hooked;

    /// <summary>Where config files live: the user data folder.</summary>
    public static string Folder => ProjectSettings.GlobalizePath("user://");

    /// <summary>Reads <c>config.cfg</c> and starts saving preferences as they change. Called once at start.</summary>
    public static void Load()
    {
        var path = Path.Combine(Folder, ConfigFile);
        if (File.Exists(path))
        {
            _loading = true;
            try
            {
                var settings = new List<string>();
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.TrimStart().StartsWith("bind ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Bind(line.Trim()[5..]) is { } bindProblem) GD.PushWarning($"{ConfigFile}: {bindProblem}");
                    }
                    else settings.Add(line);
                }
                foreach (var problem in Cvars.ApplyConfig(string.Join('\n', settings), CvarFlags.User))
                    GD.PushWarning($"{ConfigFile}: {problem}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.PushWarning($"Could not read {path}: {e.Message}");
            }
            finally
            {
                _loading = false;
            }
        }
        // The project's own window settings stand until the player changes the display (keeps test runs windowed).
        if (DisplayCvars.Any(c => !Cvars.IsDefault(c))) ApplyDisplay();
        if (_hooked) return;
        _hooked = true;
        Cvars.Changed += (cvar, _) =>
        {
            if (_loading) return;
            if (cvar.Flags.HasFlag(CvarFlags.User) && WriteUserConfig() is { } error) GD.PushWarning(error);
            if (DisplayCvars.Contains(cvar)) ApplyDisplay();
        };
    }

    /// <summary>Writes the changed preferences to <c>config.cfg</c>; null, or what went wrong.</summary>
    public static string? WriteUserConfig()
    {
        var path = Path.Combine(Folder, ConfigFile);
        try
        {
            Directory.CreateDirectory(Folder);
            var text = Cvars.WriteConfig(CvarFlags.User,
                "Open Corneroids preferences, written by the game. Settings left out are at their defaults.\n" +
                "Put your own commands in autoexec.cfg instead: this file is rewritten whenever a preference changes.");
            foreach (var line in GameInput.BindLines()) text += line + "\n";
            File.WriteAllText(path, text);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Could not write {path}: {e.Message}";
        }
    }

    /// <summary>
    /// <c>bind "key" "action"</c> (quotes optional): binds the key to a Controls row by its action or label; <c>bind
    /// "key"</c> alone is refused. Returns null, or what was wrong. Saves the preferences when not loading them.
    /// </summary>
    public static string? Bind(string arguments)
    {
        // Words, or "quoted words".
        var parts = System.Text.RegularExpressions.Regex.Matches(arguments, "\"([^\"]*)\"|(\\S+)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Where(p => p.Length > 0).ToList();
        if (parts.Count != 2) return "usage: bind <key> <action>";
        var key = GameInput.ParseKey(parts[0]);
        if (key == Key.None && !parts[0].Equals("None", StringComparison.OrdinalIgnoreCase)) return $"'{parts[0]}' is not a key name";
        if (key != Key.None && !GameInput.Rebindable(key)) return $"{parts[0]} can't be bound (number keys, Escape and function keys are fixed)";
        if (GameInput.FindBinding(parts[1]) is not { } binding) return $"'{parts[1]}' is not an action ({string.Join(", ", GameInput.Bindings.Select(b => b.Name))})";
        GameInput.Rebind(binding, key);
        if (!_loading && WriteUserConfig() is { } error) return error;
        return null;
    }

    /// <summary>
    /// Applies the display preferences: full screen or a window of <c>cl_resolution_width</c> × <c>cl_resolution_height</c>
    /// (the desktop's when 0), and V-Sync. Called at start and when one of them changes.
    /// </summary>
    public static void ApplyDisplay()
    {
        if (DisplayServer.GetName() == "headless") return;
        DisplayServer.WindowSetVsyncMode(Cvars.Bool(Simulation.Settings.Cvars.VSync) ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        int w = Cvars.Int(Simulation.Settings.Cvars.ResolutionWidth), h = Cvars.Int(Simulation.Settings.Cvars.ResolutionHeight);
        if (Cvars.Bool(Simulation.Settings.Cvars.Fullscreen))
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
        else
        {
            if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            var screen = DisplayServer.ScreenGetSize();
            var size = new Vector2I(w > 0 ? Math.Min(w, screen.X) : screen.X * 3 / 4, h > 0 ? Math.Min(h, screen.Y) : screen.Y * 3 / 4);
            DisplayServer.WindowSetSize(size);
            DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition() + (screen - size) / 2);
        }
    }

    /// <summary>
    /// Reads a config file from <see cref="Folder"/> for <c>exec</c>. Only files inside that folder are read, so a
    /// config shared by someone else cannot reach the rest of the disk.
    /// </summary>
    public static bool TryReadConfig(string name, out string text, out string error)
    {
        text = "";
        name = (name ?? "").Trim().Trim('"');
        if (name.Length == 0)
        {
            error = "no file name given";
            return false;
        }
        if (!name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)) name += ".cfg";
        var folder = Path.GetFullPath(Folder);
        var path = Path.GetFullPath(Path.Combine(folder, name));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            error = $"'{name}' is outside the config folder ({folder})";
            return false;
        }
        if (!File.Exists(path))
        {
            error = $"no file '{name}' in {folder}";
            return false;
        }
        try
        {
            text = File.ReadAllText(path);
            error = "";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"could not read '{name}': {e.Message}";
            return false;
        }
    }
}
