using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Content.Tests.Support;

namespace OpenCorneroids.Content.Tests;

public class FingerprintTests
{
    static string Fingerprint(string xml, bool bom = false) =>
        ItemsetParser.Parse(new MemorySource("test").Add(MiniItemset.Path, xml, bom), MiniItemset.Path, new DiagnosticBag())!.Fingerprint;

    [Fact]
    public void Formatting_comments_line_endings_bom_and_empty_elements_do_not_count()
    {
        var original = MiniItemset.Xml();
        var reformatted = original
            .Replace("\n", "\r\n")
            .Replace("<spotLights />", "<spotLights></spotLights><thrusters />")
            .Replace("<projectiles>", "<projectiles><!-- tuned -->")
            .Replace("""<color r="0" g="0" b="255" />""", """<color   b="255" r="0" g="0"/>""");

        Assert.Equal(Fingerprint(original), Fingerprint(reformatted, bom: true));
    }

    [Fact]
    public void Any_value_change_changes_the_fingerprint()
    {
        var original = MiniItemset.Xml();

        Assert.NotEqual(Fingerprint(original), Fingerprint(original.Replace("<damage>2</damage>", "<damage>3</damage>")));
        Assert.NotEqual(Fingerprint(original), Fingerprint(original.Replace("<name>Mini</name>", "<name>Mini 2</name>")));
    }
}
