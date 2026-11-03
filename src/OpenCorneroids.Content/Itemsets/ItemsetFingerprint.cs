using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace OpenCorneroids.Content.Itemsets;

/// <summary>
/// Content identity for an itemset. Three DarthNox releases share key "DarthNoxItemset" and version 5 with
/// 140, 162 and 196 items, so worlds bind to this instead.
/// </summary>
public static class ItemsetFingerprint
{
    /// <summary>
    /// SHA-256 over a canonical form of the XML: comments, whitespace, line endings, BOMs, attribute order and empty
    /// elements (such as <c>&lt;spotLights /&gt;</c> or <c>&lt;description /&gt;</c>) don't count. The v21 repack's copy
    /// of the vanilla itemset, which only adds an empty <c>&lt;spotLights /&gt;</c>, gets the vanilla fingerprint.
    /// </summary>
    public static string Compute(XDocument doc)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (doc.Root is { } root) Write(root, hash);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    static void Write(XElement element, IncrementalHash hash)
    {
        if (!element.HasAttributes && !element.HasElements && string.IsNullOrWhiteSpace(element.Value)) return;

        Append(hash, "<" + element.Name.LocalName);
        foreach (var attribute in element.Attributes().OrderBy(a => a.Name.LocalName, StringComparer.Ordinal))
            Append(hash, "\u0001" + attribute.Name.LocalName + "\u0002" + attribute.Value.Trim());
        Append(hash, ">");
        if (element.HasElements)
            foreach (var child in element.Elements()) Write(child, hash);
        else
            Append(hash, element.Value.Trim());
        Append(hash, "\u0003");
    }

    static void Append(IncrementalHash hash, string text) => hash.AppendData(Encoding.UTF8.GetBytes(text));
}
