using System.IO.Compression;
using System.Security.Cryptography;
using OpenCorneroids.Content.Vfs;

namespace OpenCorneroids.Content.Discovery;

/// <summary>
/// Finds packs in BYOA roots by content, not by name. Each child folder or archive of a root is one pack;
/// the hint folders <c>games</c>, <c>mods</c> and <c>texturepacks</c> are looked through, so both a flat root
/// and the documented layout work.
/// </summary>
public static class PackScanner
{
    const int MaxContentSearchDepth = 5;
    static readonly string[] HintFolders = ["games", "mods", "texturepacks"];
    static readonly string[] ContentMarkers = ["Blocksets", "Textures"];

    // Where loose atlas PNGs go: the mod readmes say to drop them into these vanilla folders.
    internal static readonly Dictionary<string, string> LooseTextureTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blockTextures.png"] = "Textures/Blocks/blockTextures.png",
        ["lightTextureAtlas.png"] = "Textures/Blocks/lightTextureAtlas.png",
        ["spriteImages.png"] = "Textures/Sprites/spriteImages.png",
    };

    sealed record CandidateTarget(string Path, string Name, bool IsZip);

    public static IReadOnlyList<PackInfo> Scan(IEnumerable<string> roots, DiagnosticBag diagnostics)
    {
        var packs = new List<PackInfo>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        foreach (var target in CandidateTargets(root, diagnostics))
        {
            var id = UniqueId(target.Name, ids);
            var pack = target.IsZip
                ? DetectZip(target.Path, id, diagnostics)
                : Detect(target.Path, id, diagnostics);
            if (pack is not null)
                packs.Add(pack);
        }
        return packs;
    }

    public static PackInfo? Detect(string folder, string id, DiagnosticBag diagnostics)
    {
        if (File.Exists(folder) || (folder.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(folder)))
            return DetectZip(folder, id, diagnostics);

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

    public static PackInfo? DetectZip(string zipPath, string id, DiagnosticBag diagnostics)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Report(Severity.Info, Codes.PackUnrecognized,
                "corrupted or unreadable zip archive; ignored", zipPath);
            return null;
        }

        using (archive)
        {
            IReadOnlyList<ZipArchiveEntry> entries;
            try
            {
                entries = archive.Entries;
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                diagnostics.Report(Severity.Info, Codes.PackUnrecognized,
                    "corrupted zip entries; ignored", zipPath);
                return null;
            }

            if (entries.Count == 0)
            {
                diagnostics.Report(Severity.Info, Codes.PackUnrecognized,
                    "empty zip archive; ignored", zipPath);
                return null;
            }

            string? contentPrefix = FindZipContentPrefix(entries);
            if (contentPrefix is not null)
            {
                var source = ZipSource.FromArchive(id, zipPath, contentPrefix);
                bool hasItemsets = source.List("Blocksets", ".xml").Any();
                bool hasExe = entries.Any(e =>
                {
                    var name = Path.GetFileName(e.FullName);
                    return name.Equals("Corneroids.exe", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Corneroids.exe.deploy", StringComparison.OrdinalIgnoreCase);
                });
                bool hasEngineData = source.List("Shaders", ".xnb").Any();
                var kind = !hasItemsets ? PackKind.TexturePack : hasExe || hasEngineData ? PackKind.GameInstall : PackKind.ItemsetMod;
                var vanillaVersion = kind == PackKind.GameInstall ? IdentifyVanilla(source) : null;
                return new PackInfo(id, kind, zipPath, source, vanillaVersion);
            }

            var looseEntries = new List<KeyValuePair<string, string>>();
            foreach (var entry in entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
                var norm = ContentPath.Normalize(entry.FullName);
                var fileName = Path.GetFileName(norm);
                if (LooseTextureTargets.TryGetValue(fileName, out var target))
                {
                    looseEntries.Add(new(target, entry.FullName));
                }
                looseEntries.Add(new(norm, entry.FullName));
            }

            if (looseEntries.Any(e => LooseTextureTargets.ContainsValue(e.Key)))
            {
                var source = new ZipSource(id, zipPath, looseEntries);
                return new PackInfo(id, PackKind.TexturePack, zipPath, source, null);
            }

            diagnostics.Report(Severity.Info, Codes.PackUnrecognized,
                "no Content folder or known atlas PNGs found; ignored", zipPath);
            return null;
        }
    }

    static string? FindZipContentPrefix(IReadOnlyList<ZipArchiveEntry> entries)
    {
        string? bestNested = null;
        int minNestedDepth = int.MaxValue;

        string? bestFlat = null;
        int minFlatDepth = int.MaxValue;

        foreach (var entry in entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            var norm = ContentPath.Normalize(entry.FullName);
            var segments = norm.Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("Content", StringComparison.OrdinalIgnoreCase) &&
                    ContentMarkers.Contains(segments[i + 1], StringComparer.OrdinalIgnoreCase))
                {
                    if (i < minNestedDepth)
                    {
                        minNestedDepth = i;
                        bestNested = string.Join('/', segments.Take(i + 1));
                    }
                    break;
                }
            }

            for (int i = 0; i < segments.Length; i++)
            {
                if (ContentMarkers.Contains(segments[i], StringComparer.OrdinalIgnoreCase))
                {
                    if (i < minFlatDepth)
                    {
                        minFlatDepth = i;
                        bestFlat = i == 0 ? "" : string.Join('/', segments.Take(i));
                    }
                    break;
                }
            }
        }

        if (bestNested is not null && minNestedDepth <= MaxContentSearchDepth)
            return bestNested;

        if (bestFlat is not null && minFlatDepth <= MaxContentSearchDepth)
            return bestFlat;

        return null;
    }

    static IEnumerable<CandidateTarget> CandidateTargets(string root, DiagnosticBag diagnostics)
    {
        var rootDirStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in SafeDirectories(root))
        {
            var dirName = Path.GetFileName(dir);
            if (HintFolders.Contains(dirName, StringComparer.OrdinalIgnoreCase))
            {
                var subDirs = SafeDirectories(dir).ToList();
                var hintDirStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var packDir in subDirs)
                {
                    hintDirStems.Add(Path.GetFileName(packDir));
                    yield return new CandidateTarget(packDir, Path.GetFileName(packDir), IsZip: false);
                }

                foreach (var zip in SafeFiles(dir).Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    var stem = Path.GetFileNameWithoutExtension(zip);
                    if (hintDirStems.Contains(stem))
                    {
                        diagnostics.Report(Severity.Warning, "pack-shadowed",
                            $"archive '{zip}' shadowed by directory '{Path.Combine(dir, stem)}'", zip);
                        continue;
                    }
                    yield return new CandidateTarget(zip, stem, IsZip: true);
                }
            }
            else
            {
                rootDirStems.Add(dirName);
                yield return new CandidateTarget(dir, dirName, IsZip: false);
            }
        }

        foreach (var zip in SafeFiles(root).Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            var stem = Path.GetFileNameWithoutExtension(zip);
            if (rootDirStems.Contains(stem))
            {
                diagnostics.Report(Severity.Warning, "pack-shadowed",
                    $"archive '{zip}' shadowed by directory '{Path.Combine(root, stem)}'", zip);
                continue;
            }
            yield return new CandidateTarget(zip, stem, IsZip: true);
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

    static Version? IdentifyVanilla(IContentSource source)
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
