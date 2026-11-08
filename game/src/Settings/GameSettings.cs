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
                foreach (var problem in Cvars.ApplyConfig(File.ReadAllText(path), CvarFlags.User))
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
        if (_hooked) return;
        _hooked = true;
        Cvars.Changed += (cvar, _) =>
        {
            if (!_loading && cvar.Flags.HasFlag(CvarFlags.User) && WriteUserConfig() is { } error) GD.PushWarning(error);
        };
    }

    /// <summary>Writes the changed preferences to <c>config.cfg</c>; null, or what went wrong.</summary>
    public static string? WriteUserConfig()
    {
        var path = Path.Combine(Folder, ConfigFile);
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(path, Cvars.WriteConfig(CvarFlags.User,
                "Open Corneroids preferences, written by the game. Settings left out are at their defaults.\n" +
                "Put your own commands in autoexec.cfg instead: this file is rewritten whenever a preference changes."));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Could not write {path}: {e.Message}";
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
