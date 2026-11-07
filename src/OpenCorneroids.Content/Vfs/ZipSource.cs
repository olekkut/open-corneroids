using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace OpenCorneroids.Content.Vfs;

/// <summary>
/// A pack backed by a .zip archive. Entries are indexed at discovery, and files are decompressed
/// on-demand into memory streams without holding persistent OS file locks.
/// </summary>
public sealed class ZipSource : IContentSource
{
    static readonly Dictionary<string, string> LooseTextureTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["blockTextures.png"] = "Textures/Blocks/blockTextures.png",
        ["lightTextureAtlas.png"] = "Textures/Blocks/lightTextureAtlas.png",
        ["spriteImages.png"] = "Textures/Sprites/spriteImages.png",
    };

    readonly string _archivePath;
    readonly Dictionary<string, string> _entries = new(ContentPath.Comparer);

    public ZipSource(string packId, string archivePath, IEnumerable<KeyValuePair<string, string>> entries)
    {
        PackId = packId;
        _archivePath = archivePath;
        foreach (var (contentPath, entryFullName) in entries)
            _entries[ContentPath.Normalize(contentPath)] = entryFullName;
    }

    /// <summary>Indexes entries in <paramref name="archivePath"/> mounted under <paramref name="contentRootPrefix"/>.</summary>
    public static ZipSource FromArchive(string packId, string archivePath, string contentRootPrefix = "")
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = new List<KeyValuePair<string, string>>();
        var prefix = ContentPath.Normalize(contentRootPrefix ?? "");
        if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            var normalized = ContentPath.Normalize(entry.FullName);

            if (prefix.Length == 0)
            {
                var rel = normalized;
                if (rel.EndsWith(".deploy", StringComparison.OrdinalIgnoreCase))
                    rel = rel[..^".deploy".Length];
                entries.Add(new(rel, entry.FullName));

                var fileName = Path.GetFileName(rel);
                if (rel.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                    LooseTextureTargets.TryGetValue(fileName, out var target))
                {
                    entries.Add(new(target, entry.FullName));
                }
            }
            else if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var rel = normalized[prefix.Length..];
                if (rel.EndsWith(".deploy", StringComparison.OrdinalIgnoreCase))
                    rel = rel[..^".deploy".Length];
                entries.Add(new(rel, entry.FullName));
            }
        }
        return new ZipSource(packId, archivePath, entries);
    }

    public string PackId { get; }

    public IReadOnlyCollection<string> Paths => _entries.Keys;

    public bool Exists(string path) =>
        !string.IsNullOrEmpty(path) && _entries.ContainsKey(ContentPath.Normalize(path));

    public string? Locate(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return _entries.TryGetValue(ContentPath.Normalize(path), out var entry)
            ? $"{_archivePath}!/{entry.TrimStart('/', '\\')}"
            : null;
    }

    public bool TryOpen(string path, [NotNullWhen(true)] out Stream? stream)
    {
        if (!string.IsNullOrEmpty(path) && _entries.TryGetValue(ContentPath.Normalize(path), out var entryName))
        {
            try
            {
                using var archive = ZipFile.OpenRead(_archivePath);
                var entry = archive.GetEntry(entryName)
                    ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, entryName, StringComparison.OrdinalIgnoreCase));
                if (entry is not null)
                {
                    var ms = entry.Length > 0 ? new MemoryStream((int)entry.Length) : new MemoryStream();
                    using var es = entry.Open();
                    es.CopyTo(ms);
                    ms.Position = 0;
                    stream = ms;
                    return true;
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                // Corrupted archive or entry
            }
        }
        stream = null;
        return false;
    }

    public IEnumerable<string> List(string directory, string extension)
    {
        var dir = ContentPath.Normalize(directory ?? "");
        var ext = extension ?? "";
        return _entries.Keys
            .Where(p => ContentPath.Comparer.Equals(ContentPath.Directory(p), dir)
                        && p.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            .Order(ContentPath.Comparer);
    }
}
