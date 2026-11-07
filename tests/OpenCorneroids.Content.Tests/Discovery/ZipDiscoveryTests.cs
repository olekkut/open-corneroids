using System.IO.Compression;
using System.Text;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Tests.Support;
using Xunit;

namespace OpenCorneroids.Content.Tests.Discovery;

public class ZipDiscoveryTests
{
    static string CreateItemsetZip(string zipPath, string? prefix = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var itemsetEntryPath = prefix is null
            ? "Blocksets/defaultItemset.xml"
            : $"{prefix}/Blocksets/defaultItemset.xml";
        var entry = archive.CreateEntry(itemsetEntryPath);
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.Write(MiniItemset.Xml());
        }
        return zipPath;
    }

    static string CreateTextureZip(string zipPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("blockTextures.png");
        using var stream = entry.Open();
        var png = TestPng.Encode(16, 16, (_, _) => (0, 255, 0, 255));
        stream.Write(png, 0, png.Length);
        return zipPath;
    }

    [Fact]
    public void PackScanner_discovers_zip_in_mods_hint_folder()
    {
        using var tree = new TempTree();
        CreateItemsetZip(tree.At("mods/AlphaMod.zip"), prefix: "Alpha/Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("AlphaMod", pack.Id);
        Assert.Equal(PackKind.ItemsetMod, pack.Kind);
        Assert.EndsWith(".zip", pack.RootPath);
        Assert.Single(pack.ItemsetPaths);
    }

    [Fact]
    public void PackScanner_discovers_zip_in_texturepacks_hint_folder()
    {
        using var tree = new TempTree();
        CreateTextureZip(tree.At("texturepacks/CleanTextures.zip"));

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("CleanTextures", pack.Id);
        Assert.Equal(PackKind.TexturePack, pack.Kind);
        Assert.Empty(pack.ItemsetPaths);
        Assert.True(pack.Source.Exists("blockTextures.png"));
        Assert.True(pack.Source.Exists("Textures/Blocks/blockTextures.png"));
    }

    [Fact]
    public void PackScanner_discovers_flat_archive_layout()
    {
        using var tree = new TempTree();
        CreateItemsetZip(tree.At("mods/FlatMod.zip"), prefix: null);

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("FlatMod", pack.Id);
        Assert.Equal(PackKind.ItemsetMod, pack.Kind);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void PackScanner_discovers_nested_content_layout()
    {
        using var tree = new TempTree();
        CreateItemsetZip(tree.At("mods/NestedMod.zip"), prefix: "Deep/Custom/Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("NestedMod", pack.Id);
        Assert.Equal(PackKind.ItemsetMod, pack.Kind);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void Directory_shadows_zip_archive_with_same_stem_name()
    {
        using var tree = new TempTree();
        // Create directory mod
        tree.Write("mods/Coexist/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        // Create zip archive with same stem name
        CreateItemsetZip(tree.At("mods/Coexist.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("Coexist", pack.Id);
        Assert.False(pack.RootPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diag, d => d.Code == "pack-shadowed");
    }

    [Fact]
    public void Empty_directory_still_shadows_zip_archive()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.At("mods/EmptyDir"));
        CreateItemsetZip(tree.At("mods/EmptyDir.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        // Since EmptyDir shadows EmptyDir.zip, and EmptyDir has no content, no pack is registered
        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == "pack-shadowed");
    }

    [Fact]
    public void Shadowing_is_case_insensitive()
    {
        using var tree = new TempTree();
        tree.Write("mods/casemod/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        CreateItemsetZip(tree.At("mods/CASEMOD.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.False(pack.RootPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diag, d => d.Code == "pack-shadowed");
    }

    [Fact]
    public void Archives_in_different_folders_do_not_shadow()
    {
        using var tree = new TempTree();
        tree.Write("games/Shared/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        CreateItemsetZip(tree.At("mods/Shared.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Equal(2, packs.Count);
        Assert.Contains(packs, p => p.RootPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(packs, p => !p.RootPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Deduplication_assigns_unique_ids_for_distinct_archives()
    {
        using var tree = new TempTree();
        CreateItemsetZip(tree.At("root1/mods/DupPack.zip"), prefix: "Content");
        CreateItemsetZip(tree.At("root2/mods/DupPack.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.At("root1"), tree.At("root2")], diag);

        Assert.Equal(2, packs.Count);
        var ids = packs.Select(p => p.Id).Order().ToList();
        Assert.Equal(["DupPack", "DupPack (2)"], ids);
    }

    [Fact]
    public void Corrupted_or_invalid_zip_is_safely_ignored()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.At("mods"));
        File.WriteAllBytes(tree.At("mods/BadHeader.zip"), [0x50, 0x4B, 0x00, 0x00, 0x01, 0x02]);
        File.WriteAllBytes(tree.At("mods/ZeroByte.zip"), []);
        File.WriteAllText(tree.At("mods/NotAZip.zip"), "just plain text");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.True(diag.Count >= 1);
    }

    [Fact]
    public void Archive_without_content_or_textures_ignored_safely()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/JustDocs.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("manual.pdf");
            using var w = new StreamWriter(e.Open());
            w.Write("User Manual");
        }

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    [Fact]
    public void Special_characters_in_zip_filename_supported()
    {
        using var tree = new TempTree();
        CreateItemsetZip(tree.At("mods/Mod_Ω-v1.0 (Special).zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Contains("Special", pack.Id);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void ContentLibrary_loads_itemset_from_zip_archive()
    {
        using var tree = new TempTree();
        var opaque = TestPng.Atlas(4, 4, 8, (_, _) => 255);
        var spriteRegistry = """
            <spriteTextureAtlases>
              <atlas><name>Default</name><key>defaultsprites</key><texture>Textures/Sprites/spriteImages.png</texture><blockSize>8</blockSize></atlas>
            </spriteTextureAtlases>
            """;

        // Base game install
        tree.Write("games/Base/Corneroids.exe", "MZ");
        tree.Write("games/Base/Content/Blocksets/defaultItemset.xml",
            MiniItemset.Xml().Replace("miniblocks", "defaultblocks").Replace("minisprites", "defaultsprites"));
        tree.Write("games/Base/Content/Textures/Blocks/DefaultBlocks.xml", """
            <blockTextureAtlases>
              <atlas><name>Default</name><key>defaultblocks</key><texture>Textures/Blocks/blockTextures.png</texture><blockSize>8</blockSize></atlas>
            </blockTextureAtlases>
            """);
        tree.Write("games/Base/Content/Textures/Blocks/blockTextures.png", opaque);
        tree.Write("games/Base/Content/Textures/Sprites/SpriteTextureAtlases.xml", spriteRegistry);
        tree.Write("games/Base/Content/Textures/Sprites/spriteImages.png", opaque);

        // Mod zip with custom itemset referencing default atlases
        var zipPath = tree.At("mods/ZipMod.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/Blocksets/customItemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write(MiniItemset.Xml()
                .Replace("<key>MiniItemset</key>", "<key>ZipCustomItemset</key>")
                .Replace("miniblocks", "defaultblocks")
                .Replace("minisprites", "defaultsprites"));
        }

        var library = ContentLibrary.Scan([tree.Root], baseInstallId: "Base");
        Assert.Equal(2, library.Packs.Count);

        var customEntry = library.FindItemset("ZipCustomItemset", out var problem);
        Assert.Null(problem);
        Assert.NotNull(customEntry);

        var loaded = library.Load(customEntry!);
        Assert.Empty(loaded.Diagnostics.AtLeast(Severity.Warning));
        Assert.NotNull(loaded.Itemset);
        Assert.Equal("ZipCustomItemset", loaded.Itemset.Header.Key);
    }
}
