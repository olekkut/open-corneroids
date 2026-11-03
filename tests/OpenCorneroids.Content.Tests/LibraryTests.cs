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
}
