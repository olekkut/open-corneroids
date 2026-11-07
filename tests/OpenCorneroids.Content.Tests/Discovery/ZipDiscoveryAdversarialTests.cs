using System.IO.Compression;
using System.Text;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Tests.Support;
using OpenCorneroids.Content.Vfs;
using Xunit;

namespace OpenCorneroids.Content.Tests.Discovery;

/// <summary>
/// Adversarial test harness for Milestone 2 (ZipSource and PackScanner).
/// Specifically challenges:
/// 1. Corrupted zip headers, truncated zip files, and non-zip files renamed .zip
/// 2. Zero-byte .zip files and empty zip archives
/// 3. Special characters, unicode names, and spaces in archive filenames
/// 4. Archive collision resolution: duplicate names across distinct roots, case-insensitive shadowing
/// 5. PackScanner ignoring invalid archives with diagnostics rather than crashing
/// </summary>
public class ZipDiscoveryAdversarialTests
{
    static string CreateValidItemsetZip(string zipPath, string prefix = "Content", string itemsetKey = "AdvItemset")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entryPath = string.IsNullOrEmpty(prefix)
            ? "Blocksets/defaultItemset.xml"
            : $"{prefix}/Blocksets/defaultItemset.xml";
        var entry = archive.CreateEntry(entryPath);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(MiniItemset.Xml().Replace("MiniItemset", itemsetKey));
        return zipPath;
    }

    static string CreateValidTextureZip(string zipPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("blockTextures.png");
        using var stream = entry.Open();
        var png = TestPng.Encode(16, 16, (_, _) => (0, 255, 128, 255));
        stream.Write(png, 0, png.Length);
        return zipPath;
    }

    // =========================================================================
    // Category 1: Corrupted Headers, Truncated Files, Non-Zip Renamed .zip
    // =========================================================================

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00 })] // Zeroes
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })] // All ones
    [InlineData(new byte[] { 0x50, 0x4B, 0x00, 0x00 })] // Broken PK signature
    [InlineData(new byte[] { 0x50, 0x4B, 0xFF, 0xFF, 0x00, 0x00 })] // Corrupted PK record
    [InlineData(new byte[] { 0x50 })] // 1 byte truncated header
    [InlineData(new byte[] { 0x50, 0x4B })] // 2 byte truncated header
    [InlineData(new byte[] { 0x50, 0x4B, 0x03 })] // 3 byte truncated header
    public void CorruptedHeader_InvalidMagicBytes_IgnoredWithoutCrashing(byte[] corruptBytes)
    {
        using var tree = new TempTree();
        var zipPath = tree.Write("mods/CorruptHeader.zip", corruptBytes);

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);

        // Direct call to DetectZip also handles safely
        var directResult = PackScanner.DetectZip(zipPath, "Direct", diag);
        Assert.Null(directResult);
    }

    [Fact]
    public void TruncatedZip_TruncatedLocalHeader_IgnoredWithoutCrashing()
    {
        using var tree = new TempTree();
        // Valid local header magic PK\x03\x04 followed by partial header data
        byte[] truncatedData = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00];
        var zipPath = tree.Write("mods/TruncatedLocal.zip", truncatedData);

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    [Fact]
    public void TruncatedZip_TruncatedCentralDirectory_IgnoredWithoutCrashing()
    {
        using var tree = new TempTree();
        var tempFolder = tree.At("scratch");
        Directory.CreateDirectory(tempFolder);
        var validZip = Path.Combine(tempFolder, "valid.zip");
        CreateValidItemsetZip(validZip);

        var allBytes = File.ReadAllBytes(validZip);
        // Delete scratch folder so valid.zip is not scanned
        Directory.Delete(tempFolder, recursive: true);

        // Truncate the file by 30% from the end, destroying the central directory
        var truncatedLength = (int)(allBytes.Length * 0.7);
        var truncatedBytes = allBytes[..truncatedLength];
        var zipPath = tree.Write("mods/TruncatedCentral.zip", truncatedBytes);

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    [Fact]
    public void TruncatedZip_TruncatedEOCD_IgnoredWithoutCrashing()
    {
        using var tree = new TempTree();
        var tempFolder = tree.At("scratch");
        Directory.CreateDirectory(tempFolder);
        var validZip = Path.Combine(tempFolder, "valid.zip");
        CreateValidItemsetZip(validZip);

        var allBytes = File.ReadAllBytes(validZip);
        // Delete scratch folder so valid.zip is not scanned
        Directory.Delete(tempFolder, recursive: true);

        // An End of Central Directory record is at least 22 bytes. Lop off 10 bytes from end.
        var truncatedBytes = allBytes[..^10];
        var zipPath = tree.Write("mods/TruncatedEOCD.zip", truncatedBytes);

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    [Theory]
    [InlineData("mods/TextFile.zip", "This is plain text content, not a PK zip archive at all!")]
    [InlineData("mods/JsonFile.zip", "{\"name\": \"mod\", \"version\": 1.0, \"active\": true}")]
    [InlineData("mods/XmlFile.zip", "<?xml version=\"1.0\"?><root><child>data</child></root>")]
    public void NonZipFile_TextBasedFormatsRenamedZip_IgnoredWithoutCrashing(string path, string textContent)
    {
        using var tree = new TempTree();
        var zipPath = tree.Write(path, textContent);

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    [Fact]
    public void NonZipFile_BinaryFormatsRenamedZip_IgnoredWithoutCrashing()
    {
        using var tree = new TempTree();
        // 1. PE Executable header (MZ...)
        tree.Write("mods/FakeExe.zip", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
        // 2. PNG header (\x89PNG\r\n\x1a\n...)
        tree.Write("mods/FakePng.zip", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        // 3. GZIP header (\x1f\x8b...)
        tree.Write("mods/FakeGzip.zip", [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00]);
        // 4. SQLite header ("SQLite format 3\0")
        tree.Write("mods/FakeSqlite.zip", Encoding.ASCII.GetBytes("SQLite format 3\0"));

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Equal(4, diag.Count(d => d.Code == Codes.PackUnrecognized));
    }

    [Fact]
    public void CorruptedEntryPayload_TryOpenReturnsFalseSafelyWithoutCrashing()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/CorruptEntry.zip");
        CreateValidItemsetZip(zipPath);

        // Index the archive while valid
        var source = ZipSource.FromArchive("corrupt-test", zipPath, "Content");
        Assert.True(source.Exists("Blocksets/defaultItemset.xml"));

        // Now maliciously corrupt the file on disk by overwriting part of it
        var bytes = File.ReadAllBytes(zipPath);
        for (int i = 30; i < Math.Min(bytes.Length - 22, 100); i++)
        {
            bytes[i] = 0xAA;
        }
        File.WriteAllBytes(zipPath, bytes);

        // TryOpen should catch IOException or InvalidDataException, return false, and not throw
        var opened = source.TryOpen("Blocksets/defaultItemset.xml", out var stream);
        Assert.False(opened);
        Assert.Null(stream);
    }

    // =========================================================================
    // Category 2: Zero-Byte .zip Files and Empty Zip Archives
    // =========================================================================

    [Fact]
    public void ZeroByteZip_ScanAndDetectZip_SafelyIgnoredWithDiagnostic()
    {
        using var tree = new TempTree();
        var zipPath = tree.Write("mods/ZeroByte.zip", Array.Empty<byte>());

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);

        var direct = PackScanner.DetectZip(zipPath, "ZeroDirect", diag);
        Assert.Null(direct);
    }

    [Fact]
    public void EmptyZipArchive_ScanAndDetectZip_SafelyIgnoredWithDiagnostic()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/EmptyArchive.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        // Valid 22-byte zip with zero entries
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized && d.Message.Contains("empty zip archive"));

        var direct = PackScanner.DetectZip(zipPath, "EmptyDirect", diag);
        Assert.Null(direct);
    }

    [Fact]
    public void EmptyZipArchive_ZipSource_BehavesAsEmptyContentSource()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("empty.zip");
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }

        var source = ZipSource.FromArchive("empty-pack", zipPath);

        Assert.Equal("empty-pack", source.PackId);
        Assert.Empty(source.Paths);
        Assert.False(source.Exists("anything.xml"));
        Assert.False(source.TryOpen("anything.xml", out var s));
        Assert.Null(s);
        Assert.Null(source.Locate("anything.xml"));
        Assert.Empty(source.List("", ""));
        Assert.Empty(source.List("Blocksets", ".xml"));
    }

    [Fact]
    public void DirectoriesOnlyZip_IgnoredWithDiagnosticWithoutCrashing()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/DirsOnly.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Directory entries in zip archives end with '/'
            archive.CreateEntry("Content/");
            archive.CreateEntry("Content/Blocksets/");
            archive.CreateEntry("Content/Textures/");
        }

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    // =========================================================================
    // Category 3: Special Characters, Unicode Names, and Spaces in Filenames
    // =========================================================================

    [Fact]
    public void SpecialCharacters_UnicodeCyrillicFilename_DiscoveredAndLoaded()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/Пакет_Модів_Переяслав_2026.zip");
        CreateValidItemsetZip(zipPath, prefix: "Content", itemsetKey: "CyrillicItemset");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("Пакет_Модів_Переяслав_2026", pack.Id);
        Assert.Equal(PackKind.ItemsetMod, pack.Kind);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
        Assert.True(pack.Source.TryOpen("Blocksets/defaultItemset.xml", out var stream));
        using (stream)
        {
            using var reader = new StreamReader(stream!);
            Assert.Contains("CyrillicItemset", reader.ReadToEnd());
        }
    }

    [Fact]
    public void SpecialCharacters_UnicodeCJKFilename_DiscoveredAndLoaded()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/宇宙ステーション_Mod_日本語.zip");
        CreateValidItemsetZip(zipPath, prefix: "Content", itemsetKey: "CjkItemset");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("宇宙ステーション_Mod_日本語", pack.Id);
        Assert.Equal(PackKind.ItemsetMod, pack.Kind);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void SpecialCharacters_UnicodeSymbolsAndEmojiFilename_DiscoveredAndLoaded()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/★_Space_Mod_⚡_Ω.zip");
        CreateValidItemsetZip(zipPath, prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("★_Space_Mod_⚡_Ω", pack.Id);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void SpecialCharacters_SpacesAndPunctuation_DiscoveredAndLoaded()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/My Super Mod - (v2.5) [Final] {Test} & More!.zip");
        CreateValidItemsetZip(zipPath, prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("My Super Mod - (v2.5) [Final] {Test} & More!", pack.Id);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void SpecialCharacters_MultipleDotsInFilename_DiscoveredAndLoaded()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/com.corneroids.expansion.pack.v1.0.4.zip");
        CreateValidItemsetZip(zipPath, prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("com.corneroids.expansion.pack.v1.0.4", pack.Id);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void SpecialCharacters_UnicodeAndSpacesInRootDirectory_DiscoveredSafely()
    {
        using var tree = new TempTree();
        var customRoot = tree.At("Custom Root (Простір 2026) [Тест]");
        var zipPath = Path.Combine(customRoot, "mods", "ModInUnicodeRoot.zip");
        CreateValidItemsetZip(zipPath, prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([customRoot], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("ModInUnicodeRoot", pack.Id);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void SpecialCharacters_ArchiveNamedOnlyDotZip_HandledSafelyWithoutCrashing()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("mods/.zip");
        CreateValidItemsetZip(zipPath, prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        // Path.GetFileNameWithoutExtension(".zip") is "" in .NET, UniqueId assigns ""
        var pack = Assert.Single(packs);
        Assert.Equal("", pack.Id);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
    }

    [Fact]
    public void SpecialCharacters_EntriesWithUnicodeNamesInsideArchive_IndexedAndReadable()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("unicode_entries.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e1 = archive.CreateEntry("Content/Blocksets/набір_блоків.xml");
            using (var w = new StreamWriter(e1.Open(), Encoding.UTF8))
            {
                w.Write("<itemset name='UnicodeItemset' />");
            }

            var e2 = archive.CreateEntry("Content/Textures/Blocks/текстура.png");
            using (var s = e2.Open())
            {
                s.Write([10, 20, 30, 40]);
            }
        }

        var source = ZipSource.FromArchive("unicode-pack", zipPath, "Content");

        Assert.True(source.Exists("Blocksets/набір_блоків.xml"));
        Assert.True(source.Exists("Textures/Blocks/текстура.png"));
        Assert.True(source.TryOpen("Blocksets/набір_блоків.xml", out var s1));
        using (s1)
        {
            using var reader = new StreamReader(s1!);
            Assert.Contains("UnicodeItemset", reader.ReadToEnd());
        }
        Assert.Equal($"{zipPath}!/Content/Blocksets/набір_блоків.xml", source.Locate("Blocksets/набір_блоків.xml"));
    }

    // =========================================================================
    // Category 4: Archive Collision Resolution & Case-Insensitive Shadowing
    // =========================================================================

    [Fact]
    public void Collisions_DuplicateNamesAcrossMultipleRoots_DisambiguatedSequentially()
    {
        using var tree = new TempTree();
        var root1 = tree.At("root1");
        var root2 = tree.At("root2");
        var root3 = tree.At("root3");
        var root4 = tree.At("root4");

        CreateValidItemsetZip(Path.Combine(root1, "mods", "DupMod.zip"), prefix: "Content");
        CreateValidItemsetZip(Path.Combine(root2, "mods", "DupMod.zip"), prefix: "Content");
        CreateValidItemsetZip(Path.Combine(root3, "mods", "DupMod.zip"), prefix: "Content");
        CreateValidItemsetZip(Path.Combine(root4, "mods", "DupMod.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([root1, root2, root3, root4], diag);

        Assert.Equal(4, packs.Count);
        var packIds = packs.Select(p => p.Id).ToList();
        Assert.Equal(["DupMod", "DupMod (2)", "DupMod (3)", "DupMod (4)"], packIds);
    }

    [Fact]
    public void Collisions_DirectoryAndZipAcrossDistinctRoots_DisambiguatedSequentially()
    {
        using var tree = new TempTree();
        var root1 = tree.At("root1");
        var root2 = tree.At("root2");
        var root3 = tree.At("root3");

        // Root 1 has uncompressed directory mod
        tree.Write("root1/mods/Coexist/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        // Root 2 has zip archive with same stem
        CreateValidItemsetZip(Path.Combine(root2, "mods", "Coexist.zip"), prefix: "Content");
        // Root 3 has another zip archive with same stem
        CreateValidItemsetZip(Path.Combine(root3, "mods", "Coexist.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([root1, root2, root3], diag);

        Assert.Equal(3, packs.Count);
        Assert.Equal("Coexist", packs[0].Id);
        Assert.DoesNotContain(".zip", packs[0].RootPath, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Coexist (2)", packs[1].Id);
        Assert.EndsWith(".zip", packs[1].RootPath);

        Assert.Equal("Coexist (3)", packs[2].Id);
        Assert.EndsWith(".zip", packs[2].RootPath);
    }

    [Fact]
    public void Collisions_CaseInsensitiveShadowing_InHintFolder_EmitsWarningDiagnostic()
    {
        using var tree = new TempTree();
        // Directory mod with lowercase
        tree.Write("mods/shadowmod/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        // Zip archive with uppercase stem
        CreateValidItemsetZip(tree.At("mods/SHADOWMOD.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        // Only the directory mod should be loaded
        var pack = Assert.Single(packs);
        Assert.Equal("shadowmod", pack.Id);
        Assert.DoesNotContain(".zip", pack.RootPath, StringComparison.OrdinalIgnoreCase);

        // Warning diagnostic emitted for shadowing
        var shadowDiag = Assert.Single(diag, d => d.Code == "pack-shadowed");
        Assert.Equal(Severity.Warning, shadowDiag.Severity);
        Assert.Contains("SHADOWMOD.zip", shadowDiag.Message);
        Assert.Contains("shadowed by directory", shadowDiag.Message);
    }

    [Fact]
    public void Collisions_CaseInsensitiveShadowing_InRootFolder_EmitsWarningDiagnostic()
    {
        using var tree = new TempTree();
        // Flat root layout: directory pack at root
        tree.Write("RootPack/Content/Blocksets/defaultItemset.xml", MiniItemset.Xml());
        // Flat root layout: zip archive at root with same stem
        CreateValidItemsetZip(tree.At("ROOTPACK.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        var pack = Assert.Single(packs);
        Assert.Equal("RootPack", pack.Id);
        Assert.DoesNotContain(".zip", pack.RootPath, StringComparison.OrdinalIgnoreCase);

        var shadowDiag = Assert.Single(diag, d => d.Code == "pack-shadowed");
        Assert.Equal(Severity.Warning, shadowDiag.Severity);
    }

    [Fact]
    public void Collisions_EmptyDirectoryShadowsArchive_EmitsWarningDiagnostic()
    {
        using var tree = new TempTree();
        // Empty directory
        Directory.CreateDirectory(tree.At("mods/EmptyGhost"));
        // Valid zip archive
        CreateValidItemsetZip(tree.At("mods/EmptyGhost.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        // Empty directory mod produces no pack, but STILL shadows the zip archive
        Assert.Empty(packs);

        var shadowDiag = Assert.Single(diag, d => d.Code == "pack-shadowed");
        Assert.Equal(Severity.Warning, shadowDiag.Severity);
        Assert.Contains("EmptyGhost.zip", shadowDiag.Message);
    }

    [Fact]
    public void Collisions_Disambiguation_HandlesExistingSuffixNumbersWithoutThrowing()
    {
        using var tree = new TempTree();
        var root1 = tree.At("root1");
        var root2 = tree.At("root2");
        var root3 = tree.At("root3");

        // root1 has "Mod"
        CreateValidItemsetZip(Path.Combine(root1, "mods", "Mod.zip"), prefix: "Content");
        // root2 already has natural name "Mod (2)"
        CreateValidItemsetZip(Path.Combine(root2, "mods", "Mod (2).zip"), prefix: "Content");
        // root3 also has "Mod" -> should become "Mod (3)" because "Mod (2)" is already claimed
        CreateValidItemsetZip(Path.Combine(root3, "mods", "Mod.zip"), prefix: "Content");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([root1, root2, root3], diag);

        Assert.Equal(3, packs.Count);
        Assert.Equal("Mod", packs[0].Id);
        Assert.Equal("Mod (2)", packs[1].Id);
        Assert.Equal("Mod (3)", packs[2].Id);
    }

    // =========================================================================
    // Category 5: Resilience, Diagnostics, and Non-Crashing Guarantee
    // =========================================================================

    [Fact]
    public void Resilience_LockedFile_IgnoredWithDiagnosticWithoutThrowing()
    {
        using var tree = new TempTree();
        var lockedPath = tree.At("mods/Locked.zip");
        CreateValidItemsetZip(lockedPath);

        // Lock file with exclusive access
        using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        // Locked file should be skipped gracefully with diagnostic
        Assert.Empty(packs);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    [Fact]
    public void Resilience_DirectDetectZipOnNonExistentFileOrDirectory_DoesNotThrow()
    {
        var diag = new DiagnosticBag();

        // 1. Non-existent file
        var r1 = PackScanner.DetectZip("C:/non_existent_folder_xyz/ghost.zip", "Ghost", diag);
        Assert.Null(r1);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);

        // 2. Directory passed as zip
        using var tree = new TempTree();
        var dirPath = tree.At("NotAFile");
        Directory.CreateDirectory(dirPath);

        var r2 = PackScanner.DetectZip(dirPath, "DirAsZip", diag);
        Assert.Null(r2);
        Assert.Contains(diag, d => d.Code == Codes.PackUnrecognized);
    }

    [Fact]
    public void Resilience_InterleavedCorruptedAndValidArchives_DiscoversAllValidPacks()
    {
        using var tree = new TempTree();

        // 1. Corrupt header
        tree.Write("mods/01_Corrupt.zip", [0x50, 0x4B, 0x00, 0x00, 0x01]);

        // 2. Valid itemset
        CreateValidItemsetZip(tree.At("mods/02_ValidItemset.zip"), prefix: "Content", itemsetKey: "ItemsetA");

        // 3. Zero byte
        tree.Write("mods/03_ZeroByte.zip", Array.Empty<byte>());

        // 4. Valid texture pack
        CreateValidTextureZip(tree.At("mods/04_ValidTextures.zip"));

        // 5. Plain text file
        tree.Write("mods/05_PlainText.zip", "not a zip file");

        // 6. Valid flat itemset
        CreateValidItemsetZip(tree.At("mods/06_ValidFlat.zip"), prefix: "", itemsetKey: "ItemsetB");

        var diag = new DiagnosticBag();
        var packs = PackScanner.Scan([tree.Root], diag);

        // Exactly the 3 valid packs must be discovered
        Assert.Equal(3, packs.Count);
        Assert.Contains(packs, p => p.Id == "02_ValidItemset" && p.Kind == PackKind.ItemsetMod);
        Assert.Contains(packs, p => p.Id == "04_ValidTextures" && p.Kind == PackKind.TexturePack);
        Assert.Contains(packs, p => p.Id == "06_ValidFlat" && p.Kind == PackKind.ItemsetMod);

        // Invalid packs recorded diagnostics
        Assert.True(diag.Count(d => d.Code == Codes.PackUnrecognized) >= 3);
    }
}
