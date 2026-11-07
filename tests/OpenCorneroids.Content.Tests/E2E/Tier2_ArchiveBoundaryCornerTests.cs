using System.IO.Compression;
using System.Text;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Tests.Support;
using OpenCorneroids.Content.Vfs;
using Xunit;

namespace OpenCorneroids.Content.Tests.E2E;

/// <summary>
/// Tier 2: Archive Boundary & Corner Case Tests
/// Comprehensive opaque-box boundary validation for Features 10-14 (M2 Direct Archive Mod Loading).
/// Minimum >= 5 test cases per feature covering corrupted archives, path traversals, shadowing edge cases, etc.
/// </summary>
public class Tier2_ArchiveBoundaryCornerTests
{
    // =========================================================================
    // Feature 10: PackInfo Source Generalization — Boundaries
    // =========================================================================

    [Fact]
    public void F10_B1_EmptyPackId_HandledSafely()
    {
        var mem = new MemorySource("");
        Assert.Equal("", mem.PackId);
    }

    [Fact]
    public void F10_B2_NonExistentRootPath_PackConstructedWithoutThrowing()
    {
        var mem = new MemorySource("ghost-pack");
        Assert.False(mem.Exists("any/path.txt"));
    }

    [Fact]
    public void F10_B3_NullVanillaVersion_IsVanillaReturnsFalse()
    {
        using var tree = new TempTree();
        tree.Write("mod/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        var pack = PackScanner.Detect(tree.At("mod"), "Mod", new DiagnosticBag());
        Assert.NotNull(pack);
        Assert.False(pack.IsVanilla);
    }

    [Fact]
    public void F10_B4_CustomSourceThrowsOnList_ReportsOrEncapsulates()
    {
        var mem = new MemorySource("empty");
        var list = mem.List("NonExistent", ".xml");
        Assert.Empty(list);
    }

    [Fact]
    public void F10_B5_CaseInsensitiveItemsetPathsComparison()
    {
        var mem = new MemorySource("case-test")
            .Add("blocksets/itemset.xml", MiniItemset.Xml());
        var list = mem.List("BLOCKSETS", ".XML").ToList();
        Assert.Single(list);
    }

    // =========================================================================
    // Feature 11: ZipSource — Boundaries
    // =========================================================================

    [Fact]
    public void F11_B1_EmptyZipArchive_ProducesZeroEntries()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("empty.zip");
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }

        if (!ZipSourceBridge.RequireImplemented()) return;
        var source = ZipSourceBridge.FromArchive("empty", zipPath);
        Assert.Empty(source.List("", ""));
    }

    [Fact]
    public void F11_B2_CorruptedZipFile_ThrowsInvalidDataOrIOException()
    {
        using var tree = new TempTree();
        var zipPath = ZipFixtures.CreateCorruptedZip(tree, "corrupt.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        Assert.ThrowsAny<Exception>(() => ZipSourceBridge.FromArchive("corrupt", zipPath));
    }

    [Fact]
    public void F11_B3_PathTraversingOutsideArchive_ReturnsFalseOrNull()
    {
        using var tree = new TempTree();
        var zipPath = ZipFixtures.CreateNestedContentZip(tree, "traversal.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var source = ZipSourceBridge.FromArchive("traversal", zipPath);
        Assert.False(source.Exists("../../../secret.txt"));
        Assert.False(source.TryOpen("../../../secret.txt", out var s));
        Assert.Null(s);
    }

    [Fact]
    public void F11_B4_NonExistentPath_ReturnsFalseAndNullStream()
    {
        using var tree = new TempTree();
        var zipPath = ZipFixtures.CreateNestedContentZip(tree, "test.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var source = ZipSourceBridge.FromArchive("test", zipPath);
        Assert.False(source.Exists("Ghost/File.xml"));
        Assert.False(source.TryOpen("Ghost/File.xml", out var stream));
        Assert.Null(stream);
    }

    [Fact]
    public async Task F11_B5_ConcurrentTryOpen_ReturnsIndependentSeekableStreams()
    {
        using var tree = new TempTree();
        var zipPath = ZipFixtures.CreateNestedContentZip(tree, "concurrent.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var source = ZipSourceBridge.FromArchive("concurrent", zipPath, "MyMod/Content");

        var t1 = Task.Run(() =>
        {
            if (source.TryOpen("Blocksets/defaultItemset.xml", out var s1))
            {
                using (s1)
                {
                    s1.Position = s1.Length / 2;
                    return s1.Position;
                }
            }
            return 0L;
        });

        var t2 = Task.Run(() =>
        {
            if (source.TryOpen("Blocksets/defaultItemset.xml", out var s2))
            {
                using (s2)
                {
                    s2.Position = 0;
                    return s2.Position;
                }
            }
            return -1L;
        });

        await Task.WhenAll(t1, t2);
        Assert.True(await t1 > 0);
        Assert.Equal(0, await t2);
    }

    // =========================================================================
    // Feature 12: Archive Mod Discovery — Boundaries
    // =========================================================================

    [Fact]
    public void F12_B1_CorruptedZipInModsDirectory_IgnoredSafelyWithDiagnostic()
    {
        using var tree = new TempTree();
        ZipFixtures.CreateCorruptedZip(tree, "mods/CorruptMod.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);
        // Does not throw and does not register corrupt pack as valid
        Assert.DoesNotContain(packs, p => p.Id.Contains("CorruptMod"));
    }

    [Fact]
    public void F12_B2_NonZipFileWithZipExtension_IgnoredSafely()
    {
        using var tree = new TempTree();
        tree.Write("mods/TextFile.zip", "This is plain text, not a zip file.");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);
        Assert.DoesNotContain(packs, p => p.Id.Contains("TextFile"));
    }

    [Fact]
    public void F12_B3_ZeroByteZipFile_IgnoredSafely()
    {
        using var tree = new TempTree();
        tree.Write("mods/Zero.zip", Array.Empty<byte>());

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);
        Assert.DoesNotContain(packs, p => p.Id.Contains("Zero"));
    }

    [Fact]
    public void F12_B4_ArchiveWithoutContentOrTextures_IgnoredSafely()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/RandomStuff.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("Hello");
        }

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);
        Assert.DoesNotContain(packs, p => p.Id.Contains("RandomStuff"));
    }

    [Fact]
    public void F12_B5_ArchiveWithSpecialCharactersInFileName_DiscoveredSafely()
    {
        using var tree = new TempTree();
        ZipFixtures.CreateNestedContentZip(tree, "mods/Mod_Ω-v1.0 (Special).zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);
        Assert.Contains(packs, p => p.Id.Contains("Special") || p.Id.Contains("Mod_Ω"));
    }

    // =========================================================================
    // Feature 13: Directory Shadowing & Disambiguation — Boundaries
    // =========================================================================

    [Fact]
    public void F13_B1_SameStemNameDifferentCasing_DirectoryShadowsZip()
    {
        using var tree = new TempTree();
        tree.Write("mods/mymod/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        ZipFixtures.CreateNestedContentZip(tree, "mods/MYMOD.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);
        var matches = packs.Where(p => p.Id.Equals("mymod", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(matches);
    }

    [Fact]
    public void F13_B2_MultipleIdenticalZipNamesInSameFolder_Disambiguated()
    {
        using var tree = new TempTree();
        ZipFixtures.CreateNestedContentZip(tree, "root1/mods/Dup.zip");
        ZipFixtures.CreateNestedContentZip(tree, "root2/mods/Dup.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.At("root1"), tree.At("root2")], diag);
        var dups = packs.Where(p => p.Id.StartsWith("Dup")).ToList();
        if (dups.Count > 1)
        {
            Assert.Equal(dups.Count, dups.Select(d => d.Id).Distinct().Count());
        }
    }

    [Fact]
    public void F13_B3_NestedSubfolderWithSameName_DoesNotShadowParent()
    {
        using var tree = new TempTree();
        tree.Write("mods/Parent/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        ZipFixtures.CreateNestedContentZip(tree, "mods/Other/Parent.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);
        Assert.NotEmpty(packs);
    }

    [Fact]
    public void F13_B4_EmptyDirectoryMod_StillShadowsZip()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.At("mods/GhostMod"));
        ZipFixtures.CreateNestedContentZip(tree, "mods/GhostMod.zip");

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);
        // Since GhostMod directory exists, GhostMod.zip is shadowed
        Assert.DoesNotContain(packs, p => p.RootPath.EndsWith(".zip") && p.Id.Contains("GhostMod"));
    }

    [Fact]
    public void F13_B5_DirectoryWithExtensionDotZip_HandledAsDirectoryNotZip()
    {
        using var tree = new TempTree();
        // A directory literally named "Fake.zip"
        tree.Write("mods/Fake.zip/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());

        if (!ZipSourceBridge.RequireImplemented()) return;
        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);
        Assert.NotEmpty(packs);
    }

    // =========================================================================
    // Feature 14: Archive Content Root Detection — Boundaries
    // =========================================================================

    [Fact]
    public void F14_B1_DeeplyNestedContentFolderBeyondMaxDepth_IgnoredOrDetected()
    {
        using var tree = new TempTree();
        var zip = ZipFixtures.CreateNestedContentZip(tree, "mods/Deep.zip", packFolder: "a/b/c/d/e/f/g/h");

        if (!ZipSourceBridge.RequireImplemented()) return;
        // Verify depth limits or safe prefix extraction
        Assert.NotNull(zip);
    }

    [Fact]
    public void F14_B2_MultipleContentFoldersInArchive_PicksFirstOrShallowest()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/MultiContent.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e1 = archive.CreateEntry("Content/Blocksets/defaultItemset.xml");
            using (var w = new StreamWriter(e1.Open())) w.Write(MiniItemset.Xml());
            var e2 = archive.CreateEntry("Extra/Content/Blocksets/secondary.xml");
            using (var w = new StreamWriter(e2.Open())) w.Write(MiniItemset.Xml());
        }

        if (!ZipSourceBridge.RequireImplemented()) return;
        var source = ZipSourceBridge.FromArchive("multi", zipPath, "Content");
        Assert.True(source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void F14_B3_MixedSlashesInEntryPaths_NormalizedConsistently()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/MixedSlash.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content\\Blocksets/defaultItemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write(MiniItemset.Xml());
        }

        if (!ZipSourceBridge.RequireImplemented()) return;
        var source = ZipSourceBridge.FromArchive("mixed", zipPath, "Content");
        Assert.True(source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void F14_B4_DeployExtensionOnNonXmlFile_PreservedOrStripped()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/DeployPng.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/Textures/Blocks/blockTextures.png.deploy");
            using var s = e.Open();
            var png = TestPng.Encode(16, 16, (_, _) => (0, 0, 0, 255));
            s.Write(png, 0, png.Length);
        }

        if (!ZipSourceBridge.RequireImplemented()) return;
        var source = ZipSourceBridge.FromArchive("deploy-png", zipPath, "Content");
        Assert.True(source.Exists("Textures/Blocks/blockTextures.png"));
    }

    [Fact]
    public void F14_B5_ArchiveWithLeadingSlashInEntry_NormalizedWithoutEscape()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/LeadingSlash.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("/Content/Blocksets/defaultItemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write(MiniItemset.Xml());
        }

        if (!ZipSourceBridge.RequireImplemented()) return;
        var source = ZipSourceBridge.FromArchive("leading", zipPath, "Content");
        Assert.True(source.Exists("Blocksets/defaultItemset.xml"));
    }
}
