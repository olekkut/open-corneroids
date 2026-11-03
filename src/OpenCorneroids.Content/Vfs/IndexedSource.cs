using System.Diagnostics.CodeAnalysis;

namespace OpenCorneroids.Content.Vfs;

/// <summary>
/// A pack backed by a fixed index of content path → physical file. Only indexed files can be opened, so paths
/// taken from mod XML can never escape the pack, and nothing is ever opened for writing.
/// </summary>
public sealed class IndexedSource : IContentSource
{
    readonly Dictionary<string, string> _files = new(ContentPath.Comparer);

    public IndexedSource(string packId, IEnumerable<KeyValuePair<string, string>> files)
    {
        PackId = packId;
        foreach (var (path, physical) in files)
            _files[ContentPath.Normalize(path)] = physical;
    }

    /// <summary>Indexes every file under <paramref name="root"/>, mounted at <paramref name="mountAt"/>.</summary>
    public static IndexedSource FromFolder(string packId, string root, string mountAt = "")
    {
        var files = new List<KeyValuePair<string, string>>();
        foreach (var file in System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.EndsWith(".deploy", StringComparison.OrdinalIgnoreCase))
                relative = relative[..^".deploy".Length];   // ClickOnce installer layout (1.0.1.2)
            files.Add(new(ContentPath.Combine(mountAt, relative), file));
        }
        return new IndexedSource(packId, files);
    }

    public string PackId { get; }

    public IReadOnlyCollection<string> Paths => _files.Keys;

    public bool Exists(string path) => _files.ContainsKey(ContentPath.Normalize(path));

    public string? Locate(string path) => _files.GetValueOrDefault(ContentPath.Normalize(path));

    public bool TryOpen(string path, [NotNullWhen(true)] out Stream? stream)
    {
        stream = Locate(path) is { } physical
            ? new FileStream(physical, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
        return stream is not null;
    }

    public IEnumerable<string> List(string directory, string extension)
    {
        var dir = ContentPath.Normalize(directory);
        return _files.Keys
            .Where(p => ContentPath.Comparer.Equals(ContentPath.Directory(p), dir)
                        && p.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Order(ContentPath.Comparer);
    }
}
