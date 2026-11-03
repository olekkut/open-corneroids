using System.Diagnostics.CodeAnalysis;

namespace OpenCorneroids.Content.Vfs;

/// <summary>Read-only view of one pack's content (or a stack of packs). Paths are content paths, see <see cref="ContentPath"/>.</summary>
public interface IContentSource
{
    string PackId { get; }

    bool Exists(string path);

    bool TryOpen(string path, [NotNullWhen(true)] out Stream? stream);

    /// <summary>Files directly inside <paramref name="directory"/> ending in <paramref name="extension"/> (e.g. ".xml").</summary>
    IEnumerable<string> List(string directory, string extension);

    /// <summary>Physical location of a file, for diagnostics; null if absent.</summary>
    string? Locate(string path);
}
