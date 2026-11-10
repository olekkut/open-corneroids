using System.Collections;
using System.Xml;
using System.Xml.Linq;

namespace OpenCorneroids.Content;

public enum Severity { Info, Warning, Error }

/// <summary>One problem found while loading content. <see cref="File"/> is a content path or a folder.</summary>
public sealed record Diagnostic(Severity Severity, string Code, string Message, string? File = null, int? Line = null)
{
    public override string ToString()
    {
        var where = File is null ? "" : Line is { } line ? $"{File}({line}): " : $"{File}: ";
        return $"{where}{Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
    }
}

public sealed class DiagnosticBag : IReadOnlyList<Diagnostic>
{
    readonly List<Diagnostic> _items = new();

    public int Count => _items.Count;
    public Diagnostic this[int index] => _items[index];
    public bool HasErrors => _items.Any(d => d.Severity == Severity.Error);

    public void Add(Diagnostic diagnostic) => _items.Add(diagnostic);

    public void Report(Severity severity, string code, string message, string? file = null, XObject? at = null) =>
        _items.Add(new Diagnostic(severity, code, message, file, LineOf(at)));

    public IEnumerable<Diagnostic> AtLeast(Severity severity) => _items.Where(d => d.Severity >= severity);

    public static int? LineOf(XObject? node) =>
        node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : null;

    public IEnumerator<Diagnostic> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Stable diagnostic codes, so tests and tools can match on them.</summary>
public static class Codes
{
    public const string FileMissing = "file-missing";
    public const string XmlMalformed = "xml-malformed";
    public const string UnexpectedRoot = "unexpected-root";
    public const string UnknownSection = "unknown-section";
    public const string UnknownCategory = "unknown-category";
    public const string UnknownField = "unknown-field";
    public const string MissingField = "missing-field";
    public const string BadNumber = "bad-number";
    public const string NonIntegral = "non-integral-number";
    public const string OutOfRange = "value-out-of-range";
    public const string BadEnum = "bad-enum";
    public const string DuplicateItemId = "duplicate-item-id";
    public const string DuplicateBlockId = "duplicate-block-id";
    public const string DuplicateProjectileId = "duplicate-projectile-id";
    public const string DanglingItemRef = "dangling-item-ref";
    public const string DanglingProjectileRef = "dangling-projectile-ref";
    public const string AtlasMissing = "atlas-missing";
    public const string TextureMissing = "texture-missing";
    public const string TextureUnreadable = "texture-unreadable";
    public const string AtlasGridMismatch = "atlas-grid-mismatch";
    public const string RegionOutOfGrid = "region-out-of-grid";
    public const string RegionEmpty = "region-empty";
    public const string RegionSizeDiffers = "region-size-differs";
    public const string PackUnrecognized = "pack-unrecognized";
    public const string NoBaseInstall = "no-base-install";
}
