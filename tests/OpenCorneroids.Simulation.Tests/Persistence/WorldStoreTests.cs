using OpenCorneroids.Simulation.Persistence;
using static OpenCorneroids.Simulation.Tests.Persistence.SaveFixtures;

namespace OpenCorneroids.Simulation.Tests.Persistence;

public sealed class WorldStoreTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "oc-worldstore-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("Alpha", "Alpha.ocworld")]
    [InlineData("My world 2", "My world 2.ocworld")]
    [InlineData("Kosmos Ω", "Kosmos Ω.ocworld")]
    [InlineData("", "world.ocworld")]
    [InlineData("   ", "world.ocworld")]
    [InlineData("...", "world.ocworld")]
    [InlineData(".hidden", "hidden.ocworld")]
    [InlineData("Ends with dot. ", "Ends with dot.ocworld")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j.ocworld")]
    [InlineData("tab\there\nnewline", "tab_here_newline.ocworld")]
    [InlineData("../../etc/passwd", "_.._etc_passwd.ocworld")]
    [InlineData("CON", "_CON.ocworld")]
    [InlineData("nul.txt", "_nul.txt.ocworld")]
    [InlineData("com1", "_com1.ocworld")]
    [InlineData("Console", "Console.ocworld")]
    public void FileNameFor_makes_safe_names(string worldName, string expected) =>
        Assert.Equal(expected, WorldStore.FileNameFor(worldName));

    [Fact]
    public void FileNameFor_caps_length_and_stays_usable()
    {
        var name = WorldStore.FileNameFor(new string('x', 500));
        Assert.True(name.Length <= 64 + WorldStore.Extension.Length, name);
        Assert.EndsWith(WorldStore.Extension, name);

        Directory.CreateDirectory(_root);
        foreach (var worldName in new[] { new string('x', 500), "a/b", "CON", "\0", " ", "😀" + new string('y', 70) })
        {
            var file = WorldStore.FileNameFor(worldName);
            Assert.True(file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, file);
            File.WriteAllText(Path.Combine(_root, file), "x");
        }
    }

    [Fact]
    public void Save_then_Load_round_trips_and_creates_the_folder()
    {
        var directory = Path.Combine(_root, "nested", "saves");
        var world = SmallWorld();

        WorldStore.Save(directory, world);

        var path = Path.Combine(directory, WorldStore.FileNameFor(world.Name));
        AssertSameWorld(world, WorldStore.Load(path));
    }

    [Fact]
    public void SaveFile_writes_to_the_given_file_not_the_one_named_after_the_world()
    {
        Directory.CreateDirectory(_root);
        var world = SmallWorld();
        WorldStore.Save(_root, world);
        var original = Path.Combine(_root, WorldStore.FileNameFor(world.Name));
        var originalBytes = File.ReadAllBytes(original);

        var backup = Path.Combine(_root, "backup copy" + WorldStore.Extension);
        var changed = world with { Seed = world.Seed + 1 };
        WorldStore.SaveFile(backup, changed);

        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        AssertSameWorld(changed, WorldStore.Load(backup));
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Fact]
    public void SaveFile_creates_the_folder()
    {
        var path = Path.Combine(_root, "deep", "x" + WorldStore.Extension);
        WorldStore.SaveFile(path, SmallWorld());
        AssertSameWorld(SmallWorld(), WorldStore.Load(path));
    }

    [Fact]
    public void Save_replaces_the_previous_file_and_leaves_no_temp_files()
    {
        var world = SmallWorld();
        WorldStore.Save(_root, world);
        var later = world with { Seed = 7, SavedAt = world.SavedAt.AddMinutes(5) };

        WorldStore.Save(_root, later);

        var file = Assert.Single(Directory.GetFiles(_root));
        Assert.Equal(7, WorldStore.Load(file).Seed);
    }

    [Fact]
    public void List_is_newest_first_and_skips_unreadable_files()
    {
        WorldStore.Save(_root, SmallWorld() with { Name = "Old", SavedAt = SavedAt });
        WorldStore.Save(_root, SmallWorld() with { Name = "New", SavedAt = SavedAt.AddDays(1) });
        WorldStore.Save(_root, AllAir(4) with { Name = "Middle", SavedAt = SavedAt.AddHours(1) });
        File.WriteAllBytes(Path.Combine(_root, "garbage.ocworld"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_root, "empty.ocworld"), []);
        var future = ToBytes(SmallWorld());
        future[4] = 99;
        File.WriteAllBytes(Path.Combine(_root, "future.ocworld"), future);
        File.WriteAllBytes(Path.Combine(_root, "notes.txt"), ToBytes(SmallWorld()));
        Directory.CreateDirectory(Path.Combine(_root, "folder.ocworld"));

        var list = WorldStore.List(_root);

        Assert.Equal(["New", "Middle", "Old"], list.Select(s => s.Name));
        Assert.All(list, s => Assert.True(File.Exists(s.FilePath), s.FilePath));
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "New.ocworld")), list[0].FilePath);
        Assert.Equal(Binding, list[0].Itemset);
    }

    [Fact]
    public void Failed_save_keeps_the_previous_file_and_leaves_no_temp_files()
    {
        var world = SmallWorld();
        WorldStore.Save(_root, world);
        var inconsistent = world with { Seed = 7, Cells = new ushort[3] };

        Assert.Throws<ArgumentException>(() => WorldStore.Save(_root, inconsistent));

        var file = Assert.Single(Directory.GetFiles(_root));
        AssertSameWorld(world, WorldStore.Load(file));
    }

    [Fact]
    public void List_skips_a_file_another_process_holds_locked()
    {
        WorldStore.Save(_root, SmallWorld() with { Name = "Readable" });
        WorldStore.Save(_root, SmallWorld() with { Name = "Locked" });

        using (new FileStream(Path.Combine(_root, "Locked.ocworld"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(["Readable"], WorldStore.List(_root).Select(s => s.Name));
    }

    [Fact]
    public void FileNameFor_never_splits_a_surrogate_pair_at_the_length_cap()
    {
        // The emoji straddles the cut: its high surrogate is the 64th char.
        var file = WorldStore.FileNameFor(new string('x', 63) + "😀");
        Assert.Equal(new string('x', 63) + WorldStore.Extension, file);

        // One char earlier it fits whole.
        Assert.Equal(new string('x', 62) + "😀" + WorldStore.Extension, WorldStore.FileNameFor(new string('x', 62) + "😀z"));
    }

    [Fact]
    public void List_of_a_missing_folder_is_empty() =>
        Assert.Empty(WorldStore.List(Path.Combine(_root, "does-not-exist")));

    [Fact]
    public void Load_of_a_damaged_file_throws_WorldSaveException()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "broken.ocworld");
        File.WriteAllBytes(path, ToBytes(SmallWorld())[..40]);
        Assert.Throws<WorldSaveException>(() => WorldStore.Load(path));
    }

    [Fact]
    public void Verify_accepts_a_good_save_and_rejects_one_whose_header_reads_but_body_is_cut_off()
    {
        Directory.CreateDirectory(_root);
        WorldStore.Save(_root, SmallWorld() with { Name = "Good" });
        var good = Path.Combine(_root, "Good.ocworld");
        WorldStore.Verify(good);

        var bytes = File.ReadAllBytes(good);
        var half = Path.Combine(_root, "Half.ocworld");
        File.WriteAllBytes(half, bytes[..(bytes.Length - 3)]);

        // The header still lists, which is why callers need Verify to know the body is gone.
        Assert.Contains(WorldStore.List(_root), s => s.FilePath == Path.GetFullPath(half));
        Assert.Throws<WorldSaveException>(() => WorldStore.Verify(half));
    }

    [Fact]
    public void List_and_Load_supports_legacy_wrl_files()
    {
        Directory.CreateDirectory(_root);
        var wrlPath = Path.Combine(_root, "LegacyShip.wrl");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={wrlPath};Pooling=false"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE worldinfo (name VARCHAR(50), version FLOAT, seed INT, itemsetKey VARCHAR(50), timestamp LONG, token VARCHAR(20));
                CREATE TABLE players (id INT PRIMARY KEY, name VARCHAR(50), posx LONG, posy LONG, posz LONG, orx FLOAT, ory FLOAT, orz FLOAT, orw FLOAT, boardedEntity INT, boundEntities BLOB, token VARCHAR(20));
                CREATE TABLE entities (id INT PRIMARY KEY, loaded BOOLEAN, posx LONG, posy LONG, posz LONG, orx FLOAT, ory FLOAT, orz FLOAT, orw FLOAT, sectorx INT, sectory INT, sectorz INT);
                CREATE TABLE blocksectors (ownerId INT, posx INT, posy INT, posz INT, formation BLOB, blockIds BLOB, orientations BLOB, PRIMARY KEY (ownerId, posx, posy, posz));
                CREATE TABLE controlledblocks (id INT, boundTo INT, bindingColor INT, ownerEntityId INT, posx FLOAT, posy FLOAT, posz FLOAT);
                CREATE TABLE controlblocks (id INT, bindingId INT, ownerEntityId INT, posx FLOAT, posy FLOAT, posz FLOAT);
                CREATE TABLE keyColorMappings (ownerEntityId INT, blockId INT, color INT, key INT);
                INSERT INTO worldinfo VALUES ('Legacy World', 1.2, 42, 'Default', 1350000000000, 'tok');
                INSERT INTO players VALUES (1, 'Genius', 83886080, 0, 0, 0, 0, 0, 1, 0, null, 'tok');
                """;
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var list = WorldStore.List(_root);
        var entry = Assert.Single(list);
        Assert.Equal("Legacy World", entry.Name);
        Assert.Equal(Path.GetFullPath(wrlPath), entry.FilePath);
        Assert.Equal("Default", entry.Itemset.Key);

        var loaded = WorldStore.Load(wrlPath);
        Assert.Equal("Legacy World", loaded.Name);
        Assert.Equal(42, loaded.Seed);
        Assert.Equal(10.0f, loaded.Player.Position.X, 0.001f);
    }
}
