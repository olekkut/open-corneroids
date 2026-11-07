using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Tests.Support;
using OpenCorneroids.Content.Vfs;
using Xunit;

namespace OpenCorneroids.Content.Tests.Vfs;

public class ZipSourceStressAndSecurityTests
{
    // =========================================================================
    // 1. High-Concurrency Reads
    // =========================================================================

    [Fact]
    public async Task ConcurrentReads_SameEntry_MultipleThreads_NoContentionOrCorruption()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("concurrent_same.zip");

        var payload = new byte[256 * 1024]; // 256 KB
        Random.Shared.NextBytes(payload);
        var expectedHash = SHA256.HashData(payload);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("Textures/large_texture.bin");
            using var s = entry.Open();
            s.Write(payload);
        }

        var source = ZipSource.FromArchive("conc-pack", zipPath);

        const int workerCount = 64;
        const int iterationsPerWorker = 5;

        var tasks = Enumerable.Range(0, workerCount).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerWorker; i++)
            {
                var opened = source.TryOpen("Textures/large_texture.bin", out var stream);
                Assert.True(opened, "Failed to open zip entry concurrently");
                Assert.NotNull(stream);

                using (stream)
                {
                    Assert.Equal(payload.Length, stream.Length);
                    var buffer = new byte[stream.Length];
                    int readTotal = 0;
                    while (readTotal < buffer.Length)
                    {
                        int read = stream.Read(buffer, readTotal, buffer.Length - readTotal);
                        if (read == 0) break;
                        readTotal += read;
                    }
                    Assert.Equal(payload.Length, readTotal);
                    var actualHash = SHA256.HashData(buffer);
                    Assert.Equal(expectedHash, actualHash);
                }
            }
        }));

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ConcurrentReads_DifferentEntries_DiversePayloads_NoCorruption()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("concurrent_diverse.zip");

        const int entryCount = 40;
        var entryHashes = new Dictionary<string, (int Size, byte[] Hash)>();

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (int i = 0; i < entryCount; i++)
            {
                int size = 100 + (i * 3000); // 100 bytes up to ~120 KB
                var data = new byte[size];
                Random.Shared.NextBytes(data);
                var name = $"Textures/entry_{i:D3}.dat";
                entryHashes[name] = (size, SHA256.HashData(data));

                var entry = archive.CreateEntry(name);
                using var s = entry.Open();
                s.Write(data);
            }
        }

        var source = ZipSource.FromArchive("diverse-pack", zipPath);

        const int workerCount = 32;
        const int readsPerWorker = 30;

        var tasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(() =>
        {
            for (int i = 0; i < readsPerWorker; i++)
            {
                int targetIndex = (workerId + i * 7) % entryCount;
                var targetName = $"Textures/entry_{targetIndex:D3}.dat";
                var (expectedSize, expectedHash) = entryHashes[targetName];

                bool opened = source.TryOpen(targetName, out var stream);
                Assert.True(opened, $"Worker {workerId} failed to open {targetName}");
                using (stream)
                {
                    Assert.Equal(expectedSize, stream!.Length);
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var actualHash = SHA256.HashData(ms.ToArray());
                    Assert.Equal(expectedHash, actualHash);
                }
            }
        }));

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ConcurrentOperations_MixedReadListExistsLocate()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("concurrent_mixed.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (int i = 0; i < 20; i++)
            {
                var entry = archive.CreateEntry($"Blocksets/block_{i}.xml");
                using var w = new StreamWriter(entry.Open());
                w.Write($"<block id=\"{i}\" />");
            }
            archive.CreateEntry("Textures/texture.png");
        }

        var source = ZipSource.FromArchive("mixed-pack", zipPath);

        var tasks = Enumerable.Range(0, 32).Select(workerId => Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                int itemIndex = (workerId + i) % 20;
                var path = $"Blocksets/block_{itemIndex}.xml";

                // Exists
                Assert.True(source.Exists(path));
                Assert.False(source.Exists($"Blocksets/missing_{itemIndex}.xml"));

                // Locate
                var loc = source.Locate(path);
                Assert.NotNull(loc);
                Assert.Contains(path, loc);

                // List
                var list = source.List("Blocksets", ".xml").ToList();
                Assert.Equal(20, list.Count);

                // TryOpen
                Assert.True(source.TryOpen(path, out var s));
                using (s)
                {
                    using var r = new StreamReader(s!);
                    var content = r.ReadToEnd();
                    Assert.Equal($"<block id=\"{itemIndex}\" />", content);
                }
            }
        }));

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ConcurrentFromArchive_MultipleThreadsOnSameArchive()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("concurrent_mount.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/data.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("mount payload");
        }

        var tasks = Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
        {
            var src = ZipSource.FromArchive("pack", zipPath, "Content");
            Assert.True(src.Exists("data.txt"));
            Assert.True(src.TryOpen("data.txt", out var stream));
            using (stream)
            {
                using var r = new StreamReader(stream!);
                Assert.Equal("mount payload", r.ReadToEnd());
            }
        }));

        await Task.WhenAll(tasks);
    }

    // =========================================================================
    // 2. Large Numbers of Archive Entries & Deep Directory Nesting
    // =========================================================================

    [Fact]
    public void LargeArchive_Over1000Entries_ScalesCleanly()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("large_1200.zip");

        const int totalEntries = 1200;
        const int folders = 40;
        var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sw = Stopwatch.StartNew();
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (int i = 0; i < totalEntries; i++)
            {
                int folderIndex = i % folders;
                string entryName = $"Folder_{folderIndex:D2}/file_{i:D4}.txt";
                expectedPaths.Add(entryName);

                var entry = archive.CreateEntry(entryName);
                using var w = new StreamWriter(entry.Open());
                w.Write($"content-{i}");
            }
        }
        sw.Stop();

        // Index large archive
        sw.Restart();
        var source = ZipSource.FromArchive("large-pack", zipPath);
        sw.Stop();

        Assert.Equal(totalEntries, source.Paths.Count);

        // Verify indexing speed is sub-second
        Assert.True(sw.ElapsedMilliseconds < 2000, $"FromArchive took too long: {sw.ElapsedMilliseconds}ms");

        // Verify List() on a single folder returns exact subset
        var folder0Files = source.List("Folder_00", ".txt").ToList();
        Assert.Equal(totalEntries / folders, folder0Files.Count);
        foreach (var file in folder0Files)
        {
            Assert.StartsWith("Folder_00/", file);
        }

        // Verify random sampling of entries open correctly
        for (int sample = 0; sample < totalEntries; sample += 73)
        {
            int folderIndex = sample % folders;
            string entryName = $"Folder_{folderIndex:D2}/file_{sample:D4}.txt";
            Assert.True(source.Exists(entryName));
            Assert.True(source.TryOpen(entryName, out var stream));
            using (stream)
            {
                using var r = new StreamReader(stream!);
                Assert.Equal($"content-{sample}", r.ReadToEnd());
            }
        }
    }

    [Fact]
    public void DeepDirectoryNesting_CanAccessLeafAndList()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("deep_nesting.zip");

        // 30 levels of directory nesting
        var dirSegments = Enumerable.Range(0, 30).Select(i => $"level_{i:D2}").ToArray();
        var deepDir = string.Join('/', dirSegments);
        var deepPath = $"{deepDir}/deep_leaf.xml";

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(deepPath);
            using var w = new StreamWriter(entry.Open());
            w.Write("<deep_data/>");
        }

        var source = ZipSource.FromArchive("deep-pack", zipPath);

        Assert.True(source.Exists(deepPath));
        Assert.True(source.Exists(deepPath.Replace('/', '\\'))); // backslash lookup

        var list = source.List(deepDir, ".xml").ToList();
        Assert.Single(list);
        Assert.Equal(deepPath, list[0]);

        Assert.True(source.TryOpen(deepPath, out var stream));
        using (stream)
        {
            using var r = new StreamReader(stream!);
            Assert.Equal("<deep_data/>", r.ReadToEnd());
        }

        var loc = source.Locate(deepPath);
        Assert.NotNull(loc);
        Assert.Equal($"{zipPath}!/{deepPath}", loc);
    }

    [Fact]
    public void LargeArchive_PackScanner_DetectZip_ResolvesContentRoot()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("scanner_1000.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Add Content/Blocksets/itemset.xml
            var itemset = archive.CreateEntry("Content/Blocksets/defaultItemset.xml");
            using (var w = new StreamWriter(itemset.Open()))
                w.Write("<itemset name=\"mod\"/>");

            // Add 1000 other files under Content/Textures
            for (int i = 0; i < 1000; i++)
            {
                var e = archive.CreateEntry($"Content/Textures/tex_{i:D4}.png");
                using var s = e.Open();
                s.Write([1, 2, 3]);
            }
        }

        var bag = new DiagnosticBag();
        var pack = PackScanner.DetectZip(zipPath, "scanner-pack", bag);

        Assert.NotNull(pack);
        Assert.Equal(PackKind.ItemsetMod, pack.Kind);
        Assert.True(pack.Source.Exists("Blocksets/defaultItemset.xml"));
        Assert.True(pack.Source.Exists("Textures/tex_0500.png"));
        Assert.Equal(1001, ((ZipSource)pack.Source).Paths.Count);
    }

    // =========================================================================
    // 3. Rapid Sequential Open/Close & OS Handle Leak Defense
    // =========================================================================

    [Fact]
    public void RapidOpenClose_DoesNotLeakOSFileHandles()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("handle_test.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("data.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("test payload for handle leakage checks");
        }

        var source = ZipSource.FromArchive("handle-pack", zipPath);

        // Warm up and settle GC
        source.TryOpen("data.txt", out var s0);
        s0?.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        int initialHandles = process.HandleCount;

        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            bool ok = source.TryOpen("data.txt", out var s);
            Assert.True(ok);
            Assert.NotNull(s);
            // Read some bytes and dispose
            byte[] buf = new byte[8];
            s.ReadExactly(buf);
            s.Dispose();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        process.Refresh();
        int finalHandles = process.HandleCount;
        int handleGrowth = finalHandles - initialHandles;

        // If handles were leaking, handleGrowth would be ~1000.
        // It must be negligible (<= 30 accounting for runtime background worker threads).
        Assert.True(handleGrowth < 30,
            $"Possible handle leak! Initial: {initialHandles}, Final: {finalHandles}, Delta: {handleGrowth}");
    }

    [Fact]
    public void StreamLifetime_ZipFileIsNotLocked()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("unlocked_test.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("file.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("Payload Inside Zip");
        }

        var source = ZipSource.FromArchive("unlocked-pack", zipPath);

        // Open stream and DO NOT DISPOSE IT YET
        Assert.True(source.TryOpen("file.txt", out var stream));
        Assert.NotNull(stream);

        // Verify stream is a MemoryStream
        Assert.IsType<MemoryStream>(stream);

        // While the stream is still open and unread:
        // Try to open the zip file with FileAccess.ReadWrite or delete it!
        // If ZipSource held an OS lock, this would throw IOException (sharing violation).
        bool canOpenForWrite = false;
        try
        {
            using (var writeStream = File.Open(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                canOpenForWrite = writeStream.Length > 0;
            }
        }
        catch (IOException)
        {
            canOpenForWrite = false;
        }

        Assert.True(canOpenForWrite, "Underlying zip archive was locked while stream was active!");

        // Finally read from the open stream to ensure it was completely unaffected
        using (stream)
        {
            using var reader = new StreamReader(stream);
            Assert.Equal("Payload Inside Zip", reader.ReadToEnd());
        }
    }

    [Fact]
    public void RapidOpen_WithoutStreamDisposal_DoesNotLeakOSFileHandles()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("undisposed_streams.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("data.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("undisposed stream payload");
        }

        var source = ZipSource.FromArchive("undisposed-pack", zipPath);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        int initialHandles = process.HandleCount;

        var streams = new List<Stream>();
        for (int i = 0; i < 500; i++)
        {
            Assert.True(source.TryOpen("data.txt", out var s));
            streams.Add(s!);
        }

        process.Refresh();
        int finalHandles = process.HandleCount;
        int delta = finalHandles - initialHandles;

        // Even with 500 active undisposed streams, OS file handles must NOT leak
        Assert.True(delta < 30,
            $"Leaked OS file handles with open streams! Initial: {initialHandles}, Final: {finalHandles}, Delta: {delta}");

        foreach (var s in streams) s.Dispose();
    }

    // =========================================================================
    // 4. Zip Path Traversal Attacks & Backslash Path Normalization
    // =========================================================================

    [Theory]
    [InlineData("../../secret.txt")]
    [InlineData("../secret.txt")]
    [InlineData(@"..\..\secret.txt")]
    [InlineData(@"..\secret.txt")]
    [InlineData("Content/../../secret.txt")]
    [InlineData(@"Content\..\..\secret.txt")]
    [InlineData("Content/Blocksets/../../../secret.txt")]
    [InlineData("/secret.txt")]
    [InlineData(@"\secret.txt")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"\\server\share\file.txt")]
    public void PathTraversal_InQueries_AlwaysReturnsFalseOrNull(string attackPath)
    {
        using var tree = new TempTree();
        var zipPath = tree.At("normal.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/Blocksets/itemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("<xml/>");
        }

        var source = ZipSource.FromArchive("pack", zipPath, "Content");

        Assert.False(source.Exists(attackPath));
        Assert.False(source.TryOpen(attackPath, out var s));
        Assert.Null(s);
        Assert.Null(source.Locate(attackPath));
    }

    [Fact]
    public void AdversarialZip_EntriesWithTraversalNames_CannotEscapeMountOrDisk()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("adversarial.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Legitimate entry
            var legitimate = archive.CreateEntry("Content/Blocksets/valid.xml");
            using (var w = new StreamWriter(legitimate.Open())) w.Write("<valid/>");

            // Malicious traversal entries inside zip
            var e1 = archive.CreateEntry("../../secret.txt");
            using (var w = new StreamWriter(e1.Open())) w.Write("evil 1");

            var e2 = archive.CreateEntry(@"..\..\secret2.txt");
            using (var w = new StreamWriter(e2.Open())) w.Write("evil 2");

            var e3 = archive.CreateEntry("Content/../../secret3.txt");
            using (var w = new StreamWriter(e3.Open())) w.Write("evil 3");

            var e4 = archive.CreateEntry("/etc/passwd");
            using (var w = new StreamWriter(e4.Open())) w.Write("evil 4");
        }

        // Mount under "Content"
        var source = ZipSource.FromArchive("adversary", zipPath, "Content");

        // The valid entry is mounted
        Assert.True(source.Exists("Blocksets/valid.xml"));

        // Entries starting with ../../ or /etc/passwd do not start with "Content/" prefix and are discarded
        Assert.False(source.Exists("../../secret.txt"));
        Assert.False(source.Exists(@"..\..\secret2.txt"));
        Assert.False(source.Exists("/etc/passwd"));

        // Even if Content/../../secret3.txt had prefix stripped to ../../secret3.txt,
        // it cannot escape to host disk because TryOpen only extracts from the zip archive itself
        if (source.Exists("../../secret3.txt"))
        {
            Assert.True(source.TryOpen("../../secret3.txt", out var s));
            using (s)
            {
                using var r = new StreamReader(s!);
                Assert.Equal("evil 3", r.ReadToEnd());
            }
        }

        // Host filesystem is completely untouched
        Assert.False(File.Exists(tree.At("../secret.txt")));
        Assert.False(File.Exists(tree.At("../../secret.txt")));
    }

    [Fact]
    public void BackslashPathNormalization_EntryInArchiveAndQueries()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("backslashes_everywhere.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Entry created with backslashes
            var e1 = archive.CreateEntry(@"Content\Textures\Blocks\blockTextures.png");
            using (var s = e1.Open()) s.Write([10, 20, 30, 40]);

            // Entry created with forward slashes
            var e2 = archive.CreateEntry("Content/Blocksets/defaultItemset.xml");
            using (var w = new StreamWriter(e2.Open())) w.Write("<itemset/>");
        }

        var source = ZipSource.FromArchive("slash-pack", zipPath, "Content");

        // Test querying backslash entry using forward slashes
        Assert.True(source.Exists("Textures/Blocks/blockTextures.png"));
        Assert.True(source.TryOpen("Textures/Blocks/blockTextures.png", out var s1));
        using (s1) Assert.Equal(4, s1!.Length);

        // Test querying backslash entry using backslashes
        Assert.True(source.Exists(@"Textures\Blocks\blockTextures.png"));
        Assert.True(source.TryOpen(@"Textures\Blocks\blockTextures.png", out var s2));
        using (s2) Assert.Equal(4, s2!.Length);

        // Test querying forward slash entry using backslashes
        Assert.True(source.Exists(@"Blocksets\defaultItemset.xml"));
        Assert.True(source.TryOpen(@"Blocksets\defaultItemset.xml", out var s3));
        using (s3)
        {
            using var r = new StreamReader(s3!);
            Assert.Equal("<itemset/>", r.ReadToEnd());
        }

        // Test List() using backslashes
        var listSlash = source.List(@"Textures\Blocks", ".png").ToList();
        Assert.Single(listSlash);
        Assert.Equal("Textures/Blocks/blockTextures.png", listSlash[0]);

        var listFwd = source.List("Textures/Blocks", ".png").ToList();
        Assert.Single(listFwd);
        Assert.Equal("Textures/Blocks/blockTextures.png", listFwd[0]);

        // Test mixed casing and slashes
        Assert.True(source.Exists(@"textures/BLOCKS\BlockTextures.PNG"));
        Assert.True(source.TryOpen(@"TEXTURES\blocks/blocktextures.png", out var s4));
        using (s4) Assert.Equal(4, s4!.Length);
    }

    [Fact]
    public void PackScanner_ShadowingWithArchives_FolderTakesPrecedence()
    {
        using var tree = new TempTree();
        var root = tree.At("content_root");
        Directory.CreateDirectory(root);

        // 1. Create uncompressed directory modA
        var dirMod = Path.Combine(root, "modA");
        Directory.CreateDirectory(Path.Combine(dirMod, "Content", "Blocksets"));
        File.WriteAllText(Path.Combine(dirMod, "Content", "Blocksets", "dir.xml"), "<dir/>");

        // 2. Create archive modA.zip
        var zipMod = Path.Combine(root, "modA.zip");
        using (var archive = ZipFile.Open(zipMod, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/Blocksets/zip.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("<zip/>");
        }

        // 3. Create independent modB.zip
        var zipModB = Path.Combine(root, "modB.zip");
        using (var archive = ZipFile.Open(zipModB, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/Blocksets/modb.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("<modb/>");
        }

        var bag = new DiagnosticBag();
        var packs = PackScanner.Scan([root], bag);

        Assert.Equal(2, packs.Count);

        var packA = packs.FirstOrDefault(p => p.Id.Equals("modA", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(packA);
        // Shadowed: packA MUST be the folder, NOT the zip
        Assert.Equal(dirMod, packA.RootPath);
        Assert.True(packA.Source.Exists("Blocksets/dir.xml"));
        Assert.False(packA.Source.Exists("Blocksets/zip.xml"));

        var packB = packs.FirstOrDefault(p => p.Id.Equals("modB", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(packB);
        Assert.Equal(zipModB, packB.RootPath);
        Assert.True(packB.Source.Exists("Blocksets/modb.xml"));

        // DiagnosticBag should have reported a warning for modA.zip shadowed
        Assert.Contains(bag, d => d.Code == "pack-shadowed" && d.File == zipMod);
    }

    [Fact]
    public void PackScanner_NestedContentVsFlatRootVsLooseTextures()
    {
        using var tree = new TempTree();
        var root = tree.At("scan_root");
        Directory.CreateDirectory(root);

        // 1. Nested Content/ layout
        var zipNested = Path.Combine(root, "nested.zip");
        using (var archive = ZipFile.Open(zipNested, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("MyMod/Content/Blocksets/nested.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("<nested/>");
        }

        // 2. Flat root layout (Blocksets at archive root)
        var zipFlat = Path.Combine(root, "flat.zip");
        using (var archive = ZipFile.Open(zipFlat, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Blocksets/flat.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("<flat/>");
        }

        // 3. Loose texture pack (blockTextures.png at archive root)
        var zipLoose = Path.Combine(root, "loose.zip");
        using (var archive = ZipFile.Open(zipLoose, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("blockTextures.png");
            using var s = e.Open();
            s.Write([1, 2, 3, 4]);
        }

        var bag = new DiagnosticBag();
        var packs = PackScanner.Scan([root], bag);

        Assert.Equal(3, packs.Count);

        var pNested = packs.First(p => p.Id == "nested");
        Assert.Equal(PackKind.ItemsetMod, pNested.Kind);
        Assert.True(pNested.Source.Exists("Blocksets/nested.xml"));

        var pFlat = packs.First(p => p.Id == "flat");
        Assert.Equal(PackKind.ItemsetMod, pFlat.Kind);
        Assert.True(pFlat.Source.Exists("Blocksets/flat.xml"));

        var pLoose = packs.First(p => p.Id == "loose");
        Assert.Equal(PackKind.TexturePack, pLoose.Kind);
        Assert.True(pLoose.Source.Exists("Textures/Blocks/blockTextures.png"));
        Assert.True(pLoose.Source.Exists("blockTextures.png"));
    }
}
