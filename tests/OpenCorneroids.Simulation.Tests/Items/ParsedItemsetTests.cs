using System.Xml.Linq;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Simulation.Items;
using OpenCorneroids.Simulation.Tests.Items.Support;

namespace OpenCorneroids.Simulation.Tests.Items;

/// <summary>
/// The item rules run against an itemset that went through the real parser, so the two packages agree on field
/// meanings. The XML is hand-written in the original format; no original game data is used.
/// </summary>
public class ParsedItemsetTests
{
    const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <itemset version="1">
          <name>Rules</name>
          <author>Tests</author>
          <key>RulesItemset</key>
          <defaultItems>
            <item itemId="20" />
            <item itemId="404" />
            <item itemId="0" />
          </defaultItems>
          <blocks>
            <asteroids>
              <asteroid itemId="0" blockId="0">
                <color r="1" g="2" b="3" /><description></description><maxStackSize>50</maxStackSize>
                <modelDepth>1</modelDepth><name>Soft rock</name><powerUsage>0</powerUsage><sprite x="0" y="0" />
                <mass>1</mass><health>8</health><powerToDrill>1</powerToDrill>
                <dimensions><size w="1" h="1" d="1" /><modelSize w="0" h="0" d="0" /><modelPlacement x="0" y="0" z="0" /></dimensions>
                <activeTextures><walls x="0" y="0" w="1" h="1" /><top x="0" y="0" w="1" h="1" /><bottom x="0" y="0" w="1" h="1" /></activeTextures>
                <asteroidType>light</asteroidType>
              </asteroid>
            </asteroids>
          </blocks>
          <crafting>
            <blueprints>
              <blueprint name="Grind" usage="0">
                <ingredients><ingredient itemId="0" quantity="3" /></ingredients>
                <result itemId="10" quantity="4" />
              </blueprint>
              <blueprint name="Bake" usage="2">
                <ingredients><ingredient itemId="10" quantity="2" /></ingredients>
                <result itemId="10" quantity="1" />
              </blueprint>
            </blueprints>
          </crafting>
          <items>
            <ingredients>
              <ingredient itemId="10">
                <color r="1" g="2" b="3" /><description></description><maxStackSize>5</maxStackSize>
                <modelDepth>1</modelDepth><name>Grit</name><powerUsage>0</powerUsage><sprite x="1" y="0" />
              </ingredient>
            </ingredients>
            <drills>
              <drill itemId="20">
                <color r="1" g="2" b="3" /><description></description><maxStackSize>1</maxStackSize>
                <modelDepth>1</modelDepth><name>Hand drill</name><powerUsage>0</powerUsage><sprite x="2" y="0" />
                <power>4</power>
              </drill>
            </drills>
          </items>
        </itemset>
        """;

    static ItemsetDef Parse()
    {
        var diagnostics = new DiagnosticBag();
        var set = ItemsetParser.Parse(XDocument.Parse(Xml), "Blocksets/rulesItemset.xml", diagnostics);
        Assert.NotNull(set);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return set;
    }

    [Fact]
    public void Full_loop_from_parsed_itemset()
    {
        var set = Parse();
        var inventory = Inventory.ForItemset(set);
        Assert.Equal(50, inventory.MaxStackOf(0));
        Assert.Equal(5, inventory.MaxStackOf(10));

        // Spawn: the drill is endless (beside the inventory), one rock goes in; the unknown id 404 is skipped.
        Assert.Equal([20], StartingKit.Endless(set));
        Assert.Equal(0, StartingKit.Give(inventory, set));
        Assert.Equal(["0x1", "-"], inventory.Layout().Take(2));

        // Mine with the drill taken from its endless slot: power 4 beats powerToDrill 1 by 3: 0.8 - 0.3 = 0.5 s.
        inventory.Add(20, 1);
        var rock = set.ItemsById[0];
        var power = Mining.DrillPower(inventory, set);
        Assert.Equal(4, power);
        Assert.Equal(0.5, Mining.SecondsToMine(power, rock.Block!), precision: 10);
        var drop = Mining.Drop(set.ItemsByBlockId[0]);
        inventory.Add(drop.ItemId, drop.Quantity * 5);
        Assert.Equal(6, inventory.Count(0));

        // Craft at the table: 6 rock -> 2 runs -> 8 grit, which needs two slots at stack 5.
        var grind = Assert.Single(Crafting.For(set, CraftingStation.CraftingTable));
        Assert.Equal(2, Crafting.MaxCraftable(inventory, grind));
        Assert.True(Crafting.TryCraft(inventory, set, grind, times: 2));
        Assert.Equal(0, inventory.Count(0));
        Assert.Equal(8, inventory.Count(10));
        Assert.Equal(["10x5", "20x1", "10x3"], inventory.Layout().Take(3));

        var bake = Assert.Single(Crafting.For(set, CraftingStation.Smelter));
        Assert.Empty(Crafting.For(set, CraftingStation.Extractor));
        Assert.True(Crafting.TryCraft(inventory, set, bake, times: 3));
        Assert.Equal(5, inventory.Count(10));
    }
}
