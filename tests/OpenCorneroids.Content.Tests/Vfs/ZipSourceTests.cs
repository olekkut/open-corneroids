using System.IO.Compression;
using System.Text;
using OpenCorneroids.Content.Tests.Support;
using OpenCorneroids.Content.Vfs;
using Xunit;

namespace OpenCorneroids.Content.Tests.Vfs;

public class ZipSourceTests
{
    [Fact]
    public void FromArchive_indexes_entries_and_normalizes_paths()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("test.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e1 = archive.CreateEntry("Blocksets/defaultItemset.xml");
            using (var w = new StreamWriter(e1.Open())) w.Write("<itemset/>");

            var e2 = archive.CreateEntry("Textures/Blocks/blockTextures.png");
            using (var s = e2.Open()) s.Write([1, 2, 3]);
        }

        var source = ZipSource.FromArchive("test-pack", zipPath);

        Assert.Equal("test-pack", source.PackId);
        Assert.True(source.Exists("Blocksets/defaultItemset.xml"));
        Assert.True(source.Exists("blocksets/DEFAULTITEMSET.XML"));
        Assert.True(source.Exists(@"Textures\Blocks\blockTextures.png"));
        Assert.False(source.Exists("NonExistent/file.txt"));
        Assert.False(source.Exists(""));
        Assert.Contains("Blocksets/defaultItemset.xml", source.Paths);
        Assert.Contains("Textures/Blocks/blockTextures.png", source.Paths);
    }

    [Fact]
    public void FromArchive_with_prefix_mounts_subfolder()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("prefix.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("MyMod/Content/Blocksets/itemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("<itemset/>");
        }

        var source = ZipSource.FromArchive("mod", zipPath, "MyMod/Content");

        Assert.True(source.Exists("Blocksets/itemset.xml"));
        Assert.False(source.Exists("MyMod/Content/Blocksets/itemset.xml"));
    }

    [Fact]
    public void FromArchive_strips_deploy_extension()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("deploy.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Content/Blocksets/defaultItemset.xml.deploy");
            using var w = new StreamWriter(e.Open());
            w.Write("<deploy-itemset/>");
        }

        var source = ZipSource.FromArchive("deploy", zipPath, "Content");

        Assert.True(source.Exists("Blocksets/defaultItemset.xml"));
        Assert.True(source.TryOpen("Blocksets/defaultItemset.xml", out var stream));
        using (stream)
        {
            using var reader = new StreamReader(stream!);
            Assert.Equal("<deploy-itemset/>", reader.ReadToEnd());
        }
    }

    [Fact]
    public void TryOpen_returns_seekable_independent_memory_streams()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("seekable.zip");
        var content = "Corneroids Voxel Data Block 1234567890";
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("data.txt");
            using var w = new StreamWriter(e.Open());
            w.Write(content);
        }

        var source = ZipSource.FromArchive("seekable", zipPath);

        Assert.True(source.TryOpen("data.txt", out var stream1));
        Assert.True(source.TryOpen("data.txt", out var stream2));
        using (stream1)
        using (stream2)
        {
            Assert.True(stream1!.CanSeek);
            Assert.True(stream2!.CanSeek);

            // Change stream1 position
            stream1.Position = 10;
            Assert.Equal(10, stream1.Position);
            Assert.Equal(0, stream2.Position);

            using var reader1 = new StreamReader(stream1, Encoding.UTF8, leaveOpen: true);
            var rest1 = reader1.ReadToEnd();
            Assert.Equal(content[10..], rest1);

            using var reader2 = new StreamReader(stream2, Encoding.UTF8, leaveOpen: true);
            var rest2 = reader2.ReadToEnd();
            Assert.Equal(content, rest2);
        }
    }

    [Fact]
    public async Task TryOpen_is_thread_safe_and_concurrent()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("concurrent.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("test.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("Concurrent Payload");
        }

        var source = ZipSource.FromArchive("conc", zipPath);

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            Assert.True(source.TryOpen("test.txt", out var s));
            using (s)
            {
                using var reader = new StreamReader(s!);
                Assert.Equal("Concurrent Payload", reader.ReadToEnd());
            }
        }));

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void TryOpen_releases_file_locks_immediately()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("lock.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("entry.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("data");
        }

        var source = ZipSource.FromArchive("lock", zipPath);
        Assert.True(source.TryOpen("entry.txt", out var s));
        using (s)
        {
            // Even while stream is active, the zip file should NOT be held open with exclusive locks
            using var readAgain = File.Open(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Assert.True(readAgain.Length > 0);
        }
    }

    [Fact]
    public void TryOpen_returns_false_and_null_stream_for_missing_entry()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("missing.zip");
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }

        var source = ZipSource.FromArchive("missing", zipPath);

        Assert.False(source.TryOpen("ghost.xml", out var s1));
        Assert.Null(s1);

        Assert.False(source.TryOpen("", out var s2));
        Assert.Null(s2);
    }

    [Fact]
    public void Locate_returns_archive_bang_entry()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("locate.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Blocksets/defaultItemset.xml");
            using var w = new StreamWriter(e.Open());
            w.Write("<xml/>");
        }

        var source = ZipSource.FromArchive("loc", zipPath);

        var loc = source.Locate("Blocksets/defaultItemset.xml");
        Assert.NotNull(loc);
        Assert.Equal($"{zipPath}!/Blocksets/defaultItemset.xml", loc);

        Assert.Null(source.Locate("missing.xml"));
        Assert.Null(source.Locate(""));
    }

    [Fact]
    public void List_filters_by_directory_and_extension_and_orders()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("list.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("Blocksets/b.xml");
            archive.CreateEntry("Blocksets/a.xml");
            archive.CreateEntry("Blocksets/c.txt");
            archive.CreateEntry("Textures/t.xml");
        }

        var source = ZipSource.FromArchive("list", zipPath);

        var xmls = source.List("Blocksets", ".xml").ToList();
        Assert.Equal(["Blocksets/a.xml", "Blocksets/b.xml"], xmls);

        var allBlocksets = source.List("Blocksets", "").ToList();
        Assert.Equal(["Blocksets/a.xml", "Blocksets/b.xml", "Blocksets/c.txt"], allBlocksets);

        Assert.Empty(source.List("NonExistent", ".xml"));
    }

    [Fact]
    public void Path_traversal_attempts_cannot_escape()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("traversal.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("safe.txt");
        }

        var source = ZipSource.FromArchive("trav", zipPath);

        Assert.False(source.Exists("../../../secret.txt"));
        Assert.False(source.TryOpen("../../../secret.txt", out var s));
        Assert.Null(s);
        Assert.Null(source.Locate("../../../secret.txt"));
    }

    [Fact]
    public void Corrupt_archive_throws_on_FromArchive()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("corrupted.zip");
        File.WriteAllBytes(zipPath, [0x50, 0x4B, 0x00, 0x00, 0xFF, 0xFF, 0xFF]);

        Assert.ThrowsAny<Exception>(() => ZipSource.FromArchive("corrupt", zipPath));
    }

    [Fact]
    public void Entry_with_backslashes_is_normalized_and_readable()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("backslash.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("Folder\\Sub\\file.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("Backslash Content");
        }

        var source = ZipSource.FromArchive("slash", zipPath);

        Assert.True(source.Exists("Folder/Sub/file.txt"));
        Assert.True(source.Exists(@"Folder\Sub\file.txt"));
        Assert.True(source.TryOpen("Folder/Sub/file.txt", out var stream));
        using (stream)
        {
            using var reader = new StreamReader(stream!);
            Assert.Equal("Backslash Content", reader.ReadToEnd());
        }
    }

    [Fact]
    public void Loose_textures_at_root_are_aliased_to_canonical_content_paths()
    {
        using var tree = new TempTree();
        var zipPath = tree.At("loose.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("blockTextures.png");
            using var s = e.Open();
            s.Write([1, 2, 3, 4]);
        }

        var source = ZipSource.FromArchive("loose", zipPath, "");

        Assert.True(source.Exists("blockTextures.png"));
        Assert.True(source.Exists("Textures/Blocks/blockTextures.png"));
        Assert.True(source.TryOpen("Textures/Blocks/blockTextures.png", out var stream));
        using (stream)
        {
            Assert.Equal(4, stream!.Length);
        }
    }
}
