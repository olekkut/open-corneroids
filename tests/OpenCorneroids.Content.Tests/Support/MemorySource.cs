using System.Diagnostics.CodeAnalysis;
using System.Text;
using OpenCorneroids.Content.Vfs;

namespace OpenCorneroids.Content.Tests.Support;

/// <summary>In-memory pack for tests that don't need real folders.</summary>
sealed class MemorySource(string packId) : IContentSource
{
    readonly Dictionary<string, byte[]> _files = new(ContentPath.Comparer);

    public string PackId { get; } = packId;

    public MemorySource Add(string path, string text, bool bom = false)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return Add(path, bom ? [.. Encoding.UTF8.Preamble, .. body] : body);
    }

    public MemorySource Add(string path, byte[] bytes)
    {
        _files[ContentPath.Normalize(path)] = bytes;
        return this;
    }

    public bool Exists(string path) => _files.ContainsKey(ContentPath.Normalize(path));

    public string? Locate(string path) => Exists(path) ? $"memory://{PackId}/{ContentPath.Normalize(path)}" : null;

    public bool TryOpen(string path, [NotNullWhen(true)] out Stream? stream)
    {
        stream = _files.TryGetValue(ContentPath.Normalize(path), out var bytes) ? new MemoryStream(bytes, writable: false) : null;
        return stream is not null;
    }

    public IEnumerable<string> List(string directory, string extension) =>
        _files.Keys.Where(p => ContentPath.Comparer.Equals(ContentPath.Directory(p), ContentPath.Normalize(directory))
                               && p.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
