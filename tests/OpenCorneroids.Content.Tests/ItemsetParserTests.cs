using System.Globalization;
using System.Numerics;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Content.Tests.Support;

namespace OpenCorneroids.Content.Tests;

public class ItemsetParserTests
{
    static ItemsetDef Parse(string xml, DiagnosticBag? diagnostics = null, bool bom = false) =>
        ItemsetParser.Parse(new MemorySource("test").Add(MiniItemset.Path, xml, bom), MiniItemset.Path, diagnostics ?? new DiagnosticBag())
        ?? throw new Xunit.Sdk.XunitException("itemset did not parse");

    [Fact]
    public void Parses_every_kind_and_keeps_reused_tag_names_apart()
    {
        var diagnostics = new DiagnosticBag();
        var set = Parse(MiniItemset.Xml(), diagnostics);

        Assert.Empty(diagnostics.AtLeast(Severity.Warning));
        Assert.Equal(new ItemsetHeader("Mini", "Tests", "MiniItemset", 3, "miniblocks", "minisprites"), set.Header);
        Assert.Equal([0, 1, 2, 3, 4, 10, 20, 21], set.Items.Select(i => i.ItemId));
        Assert.Equal(5, set.Blocks.Count());
        Assert.Equal([0, 20], set.DefaultItems);

        // <crafting> as category, block and blueprint section.
        Assert.Equal(CraftingStation.Smelter, set.ItemsById[1].Get<CraftingStationComponent>()?.Station);
        var blueprint = Assert.Single(set.Blueprints);
        Assert.Equal(CraftingStation.Smelter, blueprint.Usage);
        Assert.Equal([new ItemStack(0, 2)], blueprint.Ingredients);
        Assert.Equal(new ItemStack(10, 1), blueprint.Result);

        // <power> as category, block and field; and as drill strength.
        Assert.Equal(-1, set.ItemsById[2].Block?.Power);
        Assert.NotNull(set.ItemsById[2].Block?.Inactive);
        Assert.Null(set.ItemsById[0].Block?.Inactive);
        Assert.Equal(2, set.ItemsById[20].Get<DrillComponent>()?.Power);

        // <ingredient> as an item definition, not only a blueprint reference.
        Assert.Equal("Rock dust", set.ItemsById[10].Name);
        Assert.Null(set.ItemsById[10].Block);
    }

    [Fact]
    public void Reads_geometry_lights_and_projectiles()
    {
        var set = Parse(MiniItemset.Xml());

        var door = set.ItemsById[3];
        Assert.Equal(new Vector3(2, 4, 1), door.Block?.Size);
        Assert.Equal(new Vector3(2, 2, 1), door.Block?.VisualSize);
        Assert.Equal(-1.5f, door.Block?.ModelPlacement.Y);
        Assert.Equal(new CellRect(0, 1, 2, 2), door.Block?.Active.Walls);
        Assert.Equal(new DoorComponent(Vector3.UnitY, 2, 4, 2000), door.Get<DoorComponent>());

        Assert.Equal(0.2f, set.ItemsById[4].Block?.Mass);
        Assert.Equal(5, set.ItemsById[4].Get<LightComponent>()?.Light.Radius);
        Assert.Equal(new Rgb8(255, 255, 0), set.ItemsById[20].Get<LightComponent>()?.Light.Color);
        Assert.Null(set.ItemsById[21].Get<LightComponent>());   // <light> is optional on weapons
        Assert.Equal(new WeaponComponent(200, 1, 0.025f, 0.05f), set.ItemsById[21].Get<WeaponComponent>());

        var projectile = set.Projectiles[1];
        Assert.Equal(0.7854f, projectile.RicochetAngle);
        Assert.Equal(new Vector3(0.05f, 0.5f, 0.05f), projectile.Size);
    }

    [Fact]
    public void Decimals_parse_the_same_under_a_comma_decimal_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("uk-UA");
            var set = Parse(MiniItemset.Xml());
            Assert.Equal(0.2f, set.ItemsById[4].Block?.Mass);
            Assert.Equal(-1.5f, set.ItemsById[3].Block?.ModelPlacement.Y);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Unknown_fields_are_kept_as_extras_and_reported_once()
    {
        var diagnostics = new DiagnosticBag();
        var set = Parse(MiniItemset.Xml(weaponExtra: "<glow>3</glow>", asteroidExtra: "<glow>1</glow>"), diagnostics);

        Assert.Equal("3", set.ItemsById[21].Extras["glow"]);
        Assert.Equal("1", set.ItemsById[0].Extras["glow"]);
        Assert.Equal(2, diagnostics.Count(d => d.Code == Codes.UnknownField));   // once per kind
        Assert.All(diagnostics, d => Assert.Equal(Severity.Info, d.Severity));
    }

    [Fact]
    public void Unknown_categories_are_skipped_with_one_warning()
    {
        var teleporter = $"""<teleporter itemId="50" blockId="50">{MiniItemset.Common("T")}{MiniItemset.BlockFields("0 0 1 1")}</teleporter>""";
        var diagnostics = new DiagnosticBag();
        var set = Parse(MiniItemset.Xml(blocksExtra: $"<teleporters>{teleporter}{teleporter.Replace("50", "51")}</teleporters>"), diagnostics);

        Assert.False(set.ItemsById.ContainsKey(50));
        Assert.Single(diagnostics, d => d.Code == Codes.UnknownCategory);
    }

    [Fact]
    public void A_bad_number_is_reported_with_its_line_and_the_item_still_loads()
    {
        var diagnostics = new DiagnosticBag();
        var xml = MiniItemset.Xml().Replace("<reloadTime>200</reloadTime>", "<reloadTime>fast</reloadTime>");
        var set = Parse(xml, diagnostics);

        var bad = Assert.Single(diagnostics, d => d.Code == Codes.BadNumber);
        var expectedLine = xml.Split('\n').ToList().FindIndex(l => l.Contains("fast")) + 1;
        Assert.Equal(expectedLine, bad.Line);
        Assert.Equal(0, set.ItemsById[21].Get<WeaponComponent>()?.ReloadMs);
    }

    [Fact]
    public void Duplicate_item_ids_keep_the_first_definition()
    {
        var copy = $"""<asteroid itemId="0" blockId="9">{MiniItemset.Common("Impostor")}{MiniItemset.BlockFields("0 0 1 1")}<asteroidType>heavy</asteroidType></asteroid>""";
        var diagnostics = new DiagnosticBag();
        var set = Parse(MiniItemset.Xml(blocksExtra: $"<asteroids>{copy}</asteroids>"), diagnostics);

        Assert.Equal("Light rock", set.ItemsById[0].Name);
        Assert.False(set.ItemsByBlockId.ContainsKey(9));
        Assert.Single(diagnostics, d => d.Code == Codes.DuplicateItemId && d.Severity == Severity.Warning);
    }

    [Fact]
    public void Documents_with_a_DTD_are_refused()
    {
        var diagnostics = new DiagnosticBag();
        var xml = MiniItemset.Xml().Replace("<itemset version", "<!DOCTYPE itemset [<!ENTITY x \"boom\">]>\n<itemset version");

        var set = ItemsetParser.Parse(new MemorySource("test").Add(MiniItemset.Path, xml), MiniItemset.Path, diagnostics);

        Assert.Null(set);
        Assert.Single(diagnostics, d => d.Code == Codes.XmlMalformed);
    }

    [Fact]
    public void ScanHeader_reads_the_header_without_the_items()
    {
        var diagnostics = new DiagnosticBag();
        var header = ItemsetParser.ScanHeader(new MemorySource("test").Add(MiniItemset.Path, MiniItemset.Xml(), bom: true), MiniItemset.Path, diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(new ItemsetHeader("Mini", "Tests", "MiniItemset", 3, "miniblocks", "minisprites"), header);
    }
}
