using System.Xml;
using System.Xml.Linq;
using OpenCorneroids.Content.Vfs;

namespace OpenCorneroids.Content.Xml;

/// <summary>The one way content XML is read. Files come from players' downloads, so no DTDs or entities.</summary>
internal static class XmlInput
{
    static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
        MaxCharactersInDocument = 16 * 1024 * 1024,
    };

    /// <summary>Copes with UTF-8 with and without a BOM; keeps line numbers for diagnostics.</summary>
    public static XDocument Load(Stream stream)
    {
        using var reader = XmlReader.Create(stream, Settings);
        return XDocument.Load(reader, LoadOptions.SetLineInfo);
    }

    public static XmlReader CreateReader(Stream stream) => XmlReader.Create(stream, Settings);

    public static XDocument? TryLoad(IContentSource source, string path, DiagnosticBag diagnostics)
    {
        if (!source.TryOpen(path, out var stream))
        {
            diagnostics.Report(Severity.Error, Codes.FileMissing, "file not found", path);
            return null;
        }
        try
        {
            using (stream) return Load(stream);
        }
        catch (XmlException e)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, Codes.XmlMalformed, e.Message, path, e.LineNumber > 0 ? e.LineNumber : null));
            return null;
        }
    }
}
