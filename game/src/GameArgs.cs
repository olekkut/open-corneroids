namespace OpenCorneroids.Game;

/// <summary>
/// User arguments, passed after "--" on the Godot command line, e.g.
/// <c>Godot --path game -- --itemset=auto --textures=SordsTextures --screenshot=shot.png</c>.
/// </summary>
public sealed class GameArgs
{
    /// <summary>An extra BYOA root to scan.</summary>
    public string? ByoaDir { get; private set; }

    /// <summary>Skip the menu: "auto" (the base install's itemset), an itemset key, or a list index.</summary>
    public string? Itemset { get; private set; }

    /// <summary>Texture pack ids to enable when skipping the menu.</summary>
    public List<string> TexturePacks { get; } = new();

    /// <summary>Save a screenshot of the world after it has rendered, then quit. Used to check the game runs.</summary>
    public string? ScreenshotPath { get; private set; }

    /// <summary>Place one of every placeable block in front of the player, through the normal edit path.</summary>
    public bool Showcase { get; private set; }

    public static GameArgs Parse(IEnumerable<string> args)
    {
        var parsed = new GameArgs();
        foreach (var arg in args)
        {
            var (name, value) = arg.Split('=', 2) is [var n, var v] ? (n, v) : (arg, "");
            switch (name)
            {
                case "--byoa-dir": parsed.ByoaDir = value; break;
                case "--itemset": parsed.Itemset = value.Length > 0 ? value : "auto"; break;
                case "--textures": parsed.TexturePacks.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries)); break;
                case "--screenshot": parsed.ScreenshotPath = value; break;
                case "--showcase": parsed.Showcase = true; break;
            }
        }
        return parsed;
    }
}
