namespace OpenCorneroids.Content.Tests.Support;

/// <summary>
/// A small hand-written itemset in the original format that exercises the tricky parts: reused tag names, optional
/// fields, decimals, comments, and an empty category. No original game data is used.
/// </summary>
static class MiniItemset
{
    public const string Path = "Blocksets/miniItemset.xml";

    public static string Xml(string blocksExtra = "", string itemsExtra = "", string weaponExtra = "", string asteroidExtra = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <itemset version="3">
          <name>Mini</name>
          <author>Tests</author>
          <key>MiniItemset</key>
          <defaultItems>
            <item itemId="0" />
            <item itemId="20" />
          </defaultItems>
          <textures>
            <blockTexture><name>Mini blocks</name><key>miniblocks</key></blockTexture>
            <spriteTexture><name>Mini sprites</name><key>minisprites</key></spriteTexture>
          </textures>
          <blocks>
            <!-- comments must be ignored -->
            <asteroids>
              <asteroid itemId="0" blockId="0">
                {Common("Light rock")}
                {BlockFields("0 0 1 1")}
                <asteroidType>light</asteroidType>
                {asteroidExtra}
              </asteroid>
            </asteroids>
            <crafting>
              <crafting itemId="1" blockId="1" type="smelter">
                {Common("Furnace")}
                {BlockFields("1 0 1 1")}
              </crafting>
            </crafting>
            <power>
              <power itemId="2" blockId="2">
                {Common("Cable")}
                {BlockFields("2 0 1 1")}
                <power>-1</power>
                <deactiveTextures><walls x="3" y="0" w="1" h="1" /><top x="3" y="0" w="1" h="1" /><bottom x="3" y="0" w="1" h="1" /></deactiveTextures>
              </power>
            </power>
            <doors>
              <door itemId="3" blockId="3">
                {Common("Door")}
                {BlockFields("0 1 2 2", size: "2 4 1", model: "2 2 1", placement: "0 -1.5 0")}
                <door><direction x="0" y="1" z="0" /><distance>2</distance><speed>4</speed><time>2000</time></door>
              </door>
            </doors>
            <pointLights>
              <pointLight itemId="4" blockId="4">
                {Common("Torch block")}
                {BlockFields("2 1 1 1", mass: "0.2")}
                <light><color r="128" g="128" b="255" /><radius>5</radius></light>
              </pointLight>
            </pointLights>
            <spotLights />
            {blocksExtra}
          </blocks>
          <projectiles>
            <projectile projectileId="1">
              <color r="0" g="0" b="255" />
              <damage>2</damage>
              <lifetime>3000</lifetime>
              <ricochetAngle>0.7854</ricochetAngle>
              <speed>50</speed>
              <size w="0.05" h="0.5" d="0.05" />
            </projectile>
          </projectiles>
          <crafting>
            <blueprints>
              <blueprint name="Smelt rock" usage="2">
                <ingredients><ingredient itemId="0" quantity="2" /></ingredients>
                <result itemId="10" quantity="1" />
              </blueprint>
            </blueprints>
          </crafting>
          <items>
            <ingredients>
              <ingredient itemId="10">{Common("Rock dust", "1 0")}</ingredient>
            </ingredients>
            <drills>
              <drill itemId="20">
                {Common("Drill", "0 0")}
                <light type="point"><color r="255" g="255" b="0" /><radius>3</radius></light>
                <power>2</power>
              </drill>
            </drills>
            <weapons>
              <weapon itemId="21">
                {Common("Pistol", "1 0")}
                <reloadTime>200</reloadTime>
                <projectileId>1</projectileId>
                <pushback>0.025</pushback>
                <spread>0.05</spread>
                {weaponExtra}
              </weapon>
            </weapons>
            {itemsExtra}
          </items>
        </itemset>
        """;

    public static string Common(string name, string sprite = "0 0")
    {
        var xy = sprite.Split(' ');
        return $"""
            <color r="10" g="20" b="30" />
            <description></description>
            <maxStackSize>64</maxStackSize>
            <modelDepth>1</modelDepth>
            <name>{name}</name>
            <powerUsage>0</powerUsage>
            <sprite x="{xy[0]}" y="{xy[1]}" />
            """;
    }

    public static string BlockFields(string walls, string size = "1 1 1", string model = "0 0 0", string placement = "0 0 0", string mass = "1")
    {
        string Rect(string tag, string r) { var v = r.Split(' '); return $"""<{tag} x="{v[0]}" y="{v[1]}" w="{v[2]}" h="{v[3]}" />"""; }
        var s = size.Split(' ');
        var m = model.Split(' ');
        var p = placement.Split(' ');
        return $"""
            <mass>{mass}</mass>
            <health>5</health>
            <powerToDrill>1</powerToDrill>
            <dimensions>
              <size w="{s[0]}" h="{s[1]}" d="{s[2]}" />
              <modelSize w="{m[0]}" h="{m[1]}" d="{m[2]}" />
              <modelPlacement x="{p[0]}" y="{p[1]}" z="{p[2]}" />
            </dimensions>
            <activeTextures>{Rect("walls", walls)}{Rect("top", walls)}{Rect("bottom", walls)}</activeTextures>
            """;
    }

    /// <summary>Atlas registries for the mini itemset: 4x4 grids of 8 px cells.</summary>
    public static MemorySource WithAtlases(this MemorySource source, Func<int, int, byte>? blockAlpha = null)
    {
        source.Add("Textures/Blocks/DefaultBlocks.xml", """
            <blockTextureAtlases>
              <atlas><name>Mini blocks</name><key>miniblocks</key><texture>Textures/Blocks/mini.png</texture><blockSize>8</blockSize></atlas>
            </blockTextureAtlases>
            """);
        source.Add("Textures/Sprites/SpriteTextureAtlases.xml", """
            <spriteTextureAtlases>
              <atlas><name>Mini sprites</name><key>minisprites</key><texture>Textures/Sprites/mini.png</texture><blockSize>8</blockSize></atlas>
            </spriteTextureAtlases>
            """);
        source.Add("Textures/Blocks/mini.png", TestPng.Atlas(4, 4, 8, blockAlpha ?? ((_, _) => 255)));
        source.Add("Textures/Sprites/mini.png", TestPng.Atlas(4, 4, 8, (_, _) => 255));
        return source;
    }
}
