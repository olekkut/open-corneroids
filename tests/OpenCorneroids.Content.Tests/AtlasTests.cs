using OpenCorneroids.Content.Atlases;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Content.Tests.Support;
using OpenCorneroids.Content.Vfs;

namespace OpenCorneroids.Content.Tests;

public class AtlasTests
{
    const string BaseRegistry = """
        <blockTextureAtlases>
          <atlas><name>Default</name><key>defaultblocks</key><texture>Textures/Blocks/blockTextures.png</texture><blockSize>8</blockSize></atlas>
          <atlas><name>Base only</name><key>baseonly</key><texture>Textures/Base/b.png</texture><blockSize>8</blockSize></atlas>
        </blockTextureAtlases>
        """;

    // Mods ship the registry as "default + mine"; here the mod also repoints the default atlas.
    const string ModRegistry = """
        <blockTextureAtlases>
          <atlas><name>Default</name><key>defaultblocks</key><texture>Textures/Mod/repainted.png</texture><blockSize>8</blockSize></atlas>
          <atlas><name>Mod</name><key>modblocks</key><texture>Textures/Mod/mod.png</texture><blockSize>8</blockSize></atlas>
        </blockTextureAtlases>
        """;

    [Fact]
    public void Registries_merge_by_key_with_the_upper_layer_winning()
    {
        var game = new MemorySource("game").Add(AtlasCatalog.BlockRegistryPath, BaseRegistry, bom: true);
        var mod = new MemorySource("mod").Add(AtlasCatalog.BlockRegistryPath, ModRegistry);
        var diagnostics = new DiagnosticBag();

        var catalog = AtlasCatalog.Build(new LayeredSource([mod, game]), diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(["baseonly", "defaultblocks", "modblocks"], catalog.BlockAtlases.Keys.Order());
        Assert.Equal(("Textures/Mod/repainted.png", "mod"), (catalog.BlockAtlases["defaultblocks"].TexturePath, catalog.BlockAtlases["defaultblocks"].DeclaringPack));
        Assert.Equal("game", catalog.BlockAtlases["baseonly"].DeclaringPack);
        Assert.Empty(catalog.SpriteAtlases);
    }

    [Fact]
    public void Regions_are_classified_by_their_alpha()
    {
        // Cells along row 0: opaque, empty, half-transparent, and on/off pixels (cutout).
        var png = TestPng.Encode(32, 32, (x, y) => (0, 0, 0, (byte)((x / 8, y / 8) switch
        {
            (1, 0) => 0,
            (2, 0) => 128,
            (3, 0) => x % 2 == 0 ? 255 : 0,
            _ => 255,
        })));
        var source = new MemorySource("game").WithAtlases().Add("Textures/Blocks/mini.png", png);
        var view = new LayeredSource([source]);
        var catalog = AtlasCatalog.Build(view, new DiagnosticBag());

        var grid = AtlasGrid.Load(catalog.BlockAtlases["miniblocks"], view, new DiagnosticBag())!;

        Assert.Equal((4, 4, 8), (grid.Columns, grid.Rows, grid.CellPixels));
        Assert.Equal(AlphaClass.Opaque, grid.Classify(new CellRect(0, 0, 1, 1)));
        Assert.Equal(AlphaClass.Empty, grid.Classify(new CellRect(1, 0, 1, 1)));
        Assert.Equal(AlphaClass.Translucent, grid.Classify(new CellRect(2, 0, 1, 1)));
        Assert.Equal(AlphaClass.Cutout, grid.Classify(new CellRect(3, 0, 1, 1)));
        Assert.Equal(AlphaClass.Translucent, grid.Classify(new CellRect(0, 0, 4, 1)));
        Assert.False(grid.Contains(new CellRect(3, 3, 2, 1)));
    }

    [Fact]
    public void A_higher_resolution_override_keeps_the_declared_grid()
    {
        var game = new MemorySource("game").WithAtlases();
        var hd = new MemorySource("hd").Add("Textures/Blocks/mini.png", TestPng.Atlas(4, 4, 16, (_, _) => 255));
        var view = new LayeredSource([hd, game]);
        var catalog = AtlasCatalog.Build(view, new DiagnosticBag());
        var diagnostics = new DiagnosticBag();

        var grid = AtlasGrid.Load(catalog.BlockAtlases["miniblocks"], view, diagnostics)!;

        Assert.Empty(diagnostics);
        Assert.Equal(("hd", 4, 4, 16), (grid.ServedBy, grid.Columns, grid.Rows, grid.CellPixels));
    }

    [Fact]
    public void Validation_reports_dangling_references_and_bad_regions()
    {
        // Cell (2,1) is empty: the "Torch block" faces point at it.
        var source = new MemorySource("game").WithAtlases((x, y) => (x, y) == (2, 1) ? (byte)0 : (byte)255);
        var xml = MiniItemset.Xml()
            .Replace("""<result itemId="10" quantity="1" />""", """<result itemId="99" quantity="1" />""")
            .Replace("<projectileId>1</projectileId>", "<projectileId>7</projectileId>")
            .Replace("""<walls x="0" y="1" w="2" h="2" />""", """<walls x="3" y="1" w="2" h="2" />""");
        source.Add(MiniItemset.Path, xml);
        var view = new LayeredSource([source]);
        var diagnostics = new DiagnosticBag();
        var set = ItemsetParser.Parse(source, MiniItemset.Path, diagnostics)!;
        var catalog = AtlasCatalog.Build(view, diagnostics);

        ItemsetValidator.Validate(set,
            AtlasGrid.Load(catalog.BlockAtlases["miniblocks"], view, diagnostics),
            AtlasGrid.Load(catalog.SpriteAtlases["minisprites"], view, diagnostics),
            diagnostics);

        Assert.Single(diagnostics, d => d.Code == Codes.DanglingItemRef && d.Message.Contains("99"));
        Assert.Single(diagnostics, d => d.Code == Codes.DanglingProjectileRef && d.Message.Contains("Pistol"));
        Assert.Single(diagnostics, d => d.Code == Codes.RegionOutOfGrid && d.Message.Contains("Door"));
        Assert.Equal(3, diagnostics.Count(d => d.Code == Codes.RegionEmpty && d.Message.Contains("Torch block")));
        Assert.DoesNotContain(diagnostics, d => d.Code == Codes.RegionSizeDiffers && d.Severity > Severity.Info);
    }
}
