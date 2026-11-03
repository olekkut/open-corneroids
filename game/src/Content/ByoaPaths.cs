using Godot;

namespace OpenCorneroids.Game.Content;

/// <summary>Where players' original files and mods are looked for (design doc §6).</summary>
public static class ByoaPaths
{
    const string Readme = """
        Open Corneroids — Bring Your Own Assets
        =======================================

        Drop your own copies of Corneroids content here, exactly as downloaded:

          games\        full installs or repacks (e.g. Corneroids-Alpha-1.2.0, which contains Corneroids\Content\...)
          mods\         itemset mods (e.g. DarthNoxItemset, navItemset, each with a Content\ folder)
          texturepacks\ texture packs (e.g. SordsTextures with blockTextures.png and lightTextureAtlas.png)

        Folders are recognised by what they contain, so the subfolder names are only a guide.
        Nothing in here is ever modified. Restart the game after adding content.
        """;

    /// <summary>Roots to scan, in order: --byoa-dir, a byoa folder next to the executable, the user folder, the repo's byoa folder.</summary>
    public static IReadOnlyList<string> FindRoots(GameArgs args)
    {
        var roots = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            var full = Path.GetFullPath(path);
            if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase)) roots.Add(full);
        }

        Add(args.ByoaDir);
        Add(Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "byoa"));
        Add(EnsureUserRoot());
        // Development: the repository keeps a gitignored byoa/ next to the game/ project.
        Add(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "byoa"));
        return roots;
    }

    /// <summary>The per-user drop folder (%APPDATA%\OpenCorneroids\byoa on Windows), created with a README on first run.</summary>
    public static string UserRoot => ProjectSettings.GlobalizePath("user://byoa");

    static string? EnsureUserRoot()
    {
        try
        {
            foreach (var hint in new[] { "games", "mods", "texturepacks" })
                Directory.CreateDirectory(Path.Combine(UserRoot, hint));
            var readme = Path.Combine(UserRoot, "README.txt");
            if (!File.Exists(readme)) File.WriteAllText(readme, Readme);
            return UserRoot;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not create {UserRoot}: {e.Message}");
            return null;
        }
    }
}
