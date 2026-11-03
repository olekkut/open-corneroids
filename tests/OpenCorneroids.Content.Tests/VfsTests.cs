using OpenCorneroids.Content.Tests.Support;
using OpenCorneroids.Content.Vfs;

namespace OpenCorneroids.Content.Tests;

public class VfsTests
{
    [Theory]
    [InlineData(@"Textures\Blocks\blockTextures.png", "Textures/Blocks/blockTextures.png")]
    [InlineData("/Textures//Blocks/./x.png", "Textures/Blocks/x.png")]
    [InlineData("", "")]
    public void Paths_normalize_to_forward_slashes(string input, string expected) =>
        Assert.Equal(expected, ContentPath.Normalize(input));

    [Fact]
    public void Folder_sources_ignore_case_and_strip_ClickOnce_deploy_suffixes()
    {
        using var tree = new TempTree();
        tree.Write("Content/Blocksets/defaultItemset.xml.deploy", "<itemset/>");
        tree.Write("Content/Textures/Blocks/BlockTextures.PNG", "png");

        var source = IndexedSource.FromFolder("pack", tree.At("Content"));

        Assert.True(source.Exists("blocksets/DEFAULTITEMSET.XML"));
        Assert.True(source.Exists(@"Textures\Blocks\blockTextures.png"));
        Assert.False(source.Exists("Textures/Blocks/../../../secret.txt"));
        Assert.Equal(["Blocksets/defaultItemset.xml"], source.List("Blocksets", ".xml"));
        Assert.True(source.TryOpen("blocksets/defaultitemset.xml", out var stream));
        using (stream) Assert.Equal("<itemset/>", new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public void Layered_sources_serve_the_top_layer_first_and_merge_listings()
    {
        var mod = new MemorySource("mod").Add("Blocksets/mod.xml", "mod").Add("Textures/a.png", "mod");
        var game = new MemorySource("game").Add("Blocksets/default.xml", "game").Add("textures/A.PNG", "game");
        var view = new LayeredSource([mod, game]);

        Assert.Same(mod, view.Resolve("Textures/a.png"));
        Assert.Same(game, view.Resolve("Blocksets/default.xml"));
        Assert.Null(view.Resolve("missing.xml"));
        Assert.Equal(["Blocksets/default.xml", "Blocksets/mod.xml"], view.List("Blocksets", ".xml").Order());
        Assert.Equal("mod > game", view.PackId);
    }
}
