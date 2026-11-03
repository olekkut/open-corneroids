using System.Globalization;
using System.Numerics;
using System.Xml.Linq;

namespace OpenCorneroids.Content.Xml;

/// <summary>
/// Reads fields by name, never by position, and parses every number with the invariant culture: the data uses
/// '.' decimals ("0.2", "-2.5"), which a uk-UA or pl-PL default culture would misread. Bad values are reported
/// with their line and replaced by the fallback, so one typo doesn't sink a whole itemset.
/// </summary>
internal sealed class FieldReader(string file, DiagnosticBag diagnostics)
{
    readonly HashSet<string> _reportedOnce = new(StringComparer.Ordinal);

    public string File => file;

    public string Text(XElement parent, string name, string fallback = "") =>
        parent.Element(name)?.Value.Trim() ?? fallback;

    public int Int(XElement parent, string name, int fallback = 0) =>
        parent.Element(name) is { } e ? ParseInt(e.Value, e, fallback) : fallback;

    public float Float(XElement parent, string name, float fallback = 0) =>
        parent.Element(name) is { } e ? ParseFloat(e.Value, e, fallback) : fallback;

    public int IntAttr(XElement element, string name, int fallback = 0) =>
        element.Attribute(name) is { } a ? ParseInt(a.Value, a, fallback) : fallback;

    public float FloatAttr(XElement element, string name, float fallback = 0) =>
        element.Attribute(name) is { } a ? ParseFloat(a.Value, a, fallback) : fallback;

    /// <summary>Reports and returns null when the attribute is missing or not a number.</summary>
    public int? RequiredIntAttr(XElement element, string name)
    {
        if (element.Attribute(name) is not { } attribute)
        {
            Report(Severity.Error, Codes.MissingField, $"<{element.Name.LocalName}> has no {name} attribute; skipped", element);
            return null;
        }
        return TryParseInt(attribute.Value, attribute, out var value) ? value : null;
    }

    public Rgb8 Rgb(XElement? e) => e is null ? default : new(Byte(e, "r"), Byte(e, "g"), Byte(e, "b"));

    public CellRect Cell(XElement? e) =>
        e is null ? default : new(IntAttr(e, "x"), IntAttr(e, "y"), IntAttr(e, "w", 1), IntAttr(e, "h", 1));

    public Vector3 Xyz(XElement? e) =>
        e is null ? Vector3.Zero : new(FloatAttr(e, "x"), FloatAttr(e, "y"), FloatAttr(e, "z"));

    public Vector3 Whd(XElement? e, Vector3 fallback = default) =>
        e is null ? fallback : new(FloatAttr(e, "w"), FloatAttr(e, "h"), FloatAttr(e, "d"));

    public void Report(Severity severity, string code, string message, XObject? at) =>
        diagnostics.Report(severity, code, message, file, at);

    /// <summary>Reports only the first time <paramref name="key"/> is seen in this file.</summary>
    public void ReportOnce(string key, Severity severity, string code, string message, XObject? at)
    {
        if (_reportedOnce.Add(key)) Report(severity, code, message, at);
    }

    byte Byte(XElement e, string name) => (byte)Math.Clamp(IntAttr(e, name), 0, 255);

    int ParseInt(string text, XObject at, int fallback) => TryParseInt(text, at, out var value) ? value : fallback;

    bool TryParseInt(string text, XObject at, out int value)
    {
        var s = text.Trim();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && Math.Abs(d) < int.MaxValue)
        {
            value = (int)Math.Round(d);
            if (value != d) Report(Severity.Warning, Codes.NonIntegral, $"'{s}' is not a whole number; using {value}", at);
            return true;
        }
        Report(Severity.Warning, Codes.BadNumber, $"'{s}' is not a number", at);
        return false;
    }

    float ParseFloat(string text, XObject at, float fallback)
    {
        var s = text.Trim();
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
        Report(Severity.Warning, Codes.BadNumber, $"'{s}' is not a number; using {fallback.ToString(CultureInfo.InvariantCulture)}", at);
        return fallback;
    }
}
