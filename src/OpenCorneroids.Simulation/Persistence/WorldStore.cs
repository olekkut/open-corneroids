using System.Buffers;
using System.Text;

namespace OpenCorneroids.Simulation.Persistence;

/// <summary>World saves as files in one folder, one <see cref="Extension"/> file per world, named after the world.</summary>
public static class WorldStore
{
    public const string Extension = ".ocworld";

    /// <summary>Keeps the full path well under Windows' 260-character limit even in a deep user-data folder.</summary>
    const int MaxStemLength = 64;
    const string FallbackStem = "world";

    /// <summary>
    /// Characters rejected by Windows, macOS or Linux. A fixed set rather than <see cref="Path.GetInvalidFileNameChars"/>,
    /// which is per-OS, so a world saved on Linux keeps its file name when the folder is copied to Windows.
    /// </summary>
    static readonly SearchValues<char> Unsafe = SearchValues.Create("<>:\"/\\|?*");

    static readonly HashSet<string> ReservedOnWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// A file name that is valid on every desktop OS and never empty. Different world names can map to the same file
    /// (e.g. "a/b" and "a_b"); saving one then replaces the other, which callers can avoid by checking
    /// <see cref="List"/> first.
    /// </summary>
    public static string FileNameFor(string worldName)
    {
        var stem = new StringBuilder(worldName?.Length ?? 0);
        foreach (var c in worldName ?? "")
            stem.Append(char.IsControl(c) || Unsafe.Contains(c) ? '_' : c);

        // Windows drops trailing dots and spaces; leading dots hide files on Unix.
        var name = TrimEdges(stem.ToString());
        if (name.Length > MaxStemLength)
        {
            // Don't split a surrogate pair.
            int cut = char.IsHighSurrogate(name[MaxStemLength - 1]) ? MaxStemLength - 1 : MaxStemLength;
            name = TrimEdges(name[..cut]);
        }
        if (name.Length == 0) name = FallbackStem;

        // Windows treats "CON.txt" like "CON", so the part before the first dot is what matters.
        var device = name.Split('.')[0].TrimEnd();
        if (ReservedOnWindows.Contains(device)) name = "_" + name;
        return name + Extension;
    }

    /// <summary>
    /// Writes to a temporary file first and then replaces the old save in one move, so a crash or full disk mid-write
    /// leaves the previous save intact instead of a truncated one.
    /// </summary>
    public static void Save(string directory, WorldSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(snapshot);
        SaveFile(Path.Combine(directory, FileNameFor(snapshot.Name)), snapshot);
    }

    /// <summary>
    /// Like <see cref="Save"/>, but to an exact file, whatever the world is called. A world loaded from a file whose
    /// name does not match its <see cref="WorldSnapshot.Name"/> (a copied backup, a renamed file) must be saved back
    /// to that same file; saving by name would overwrite whichever world owns the name.
    /// </summary>
    public static void SaveFile(string path, WorldSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory) Directory.CreateDirectory(directory);

        // Not ending in Extension, so List never sees a half-written file.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                WorldSaveFormat.Write(file, snapshot);
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <exception cref="WorldSaveException">The file is not a readable world save.</exception>
    public static WorldSnapshot Load(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return WorldSaveFormat.Read(file);
    }

    /// <summary>See <see cref="WorldSaveFormat.Verify"/>: reads the whole file as <see cref="Load"/> would, keeping nothing.</summary>
    /// <exception cref="WorldSaveException">The file is not a readable world save.</exception>
    public static void Verify(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        WorldSaveFormat.Verify(file);
    }

    /// <summary>
    /// Newest first. Damaged, foreign-version or locked files are skipped rather than failing the whole list, so one
    /// bad file never hides the player's other worlds. A missing folder simply has no worlds.
    /// </summary>
    public static IReadOnlyList<WorldSummary> List(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!Directory.Exists(directory)) return [];

        var summaries = new List<WorldSummary>();
        foreach (var path in Directory.EnumerateFiles(directory, "*" + Extension))
        {
            if (!Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                summaries.Add(WorldSaveFormat.ReadSummary(file) with { FilePath = Path.GetFullPath(path) });
            }
            catch (Exception e) when (e is WorldSaveException or IOException or UnauthorizedAccessException)
            {
                // Skipped; see the summary.
            }
        }

        return summaries
            .OrderByDescending(s => s.SavedAt)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => s.FilePath, StringComparer.Ordinal)
            .ToList();
    }

    static string TrimEdges(string s) => s.Trim(EdgeChars);

    static readonly char[] EdgeChars = [' ', '.', '\u00A0', '\u3000'];

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
