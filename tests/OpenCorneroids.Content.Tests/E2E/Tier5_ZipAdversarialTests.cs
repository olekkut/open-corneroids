using System.IO.Compression;
using System.Text;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Tests.Support;
using OpenCorneroids.Content.Vfs;
using Xunit;

namespace OpenCorneroids.Content.Tests.E2E;

/// <summary>
/// Tier 5 White-Box Adversarial Tests for Milestone 2 (ZipSource & PackScanner).
/// Exercises:
/// 1. Empty zips and directory-only zip archives
/// 2. ClickOnce .deploy extension stripping and GameInstall detection
/// 3. Loose texture redirection (both flat and target paths)
/// 4. Windows backslash archive entry normalization and fallback matching
/// 5. Directory shadowing edge cases (casing, hint folders, across directories)
/// 6. Deep content nesting boundaries (MaxContentSearchDepth = 5)
/// 7. Multi-threaded concurrent reads and stream independence
/// 8. Corrupt entry payload resilience
/// Purely in-memory / isolated temporary tree fixtures. NEVER touches disk or user data.
/// </summary>
public sealed class Tier5_ZipAdversarialTests
{
    // =========================================================================
    // 1. Empty and Directory-Only Zip Archives
    // =========================================================================

    [Fact]
    public void DetectZip_CompletelyEmptyArchive_ReturnsNullWithDiagnostic()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("empty.zip");
        // Create valid empty zip (0 entries)
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }

        var diag = new DiagnosticBag();
        var pack = PackScanner.DetectZip(zipPath, "EmptyPack", diag);

        Assert.Null(pack);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized && d.Message.Contains("empty zip archive"));
    }

    [Fact]
    public void DetectZip_DirectoriesOnlyInArchive_ReturnsNullWithDiagnostic()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("dirsonly.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("Content/");
            archive.CreateEntry("Content/Blocksets/");
            archive.CreateEntry("Content/Textures/");
        }

        var diag = new DiagnosticBag();
        var pack = PackScanner.DetectZip(zipPath, "DirsOnlyPack", diag);

        Assert.Null(pack);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    [Fact]
    public void ZipSource_FromEmptyArchive_ProvidesSafeEmptyOperations()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("empty_source.zip");
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }

        var source = ZipSource.FromArchive("empty-source", zipPath);

        Assert.Equal("empty-source", source.PackId);
        Assert.Empty(source.Paths);
        Assert.False(source.Exists("anything.xml"));
        Assert.False(source.Exists(null!));
        Assert.False(source.Exists(""));
        Assert.False(source.TryOpen("anything.xml", out var s));
        Assert.Null(s);
        Assert.Null(source.Locate("anything.xml"));
        Assert.Null(source.Locate(null!));
        Assert.Empty(source.List("", ""));
        Assert.Empty(source.List("Blocksets", ".xml"));
    }

    // =========================================================================
    // 2. ClickOnce .deploy Extension Stripping & GameInstall Detection
    // =========================================================================

    [Fact]
    public void ZipSource_EntriesWithDeployExtension_StrippedCleanly()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("deploy_pack.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e1 = archive.CreateEntry("Content/Blocksets/custom.xml.deploy");
            using (var w = new StreamWriter(e1.Open()))
                w.Write("<custom_itemset />");

            var e2 = archive.CreateEntry("Content/Textures/Blocks/blockTextures.png.deploy");
            using (var s = e2.Open())
                s.Write([1, 2, 3, 4]);
        }

        var source = ZipSource.FromArchive("deploy-pack", zipPath, "Content");

        // The .deploy suffix must be stripped from public content paths
        Assert.True(source.Exists("Blocksets/custom.xml"));
        Assert.True(source.Exists("Textures/Blocks/blockTextures.png"));
        Assert.False(source.Exists("Blocksets/custom.xml.deploy"));

        // Must open successfully and return content
        Assert.True(source.TryOpen("Blocksets/custom.xml", out var s1));
        using (s1)
        {
            using var reader = new StreamReader(s1!);
            Assert.Equal("<custom_itemset />", reader.ReadToEnd());
        }

        Assert.True(source.TryOpen("Textures/Blocks/blockTextures.png", out var s2));
        using (s2)
        {
            Assert.Equal(4, s2!.Length);
        }
    }

    [Fact]
    public void PackScanner_DetectZip_GameInstallWithDeployExe_DetectsAsGameInstall()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("vanilla_deploy.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // ClickOnce packaged Corneroids.exe.deploy
            var exeEntry = archive.CreateEntry("Corneroids.exe.deploy");
            using (var s = exeEntry.Open()) s.Write([0x4D, 0x5A]);

            var itemset = archive.CreateEntry("Content/Blocksets/defaultItemset.xml");
            using (var w = new StreamWriter(itemset.Open()))
                w.Write(MiniItemset.Xml());
        }

        var diag = new DiagnosticBag();
        var pack = PackScanner.DetectZip(zipPath, "VanillaDeploy", diag);

        Assert.NotNull(pack);
        Assert.Equal(PackKind.GameInstall, pack.Kind);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void ZipSource_FlatRootWithDeployExtension_StrippedCleanly()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("flat_deploy.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Blocksets/flatItemset.xml.deploy");
            using (var w = new StreamWriter(e.Open()))
                w.Write("<flat_deploy/>");
        }

        var source = ZipSource.FromArchive("flat-deploy", zipPath, "");

        Assert.True(source.Exists("Blocksets/flatItemset.xml"));
        Assert.True(source.TryOpen("Blocksets/flatItemset.xml", out var stream));
        using (stream)
        {
            using var r = new StreamReader(stream!);
            Assert.Equal("<flat_deploy/>", r.ReadToEnd());
        }
    }

    // =========================================================================
    // 3. Loose Texture Pack Targets & Redirection
    // =========================================================================

    [Fact]
    public void ZipSource_LooseTextureTargets_MappedToBothFlatAndTargetPaths()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("loose_textures.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e1 = archive.CreateEntry("blockTextures.png");
            using (var s = e1.Open()) s.Write([10, 11, 12]);

            var e2 = archive.CreateEntry("lightTextureAtlas.png");
            using (var s = e2.Open()) s.Write([20, 21]);

            var e3 = archive.CreateEntry("spriteImages.png");
            using (var s = e3.Open()) s.Write([30, 31, 32, 33]);
        }

        // Mount flat (prefix "")
        var source = ZipSource.FromArchive("loose-source", zipPath, "");

        // Verify flat path access
        Assert.True(source.Exists("blockTextures.png"));
        Assert.True(source.Exists("lightTextureAtlas.png"));
        Assert.True(source.Exists("spriteImages.png"));

        // Verify virtual redirected content path access
        Assert.True(source.Exists("Textures/Blocks/blockTextures.png"));
        Assert.True(source.Exists("Textures/Blocks/lightTextureAtlas.png"));
        Assert.True(source.Exists("Textures/Sprites/spriteImages.png"));

        // Verify TryOpen on redirected content paths opens proper data
        Assert.True(source.TryOpen("Textures/Blocks/blockTextures.png", out var s1));
        using (s1) Assert.Equal(3, s1!.Length);

        Assert.True(source.TryOpen("Textures/Blocks/lightTextureAtlas.png", out var s2));
        using (s2) Assert.Equal(2, s2!.Length);

        Assert.True(source.TryOpen("Textures/Sprites/spriteImages.png", out var s3));
        using (s3) Assert.Equal(4, s3!.Length);
    }

    [Fact]
    public void PackScanner_DetectZip_LooseTexturePack_DiscoversPackKindTexturePack()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("loose_pack.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("blockTextures.png");
            using var s = e.Open();
            s.Write([1, 2, 3]);
        }

        var diag = new DiagnosticBag();
        var pack = PackScanner.DetectZip(zipPath, "LoosePack", diag);

        Assert.NotNull(pack);
        Assert.Equal(PackKind.TexturePack, pack.Kind);
        Assert.Empty(pack.ItemsetPaths);
        Assert.True(pack.Source.Exists("Textures/Blocks/blockTextures.png"));
        Assert.True(pack.Source.Exists("blockTextures.png"));
    }

    // =========================================================================
    // 4. Windows Backslash Normalization & Fallback Matching
    // =========================================================================

    [Fact]
    public void ZipSource_WindowsBackslashedArchiveEntryNames_IndexedAndOpenedCorrectly()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("windows_slashes.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Specifically create zip entries containing literal backslashes
            var e1 = archive.CreateEntry(@"Content\Blocksets\win_itemset.xml");
            using (var w = new StreamWriter(e1.Open()))
                w.Write("<windows_slashed_itemset />");

            var e2 = archive.CreateEntry(@"Content\Textures\Blocks\win_tex.png");
            using (var s = e2.Open())
                s.Write([99, 100]);
        }

        var source = ZipSource.FromArchive("win-pack", zipPath, "Content");

        // Forward slash query
        Assert.True(source.Exists("Blocksets/win_itemset.xml"));
        Assert.True(source.TryOpen("Blocksets/win_itemset.xml", out var s1));
        using (s1)
        {
            using var r = new StreamReader(s1!);
            Assert.Equal("<windows_slashed_itemset />", r.ReadToEnd());
        }

        // Backslash query
        Assert.True(source.Exists(@"Blocksets\win_itemset.xml"));
        Assert.True(source.TryOpen(@"Blocksets\win_itemset.xml", out var s2));
        using (s2)
        {
            using var r = new StreamReader(s2!);
            Assert.Equal("<windows_slashed_itemset />", r.ReadToEnd());
        }

        // List verification
        var list = source.List("Blocksets", ".xml").ToList();
        Assert.Single(list);
        Assert.Equal("Blocksets/win_itemset.xml", list[0]);
    }

    // =========================================================================
    // 5. Directory Shadowing Edge Cases
    // =========================================================================

    [Fact]
    public void PackScanner_DirectoryShadowing_WithCasingDifferences_ShadowsArchive()
    {
        using var tree = new TempTree();
        // Uncompressed folder: "MyCoolMod"
        tree.Write("root/MyCoolMod/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        // Archive: "mycoolmod.zip"
        var zipPath = tree.At("root/mycoolmod.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/Blocksets/defaultItemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write(MiniItemset.Xml());
        }

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.At("root")], diag);

        // Exactly one pack must be returned: the directory pack
        var pack = Assert.Single(packs);
        Assert.Equal("MyCoolMod", pack.Id);
        Assert.DoesNotContain(".zip", pack.RootPath, StringComparison.OrdinalIgnoreCase);

        // Warning diagnostic emitted
        Assert.Contains(diag, d => d.Code == "pack-shadowed" && d.Severity == Severity.Warning);
    }

    [Fact]
    public void PackScanner_DirectoryAndArchiveInDifferentFolders_BothDiscoveredWithDisambiguation()
    {
        using var tree = new TempTree();
        // Folder pack in root: "SharedName"
        tree.Write("root/SharedName/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());

        // Archive in hint folder "mods": "SharedName.zip"
        // Hint folder archives are checked against hint folder directories, NOT root directories
        var zipPath = tree.At("root/mods/SharedName.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/Blocksets/defaultItemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write(MiniItemset.Xml());
        }

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.At("root")], diag);

        // Both should be discovered, second one disambiguated with (2)
        Assert.Equal(2, packs.Count);
        Assert.Contains(packs, p => p.Id == "SharedName");
        Assert.Contains(packs, p => p.Id == "SharedName (2)");
    }

    // =========================================================================
    // 6. Deep Content Nesting Boundaries (MaxContentSearchDepth = 5)
    // =========================================================================

    [Fact]
    public void PackScanner_ContentAtDepth5_DiscoveredSuccessfully()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("depth5.zip");

        // 5 levels of nesting before Content: d1/d2/d3/d4/d5/Content/Blocksets/defaultItemset.xml
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("d1/d2/d3/d4/d5/Content/Blocksets/defaultItemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write(MiniItemset.Xml());
        }

        var diag = new DiagnosticBag();
        var pack = PackScanner.DetectZip(zipPath, "Depth5Pack", diag);

        Assert.NotNull(pack);
        Assert.Equal(PackKind.ItemsetMod, pack.Kind);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void PackScanner_ContentAtDepth6_ExceedsMaxSearchDepth_IgnoredWithDiagnostic()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("depth6.zip");

        // 6 levels of nesting before Content: d1/d2/d3/d4/d5/d6/Content/Blocksets/defaultItemset.xml
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("d1/d2/d3/d4/d5/d6/Content/Blocksets/defaultItemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write(MiniItemset.Xml());
        }

        var diag = new DiagnosticBag();
        var pack = PackScanner.DetectZip(zipPath, "Depth6Pack", diag);

        Assert.Null(pack);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    // =========================================================================
    // 7. Multi-Threaded Concurrent Reads and Stream Independence
    // =========================================================================

    [Fact]
    public async Task ZipSource_ConcurrentTryOpen_YieldsIndependentMemoryStreams()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("concurrent_test.zip");

        var testData = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog. 1234567890!");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/data.txt");
            using var s = e.Open();
            s.Write(testData);
        }

        var source = ZipSource.FromArchive("conc-pack", zipPath, "Content");

        const int threadCount = 32;
        var tasks = Enumerable.Range(0, threadCount).Select(threadId => Task.Run(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                bool ok = source.TryOpen("data.txt", out var stream);
                Assert.True(ok);
                Assert.NotNull(stream);

                using (stream)
                {
                    // Seek to arbitrary position and read
                    stream.Position = 10;
                    byte[] buf = new byte[5];
                    int read = stream.Read(buf, 0, 5);
                    Assert.Equal(5, read);
                    Assert.Equal("brown", Encoding.UTF8.GetString(buf));

                    // Rewind to 0 and verify full content
                    stream.Position = 0;
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    Assert.Equal("The quick brown fox jumps over the lazy dog. 1234567890!", reader.ReadToEnd());
                }
            }
        }));

        await Task.WhenAll(tasks);
    }

    // =========================================================================
    // 8. Corrupt Entry Payload Resilience
    // =========================================================================

    [Fact]
    public void ZipSource_CorruptEntryPayload_ReturnsFalseWithoutThrowingException()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("corrupted_entry.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/corrupt.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("Initial valid content");
        }

        var source = ZipSource.FromArchive("corrupt-pack", zipPath, "Content");
        Assert.True(source.Exists("corrupt.txt"));

        // Maliciously corrupt the compressed stream bytes in the zip file
        var bytes = File.ReadAllBytes(zipPath);
        for (int i = 20; i < Math.Min(bytes.Length - 20, 60); i++)
        {
            bytes[i] ^= 0x55;
        }
        File.WriteAllBytes(zipPath, bytes);

        // TryOpen must return false and not throw IOException / InvalidDataException
        bool opened = source.TryOpen("corrupt.txt", out var stream);
        Assert.False(opened);
        Assert.Null(stream);
    }
}
