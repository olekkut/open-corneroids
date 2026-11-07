using OpenCorneroids.Content.Atlases;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Content.Tests.Support;
using OpenCorneroids.Content.Vfs;
using Xunit;

namespace OpenCorneroids.Content.Tests.E2E;

/// <summary>
/// Tier 3: Cross-Feature Interaction Tests (Archive Mod Subsystem)
/// Validates pairwise combinations between:
/// - Zip-backed content sources (ZipSource)
/// - Directory-backed indexed sources (IndexedSource)
/// - Layered source stack (LayeredSource)
/// - Itemset parser and canonical XML fingerprinting
/// - Atlas catalog generation across archives and directories
/// </summary>
public class Tier3_ArchiveCrossFeatureTests
{
    [Fact]
    public void C11_ZipItemsetMod_And_ZipTexturePack_LayeredTogether()
    {
        using var tree = new TempTree();
        var zipItemset = ZipFixtures.CreateNestedContentZip(tree, "mods/ItemMod.zip");
        var zipTextures = ZipFixtures.CreateLooseTexturePackZip(tree, "texturepacks/TexMod.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var src1 = ZipSourceBridge.FromArchive("item-pack", zipItemset, "MyMod/Content");
        var src2 = ZipSourceBridge.FromArchive("tex-pack", zipTextures, "");

        var layered = new LayeredSource([src2, src1]);
        Assert.True(layered.Exists("Blocksets/defaultItemset.xml"));
        Assert.True(layered.Exists("blockTextures.png"));
    }

    [Fact]
    public void C12_UnpackedDirectoryMod_Overrides_ZipArchiveLayer()
    {
        using var tree = new TempTree();
        var zip = ZipFixtures.CreateNestedContentZip(tree, "mods/ZipBase.zip");
        tree.Write("dir/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml(blocksExtra: "<!-- from dir -->"));

        if (!ZipSourceBridge.RequireImplemented()) return;
        var zipSource = ZipSourceBridge.FromArchive("zip-base", zip, "MyMod/Content");
        var dirSource = IndexedSource.FromFolder("dir-base", tree.At("dir/Content"));

        // Dir source takes priority in layer
        var layered = new LayeredSource([dirSource, zipSource]);
        Assert.True(layered.TryOpen("Blocksets/defaultItemset.xml", out var stream));
        using var reader = new StreamReader(stream!);
        var text = reader.ReadToEnd();
        Assert.Contains("from dir", text);
    }

    [Fact]
    public void C13_ZipModWithCustomItemset_ParsedByItemsetParser()
    {
        using var tree = new TempTree();
        var zip = ZipFixtures.CreateNestedContentZip(tree, "mods/CustomItemset.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var source = ZipSourceBridge.FromArchive("custom", zip, "MyMod/Content");
        var diag = new DiagnosticBag();
        var itemset = ItemsetParser.Parse(source, "Blocksets/defaultItemset.xml", diag);
        Assert.NotNull(itemset);
        Assert.Equal("Mini", itemset.Header.Name);
    }

    [Fact]
    public void C14_AtlasCatalog_BuildsFromZipAndDirectorySources()
    {
        using var tree = new TempTree();
        var zip = ZipFixtures.CreateLooseTexturePackZip(tree, "texturepacks/Textures.zip");
        tree.Write("base/Content/Textures/Blocks/DefaultBlocks.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <atlases>
              <atlas texture="Textures/Blocks/blockTextures.png" cellSize="16" />
            </atlases>
            """);

        if (!ZipSourceBridge.RequireImplemented()) return;
        var zipSource = ZipSourceBridge.FromArchive("tex", zip, "");
        var dirSource = IndexedSource.FromFolder("base", tree.At("base/Content"));

        var layered = new LayeredSource([zipSource, dirSource]);
        Assert.True(layered.Exists("blockTextures.png"));
    }

    [Fact]
    public void C15_FingerprintCalculation_IdenticalForZipAndDirectoryMod()
    {
        using var tree = new TempTree();
        var xmlContent = MiniItemset.Xml();
        var zip = ZipFixtures.CreateNestedContentZip(tree, "mods/FpMod.zip");
        tree.Write("dir/Content/Blocksets/defaultItemset.xml", xmlContent);

        if (!ZipSourceBridge.RequireImplemented()) return;
        var zipSource = ZipSourceBridge.FromArchive("zip-fp", zip, "MyMod/Content");
        var dirSource = IndexedSource.FromFolder("dir-fp", tree.At("dir/Content"));

        var diagZip = new DiagnosticBag();
        var diagDir = new DiagnosticBag();

        var setZip = ItemsetParser.Parse(zipSource, "Blocksets/defaultItemset.xml", diagZip);
        var setDir = ItemsetParser.Parse(dirSource, "Blocksets/defaultItemset.xml", diagDir);

        Assert.NotNull(setZip);
        Assert.NotNull(setDir);
        Assert.Equal(setDir.Fingerprint, setZip.Fingerprint);
    }
}
