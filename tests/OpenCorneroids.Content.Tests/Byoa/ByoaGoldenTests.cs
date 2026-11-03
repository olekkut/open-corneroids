using OpenCorneroids.Content.Atlases;
using OpenCorneroids.Content.Discovery;

namespace OpenCorneroids.Content.Tests.Byoa;

/// <summary>
/// Checks the loader against the real original files and community mods in the developer's local BYOA folder.
/// The expected numbers are the ones recorded in docs/design/byoa-asset-loading.md.
/// </summary>
public class ByoaGoldenTests
{
    const string Vanilla120 = "Corneroids-Alpha-1.2.0";
    const string Vanilla1012 = "Corneroids-Alpha-1.0.1.2";
    const string V15 = "Chef Anons DarthNox Rebalance Repack v15";
    const string V21 = "v21";
    const string DarthNox = "DarthNoxItemset";
    const string Nav = "navItemset";
    const string Sord = "SordsTextures";

    static ContentLibrary Library => ByoaRoot.Library.Value;

    static LoadedItemset Load(string packId, string path, params string[] texturePacks) =>
        Library.Load(
            Library.Itemsets.Single(e => e.Pack.Id == packId && e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)),
            texturePacks.Select(id => Library.FindPack(id)!));

    [ByoaFact(Vanilla120, Vanilla1012, V15, V21, DarthNox, Nav, Sord)]
    public void Every_pack_is_detected_with_the_right_kind()
    {
        Assert.Equal(PackKind.GameInstall, Library.FindPack(Vanilla120)?.Kind);
        Assert.Equal(PackKind.GameInstall, Library.FindPack(Vanilla1012)?.Kind);
        Assert.Equal(PackKind.GameInstall, Library.FindPack(V15)?.Kind);
        Assert.Equal(PackKind.GameInstall, Library.FindPack(V21)?.Kind);
        Assert.Equal(PackKind.ItemsetMod, Library.FindPack(DarthNox)?.Kind);
        Assert.Equal(PackKind.ItemsetMod, Library.FindPack(Nav)?.Kind);
        Assert.Equal(PackKind.TexturePack, Library.FindPack(Sord)?.Kind);
        Assert.Empty(Library.Diagnostics.AtLeast(Severity.Warning));
    }

    [ByoaFact(Vanilla120, Vanilla1012, V21)]
    public void Vanilla_installs_are_recognised_and_the_newest_is_the_base()
    {
        Assert.Equal(new Version(1, 2, 0), Library.FindPack(Vanilla120)?.VanillaVersion);
        Assert.Equal(new Version(1, 0, 1, 2), Library.FindPack(Vanilla1012)?.VanillaVersion);
        Assert.Null(Library.FindPack(V21)?.VanillaVersion);
        Assert.Equal(Vanilla120, Library.BaseInstall?.Id);
    }

    [ByoaTheory(Vanilla120, Vanilla1012, V15, V21, DarthNox, Nav)]
    [InlineData(Vanilla1012, "Blocksets/defaultItemset.xml", 112, 46, 73, 6)]
    [InlineData(Vanilla120, "Blocksets/defaultItemset.xml", 113, 47, 82, 6)]
    [InlineData(DarthNox, "Blocksets/DarthNoxtItemset.xml", 140, 60, 113, 6)]
    [InlineData(V15, "Blocksets/DarthNoxtItemset.xml", 162, 81, 138, 8)]
    [InlineData(V21, "Blocksets/DarthNoxtItemset.xml", 196, 101, 170, 9)]
    [InlineData(Nav, "Blocksets/navItemset.xml", 116, 47, 87, 8)]
    public void Itemsets_load_with_the_documented_counts(string pack, string path, int items, int blocks, int blueprints, int projectiles)
    {
        var loaded = Load(pack, path);

        Assert.Empty(loaded.Diagnostics.AtLeast(Severity.Warning));
        var set = loaded.Itemset!;
        Assert.Equal((items, blocks, blueprints, projectiles),
            (set.Items.Count, set.Blocks.Count(), set.Blueprints.Count, set.Projectiles.Count));
    }

    [ByoaFact]
    public void Every_itemset_found_loads_cleanly_through_its_own_view()
    {
        Assert.NotEmpty(Library.Itemsets);
        Assert.All(Library.Itemsets, entry =>
        {
            var loaded = Library.Load(entry);
            Assert.True(loaded.Itemset is not null && loaded.BlockAtlas is not null && loaded.SpriteAtlas is not null,
                $"{entry.Pack.Id}:{entry.Path} did not fully resolve");
            Assert.Empty(loaded.Diagnostics.AtLeast(Severity.Warning));
        });
    }

    [ByoaFact(Vanilla120, V15, V21, DarthNox)]
    public void Fingerprints_tell_apart_what_key_and_version_cannot()
    {
        var vanilla = Load(Vanilla120, "Blocksets/defaultItemset.xml").Itemset!;
        // The repacks' copies of the vanilla itemset differ only by an empty <spotLights />.
        Assert.Equal(vanilla.Fingerprint, Load(V21, "Blocksets/defaultItemset.xml").Itemset!.Fingerprint);
        Assert.Equal(vanilla.Fingerprint, Load(V15, "Blocksets/defaultItemset.xml").Itemset!.Fingerprint);

        // Three DarthNox releases: same key and version, different content.
        var darthNox = new[] { DarthNox, V15, V21 }.Select(p => Load(p, "Blocksets/DarthNoxtItemset.xml").Itemset!).ToList();
        Assert.All(darthNox, s => Assert.Equal(("DarthNoxItemset", 5), (s.Header.Key, s.Header.Version)));
        Assert.Equal(3, darthNox.Select(s => s.Fingerprint).Distinct().Count());
    }

    [ByoaFact(Nav, Vanilla120)]
    public void Nav_uses_its_own_atlases_and_falls_back_to_vanilla_for_the_defaults_it_does_not_ship()
    {
        var loaded = Load(Nav, "Blocksets/navItemset.xml");

        Assert.Equal(("navblocks", Nav), (loaded.BlockAtlas!.Def.Key, loaded.BlockAtlas.ServedBy));
        Assert.Equal(("navsprites", Nav), (loaded.SpriteAtlas!.Def.Key, loaded.SpriteAtlas.ServedBy));
        var defaults = AtlasGrid.Load(loaded.Atlases.BlockAtlases["defaultblocks"], loaded.View, new DiagnosticBag())!;
        Assert.Equal((Nav, Vanilla120), (defaults.Def.DeclaringPack, defaults.ServedBy));
    }

    [ByoaFact(Vanilla120, Sord)]
    public void Sords_textures_replace_the_vanilla_block_atlas_only_when_enabled()
    {
        Assert.Equal(Vanilla120, Load(Vanilla120, "Blocksets/defaultItemset.xml").BlockAtlas!.ServedBy);

        var retextured = Load(Vanilla120, "Blocksets/defaultItemset.xml", Sord);

        Assert.Equal((Sord, 16, 32), (retextured.BlockAtlas!.ServedBy, retextured.BlockAtlas.Columns, retextured.BlockAtlas.CellPixels));
        Assert.Empty(retextured.Diagnostics.AtLeast(Severity.Warning));
    }

    [ByoaFact(Vanilla120)]
    public void Windows_are_cutouts_and_hull_blocks_are_opaque()
    {
        var loaded = Load(Vanilla120, "Blocksets/defaultItemset.xml");
        AlphaClass WallsOf(string name) =>
            loaded.BlockAtlas!.Classify(loaded.Itemset!.Items.Single(i => i.Name == name).Block!.Active.Walls);

        // Window panes are fully transparent pixels inside an opaque frame; no known block atlas uses partial alpha.
        Assert.Equal(AlphaClass.Cutout, WallsOf("Big window block"));
        Assert.Equal(AlphaClass.Opaque, WallsOf("Light aluminium block"));
    }
}
