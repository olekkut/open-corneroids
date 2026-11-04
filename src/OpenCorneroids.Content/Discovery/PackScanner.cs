using System.Security.Cryptography;
using OpenCorneroids.Content.Vfs;

namespace OpenCorneroids.Content.Discovery;

/// <summary>
/// Finds packs in BYOA roots by content, not by name. Each child folder of a root is one pack; the hint folders
/// <c>games</c>, <c>mods</c> and <c>texturepacks</c> are looked through, so both a flat root and the documented
/// layout work.
/// </summary>
public static class PackScanner
{
    const int MaxContentSearchDepth = 5;
    static readonly string[] HintFolders = ["games", "mods", "texturepacks"];
    static readonly string[] ContentMarkers = ["Blocksets", "Textures"];

    // Where loose atlas PNGs go: the mod readmes say to drop them into these vanilla folders.
    static readonly Dictionary<string, string> LooseTextureTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blockTextures.png"] = "Textures/Blocks/blockTextures.png",
        ["lightTextureAtlas.png"] = "Textures/Blocks/lightTextureAtlas.png",
        ["spriteImages.png"] = "Textures/Sprites/spriteImages.png",
    };

    public static IReadOnlyList<PackInfo> Scan(IEnumerable<string> roots, DiagnosticBag diagnostics)
    {
        var packs = new List<PackInfo>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        foreach (var folder in CandidateFolders(root))
        {
            if (Detect(folder, UniqueId(Path.GetFileName(folder), ids), diagnostics) is { } pack)
                packs.Add(pack);
        }
        return packs;
    }

    public static PackInfo? Detect(string folder, string id, DiagnosticBag diagnostics)
    {
        if (FindContentRoot(folder) is { } content)
        {
            var source = IndexedSource.FromFolder(id, content);
            bool hasItemsets = source.List("Blocksets", ".xml").Any();
            var installDir = Path.GetDirectoryName(content)!;
            bool hasExe = HasFile(installDir, "Corneroids.exe") || HasFile(installDir, "Corneroids.exe.deploy");
            // A copy of an install's data without its program (as the game's own bundle ships it) still carries the
            // game's compiled shaders, which itemset mods never do.
            bool hasEngineData = source.List("Shaders", ".xnb").Any();
            var kind = !hasItemsets ? PackKind.TexturePack : hasExe || hasEngineData ? PackKind.GameInstall : PackKind.ItemsetMod;
            return new PackInfo(id, kind, folder, source, kind == PackKind.GameInstall ? IdentifyVanilla(source) : null);
        }

        var loose = SafeFiles(folder)
            .Where(f => LooseTextureTargets.ContainsKey(Path.GetFileName(f)))
            .Select(f => KeyValuePair.Create(LooseTextureTargets[Path.GetFileName(f)], f))
            .ToList();
        if (loose.Count > 0)
            return new PackInfo(id, PackKind.TexturePack, folder, new IndexedSource(id, loose), null);

        diagnostics.Report(Severity.Info, Codes.PackUnrecognized,
            "no Content folder or known atlas PNGs found; ignored", folder);
        return null;
    }

    static IEnumerable<string> CandidateFolders(string root)
    {
        foreach (var dir in SafeDirectories(root))
        {
            if (HintFolders.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase))
                foreach (var pack in SafeDirectories(dir)) yield return pack;
            else
                yield return dir;
        }
    }

    /// <summary>Shallowest folder named Content that holds Blocksets or Textures (breadth-first, bounded depth).</summary>
    static string? FindContentRoot(string folder)
    {
        if (IsContentRoot(folder)) return folder;
        var queue = new Queue<(string Dir, int Depth)>([(folder, 0)]);
        while (queue.TryDequeue(out var next))
        {
            var children = SafeDirectories(next.Dir).ToList();
            if (children.FirstOrDefault(IsContentRoot) is { } found) return found;
            if (next.Depth < MaxContentSearchDepth)
                foreach (var child in children) queue.Enqueue((child, next.Depth + 1));
        }
        return null;
    }

    static bool IsContentRoot(string dir) =>
        Path.GetFileName(dir).Equals("Content", StringComparison.OrdinalIgnoreCase)
        && SafeDirectories(dir).Any(d => ContentMarkers.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase));

    static Version? IdentifyVanilla(IndexedSource source)
    {
        if (!source.TryOpen("Blocksets/defaultItemset.xml", out var stream)) return null;
        using (stream)
            return KnownVanilla.Identify(Convert.ToHexString(SHA256.HashData(stream)));
    }

    static string UniqueId(string name, HashSet<string> taken)
    {
        var id = name;
        for (int n = 2; !taken.Add(id); n++) id = $"{name} ({n})";
        return id;
    }

    static bool HasFile(string dir, string name) =>
        SafeFiles(dir).Any(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));

    static IEnumerable<string> SafeDirectories(string dir)
    {
        try { return Directory.GetDirectories(dir).Order(StringComparer.OrdinalIgnoreCase); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.GetFiles(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
