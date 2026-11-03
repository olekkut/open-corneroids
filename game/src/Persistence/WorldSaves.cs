using System.Text;
using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Simulation.Persistence;

namespace OpenCorneroids.Game.Persistence;

/// <summary>What the menu (or <c>--new-world</c>) asks for when it creates a world.</summary>
public sealed record NewWorldSettings(ItemsetEntry Itemset, IReadOnlyList<PackInfo> TexturePacks, string Name, int Seed, bool Creative);

/// <summary>
/// Identity of a running world that is not part of the world itself: what it is called, how it was generated, which
/// rules apply and which texture packs dress it. Kept as the player chose it, so saving never loses a texture pack id
/// just because that pack is not installed right now.
/// </summary>
/// <param name="FilePath">The save file this world writes to: the file it was loaded from, or for a new world the one
/// named after it. Not derived from <paramref name="Name"/> on every save, so a copied or renamed save file stays the
/// one that gets updated and never overwrites the world whose name it carries.</param>
public sealed record WorldInfo(string Name, int Seed, bool Creative, IReadOnlyList<string> TexturePackIds, string FilePath);

/// <summary>One Continue entry. <paramref name="Summary"/> is null when even the header does not read;
/// <paramref name="Problem"/> is null for a save that reads to its end.</summary>
public sealed record SavedWorld(string FilePath, WorldSummary? Summary, string? Problem)
{
    public bool Damaged => Problem is not null;
}

/// <summary>The game's view of <see cref="WorldStore"/>: where saves live and the small rules around naming them.</summary>
public static class WorldSaves
{
    /// <summary>Seed for worlds started from the command line without <c>--seed</c>, so screenshots stay reproducible.</summary>
    public const int DefaultSeed = 2012;

    /// <summary><c>user://worlds</c>, i.e. <c>%APPDATA%\OpenCorneroids\worlds</c> on Windows.</summary>
    public static string Folder => ProjectSettings.GlobalizePath("user://worlds");

    /// <summary>Saved worlds, newest first. A folder that can't be read lists as empty rather than breaking the menu.</summary>
    public static IReadOnlyList<WorldSummary> List()
    {
        try
        {
            return WorldStore.List(Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not list saved worlds in {Folder}: {e.Message}");
            return [];
        }
    }

    /// <summary>
    /// Every <c>.ocworld</c> file for the Continue list, each read through to its last cell. Healthy saves come first,
    /// newest first; then saves whose header reads but whose body does not (a crash or a bad sync cut them off), and
    /// files <see cref="List"/> skipped outright. Damaged ones are still listed so the player learns about the damage
    /// before picking one, and can delete the file. Saves are kilobytes, so reading them all is cheap.
    /// </summary>
    public static IReadOnlyList<SavedWorld> ListChecked()
    {
        var healthy = new List<SavedWorld>();
        var damaged = new List<SavedWorld>();
        var readable = List();
        foreach (var world in readable)
        {
            try
            {
                WorldStore.Verify(world.FilePath);
                healthy.Add(new SavedWorld(world.FilePath, world, null));
            }
            catch (Exception e) when (e is WorldSaveException or IOException or UnauthorizedAccessException)
            {
                damaged.Add(new SavedWorld(world.FilePath, world, e.Message));
            }
        }

        try
        {
            if (Directory.Exists(Folder))
            {
                var known = readable.Select(w => w.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                damaged.AddRange(Directory.EnumerateFiles(Folder, "*" + WorldStore.Extension)
                    .Where(f => Path.GetExtension(f).Equals(WorldStore.Extension, StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFullPath)
                    .Where(f => !known.Contains(f))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Select(f => new SavedWorld(f, null, "its header can't be read.")));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not list saved worlds in {Folder}: {e.Message}");
        }

        foreach (var world in damaged) GD.PushWarning($"Damaged world save {world.FilePath}: {world.Problem}");
        return [.. healthy, .. damaged];
    }

    /// <summary>
    /// Whether a new world may not use this name: a file already maps to it (names map to files, see
    /// <see cref="WorldStore.FileNameFor"/>, so "a/b" and "a_b" collide), or a readable save already carries it inside,
    /// whatever its file is called (a renamed or copied file). Two worlds with one name could not be told apart.
    /// </summary>
    public static bool Exists(string name) =>
        File.Exists(PathFor(name)) || List().Any(w => w.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string PathFor(string name) => Path.Combine(Folder, WorldStore.FileNameFor(name));

    /// <summary>
    /// The save file for <c>--load=name</c>: the file named after it if that holds the world (so of a world and its
    /// copied backup, the original wins), then any save called exactly that, then ignoring case, then any file of that
    /// name even if it does not read, so a damaged save reports its error instead of "no such world".
    /// </summary>
    public static string? FindFile(string name)
    {
        var worlds = List();
        var path = Path.GetFullPath(PathFor(name));
        var match = worlds.FirstOrDefault(w => w.Name == name && w.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                    ?? worlds.FirstOrDefault(w => w.Name == name)
                    ?? worlds.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return match?.FilePath ?? (File.Exists(path) ? path : null);
    }

    /// <summary>"Asteroid 1", "Asteroid 2", ...: the first one no save uses yet.</summary>
    public static string NextDefaultName()
    {
        var names = List().Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int n = 1; ; n++)
        {
            var name = $"Asteroid {n}";
            if (!names.Contains(name) && !File.Exists(PathFor(name))) return name;
        }
    }

    /// <summary>
    /// A seed from the menu's text field: a number is used as is, other text is hashed (so "kosmos" is a seed too),
    /// and empty text gets a random seed. The hash is our own FNV-1a, not <see cref="string.GetHashCode()"/>, which
    /// changes between runs.
    /// </summary>
    public static int ParseSeed(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return (int)(GD.Randi() & int.MaxValue);
        if (int.TryParse(text, out var seed)) return seed;
        uint hash = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes(text)) hash = (hash ^ b) * 16777619;
        return (int)hash;
    }
}
