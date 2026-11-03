using System.Diagnostics.CodeAnalysis;

namespace OpenCorneroids.Content.Vfs;

/// <summary>A stack of packs, top layer first; the first layer that has a file wins.</summary>
public sealed class LayeredSource : IContentSource
{
    public LayeredSource(IEnumerable<IContentSource> layersTopFirst) => Layers = layersTopFirst.ToArray();

    public IReadOnlyList<IContentSource> Layers { get; }

    public string PackId => string.Join(" > ", Layers.Select(l => l.PackId));

    /// <summary>The layer that serves <paramref name="path"/>, or null.</summary>
    public IContentSource? Resolve(string path) => Layers.FirstOrDefault(l => l.Exists(path));

    public bool Exists(string path) => Resolve(path) is not null;

    public string? Locate(string path) => Resolve(path)?.Locate(path);

    public bool TryOpen(string path, [NotNullWhen(true)] out Stream? stream)
    {
        if (Resolve(path) is { } layer)
            return layer.TryOpen(path, out stream);
        stream = null;
        return false;
    }

    public IEnumerable<string> List(string directory, string extension) =>
        Layers.SelectMany(l => l.List(directory, extension)).Distinct(ContentPath.Comparer);
}
