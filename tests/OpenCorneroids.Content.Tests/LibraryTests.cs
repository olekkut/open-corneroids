using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Tests.Support;

namespace OpenCorneroids.Content.Tests;

public class LibraryTests
{
    /// <summary>
    /// A synthetic BYOA root shaped like the real one: a game install, an itemset mod that relies on the game's
    /// default sprite atlas (like Nav), loose and mirrored texture packs, an installer layout, and junk.
    /// </summary>
    static TempTree BuildRoot()
    {
        var tree = new TempTree();
        var opaque = TestPng.Atlas(4, 4, 8, (_, _) => 255);
        var spriteRegistry = """
            <spriteTextureAtlases>
              <atlas><name>Default</name><key>defaultsprites</key><texture>Textures/Sprites/spriteImages.png</texture><blockSize>8</blockSize></atlas>
            </spriteTextureAtlases>
            """;

        // Game install with its own default itemset and atlases.
        tree.Write("games/Game/Corneroids/Corneroids.exe", "MZ");
        tree.Write("games/Game/Corneroids/Content/Blocksets/defaultItemset.xml",
            MiniItemset.Xml().Replace("miniblocks", "defaultblocks").Replace("minisprites", "defaultsprites"));
        tree.Write("games/Game/Corneroids/Content/Textures/Blocks/DefaultBlocks.xml", """
            <blockTextureAtlases>
              <atlas><name>Default</name><key>defaultblocks</key><texture>Textures/Blocks/blockTextures.png</texture><blockSize>8</blockSize></atlas>
            </blockTextureAtlases>
            """);
        tree.Write("games/Game/Corneroids/Content/Textures/Blocks/blockTextures.png", opaque);
        tree.Write("games/Game/Corneroids/Content/Textures/Sprites/SpriteTextureAtlases.xml", spriteRegistry);
        tree.Write("games/Game/Corneroids/Content/Textures/Sprites/spriteImages.png", opaque);

        // Itemset mod: own block atlas, lists the default sprite atlas but doesn't ship its PNG.
        tree.Write("mods/Mod/Content/Blocksets/modItemset.xml",
            MiniItemset.Xml().Replace("<key>MiniItemset</key>", "<key>ModItemset</key>").Replace("minisprites", "defaultsprites"));
        tree.Write("mods/Mod/Content/Textures/Blocks/DefaultBlocks.xml", """
            <blockTextureAtlases>
              <atlas><name>Mod</name><key>miniblocks</key><texture>Textures/ModTextures/blocks.png</texture><blockSize>8</blockSize></atlas>
            </blockTextureAtlases>
            """);
        tree.Write("mods/Mod/Content/Textures/ModTextures/blocks.png", opaque);
        tree.Write("mods/Mod/Content/Textures/Sprites/SpriteTextureAtlases.xml", spriteRegistry);

        // Loose texture pack (like Sord's): 2x resolution replacement for the default block atlas.
        tree.Write("texturepacks/Loose/blockTextures.png", TestPng.Atlas(4, 4, 16, (_, _) => 255));
        tree.Write("texturepacks/Loose/readme.txt", "drop into Content/Textures/Blocks");

        // Mirrored texture pack, an old installer layout, and a folder that isn't a pack.
        tree.Write("Mirrored/Content/Textures/Sprites/spriteImages.png", opaque);
        tree.Write("Installer/Corneroids_installer/Application Files/Corneroids_1_0/Corneroids.exe.deploy", "MZ");
        tree.Write("Installer/Corneroids_installer/Application Files/Corneroids_1_0/Content/Blocksets/defaultItemset.xml.deploy", MiniItemset.Xml());
        tree.Write("Junk/notes.txt", "hello");
        return tree;
    }

    [Fact]
    public void Packs_are_detected_by_content_through_hint_folders()
    {
        using var tree = BuildRoot();

        var library = ContentLibrary.Scan([tree.Root], baseInstallId: "Game");

        Assert.Equal(
            [("Game", PackKind.GameInstall), ("Installer", PackKind.GameInstall), ("Loose", PackKind.TexturePack),
             ("Mirrored", PackKind.TexturePack), ("Mod", PackKind.ItemsetMod)],
            library.Packs.Select(p => (p.Id, p.Kind)).OrderBy(p => p.Id));
        Assert.True(library.FindPack("Loose")!.Source.Exists("Textures/Blocks/blockTextures.png"));
        Assert.True(library.FindPack("Installer")!.Source.Exists("Blocksets/defaultItemset.xml"));
        Assert.All(library.Packs, p => Assert.Null(p.VanillaVersion));   // synthetic data never matches vanilla
        Assert.Single(library.Diagnostics, d => d.Code == Codes.PackUnrecognized && d.File!.EndsWith("Junk"));
        Assert.Equal(["Game", "Installer", "Mod"], library.Itemsets.Select(e => e.Pack.Id).Order());
    }

    [Fact]
    public void Without_a_known_vanilla_install_there_is_no_base_and_a_warning()
    {
        using var tree = BuildRoot();

        var library = ContentLibrary.Scan([tree.Root]);

        Assert.Null(library.BaseInstall);
        Assert.Single(library.Diagnostics, d => d.Code == Codes.NoBaseInstall);
    }

    [Fact]
    public void A_mod_resolves_its_own_atlas_and_falls_back_to_the_base_for_defaults()
    {
        using var tree = BuildRoot();
        var library = ContentLibrary.Scan([tree.Root], baseInstallId: "Game");

        var loaded = library.Load(library.Itemsets.Single(e => e.Pack.Id == "Mod"));

        Assert.Empty(loaded.Diagnostics.AtLeast(Severity.Warning));
        Assert.Equal("Mod > Game", loaded.View.PackId);
        Assert.Equal(("miniblocks", "Mod"), (loaded.BlockAtlas!.Def.Key, loaded.BlockAtlas.ServedBy));
        Assert.Equal(("Mod", "Game"), (loaded.SpriteAtlas!.Def.DeclaringPack, loaded.SpriteAtlas.ServedBy));
        Assert.Equal(8, loaded.Itemset!.Items.Count);
    }

    [Fact]
    public void An_enabled_texture_pack_overrides_the_default_atlas_at_its_own_resolution()
    {
        using var tree = BuildRoot();
        var library = ContentLibrary.Scan([tree.Root], baseInstallId: "Game");
        var game = library.Itemsets.Single(e => e.Pack.Id == "Game");

        var plain = library.Load(game);
        var retextured = library.Load(game, [library.FindPack("Loose")!]);

        Assert.Equal(("Game", 8), (plain.BlockAtlas!.ServedBy, plain.BlockAtlas.CellPixels));
        Assert.Equal("Loose > Game", retextured.View.PackId);
        Assert.Equal(("Loose", 4, 16), (retextured.BlockAtlas!.ServedBy, retextured.BlockAtlas.Columns, retextured.BlockAtlas.CellPixels));
        Assert.Empty(retextured.Diagnostics.AtLeast(Severity.Warning));
    }

    /// <summary>
    /// Shaped like the real DarthNox releases: a repack (a game install) shipping a copy of the vanilla itemset beside
    /// its own, an itemset mod with the same key, and a second install with only the vanilla one.
    /// </summary>
    static TempTree BuildNamingRoot()
    {
        var tree = new TempTree();
        static string Itemset(string key, string name) =>
            MiniItemset.Xml().Replace("<key>MiniItemset</key>", $"<key>{key}</key>").Replace("<name>Mini</name>", $"<name>{name}</name>");
        tree.Write("games/Repack/Corneroids/Corneroids.exe", "MZ");
        tree.Write("games/Repack/Corneroids/Content/Blocksets/defaultItemset.xml", Itemset("DefaultItemset", "Default"));
        tree.Write("games/Repack/Corneroids/Content/Blocksets/ownItemset.xml", Itemset("OwnItemset", "Repack v2"));
        tree.Write("games/Game/Corneroids/Corneroids.exe", "MZ");
        tree.Write("games/Game/Corneroids/Content/Blocksets/defaultItemset.xml", Itemset("DefaultItemset", "Default"));
        tree.Write("mods/OwnItemset/Content/Blocksets/ownItemset.xml", Itemset("OwnItemset", "Own"));
        return tree;
    }

    [Theory]
    [InlineData("OwnItemset", "OwnItemset", "Own")]          // a pack id wins over the key two itemsets share
    [InlineData("repack", "Repack", "Repack v2")]            // a pack's own itemset, not its vanilla copy
    [InlineData("Game", "Game", "Default")]                  // a pack with only the vanilla one
    [InlineData("Repack/DefaultItemset", "Repack", "Default")]
    [InlineData("Repack v2", "Repack", "Repack v2")]         // the name the menu shows
    public void An_itemset_is_found_by_pack_key_or_name(string choice, string pack, string name)
    {
        using var tree = BuildNamingRoot();
        var library = ContentLibrary.Scan([tree.Root]);

        var entry = library.FindItemset(choice, out var problem);

        Assert.Null(problem);
        Assert.Equal((pack, name), (entry?.Pack.Id, entry?.Header.Name));
    }

    [Fact]
    public void An_itemset_is_found_by_index_and_an_ambiguous_or_unknown_choice_says_why()
    {
        using var tree = BuildNamingRoot();
        var library = ContentLibrary.Scan([tree.Root]);

        Assert.Same(library.Itemsets[2], library.FindItemset("2", out _));
        Assert.Null(library.FindItemset("9", out var outOfRange));
        Assert.Contains("#9", outOfRange);
        Assert.Null(library.FindItemset("DefaultItemset", out var ambiguous));
        Assert.Contains("matches 2 itemsets", ambiguous);
        Assert.Contains("Game/DefaultItemset", ambiguous);
        Assert.Contains("Repack/DefaultItemset", ambiguous);
        Assert.Null(library.FindItemset("Nothing", out var unknown));
        Assert.Contains("No itemset matches", unknown);
    }

    [Fact]
    public void A_key_several_packs_share_means_the_base_installs_itemset()
    {
        using var tree = BuildNamingRoot();
        var library = ContentLibrary.Scan([tree.Root], baseInstallId: "Game");

        var entry = library.FindItemset("defaultitemset", out var problem);

        Assert.Null(problem);
        Assert.Equal("Game", entry?.Pack.Id);
    }
}
