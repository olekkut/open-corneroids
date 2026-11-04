using OpenCorneroids.Simulation.Shapes;

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

    /// <summary>Place one of every placeable block in front of the player, through the normal edit path.
    /// Implies <see cref="Creative"/>, since a survival player could not have built it.</summary>
    public bool Showcase { get; private set; }

    /// <summary>Creative rules: instant mining, unlimited placing from the whole block palette, no inventory.</summary>
    public bool Creative { get; private set; }

    /// <summary>A window to open once the world has loaded ("inventory" or "crafting"), so screenshots can show it.</summary>
    public string? Open { get; private set; }

    /// <summary>Items added to the survival inventory at start, from <c>--give=itemId:count[,itemId:count...]</c>.</summary>
    public List<(int ItemId, int Count)> Give { get; } = new();

    /// <summary>Create a world with this name (from <see cref="Itemset"/>, or the base install's itemset) and save it
    /// at once, replacing any save of the same name.</summary>
    public string? NewWorld { get; private set; }

    /// <summary>Load the saved world with this name instead of showing the menu.</summary>
    public string? Load { get; private set; }

    /// <summary>Generator seed for a world created from the command line; <see cref="Persistence.WorldSaves.DefaultSeed"/> otherwise.</summary>
    public int? Seed { get; private set; }

    /// <summary>Save the world started from the command line right away and quit without rendering a screenshot.</summary>
    public bool QuitAfterSave { get; private set; }

    /// <summary>How multi-block face textures meet faces bigger than their region: stretched once (default) or repeated.</summary>
    public TextureMapping TextureMapping { get; private set; } = TextureMapping.Stretch;

    /// <summary>Initial orientation index (0..23) for placing multi-blocks.</summary>
    public int? Orientation { get; private set; }

    /// <summary>Select this item and aim at the asteroid so the placement ghost shows, for screenshots.</summary>
    public int? Preview { get; private set; }

    /// <summary>With <see cref="Preview"/>: place the previewed block there, through the normal placing rules (survival
    /// spends it), so a placed object can be saved and loaded without a mouse.</summary>
    public bool Place { get; private set; }

    /// <summary>Move the camera close to the first placed multi-block of this item, for screenshots.</summary>
    public int? Focus { get; private set; }

    /// <summary>Open every door once the world is up, and keep them open (no auto-close), for screenshots.</summary>
    public bool OpenDoors { get; private set; }

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
                case "--showcase": parsed.Showcase = parsed.Creative = true; break;
                case "--creative": parsed.Creative = true; break;
                case "--open": parsed.Open = value.ToLowerInvariant(); break;
                case "--give": parsed.Give.AddRange(ParseGive(value)); break;
                case "--new-world": parsed.NewWorld = value; break;
                case "--load": parsed.Load = value; break;
                case "--quit-after-save": parsed.QuitAfterSave = true; break;
                case "--open-doors": parsed.OpenDoors = true; break;
                case "--place": parsed.Place = true; break;
                case "--texture-mapping":
                    if (Enum.TryParse<TextureMapping>(value, ignoreCase: true, out var mapping) && Enum.IsDefined(mapping))
                        parsed.TextureMapping = mapping;
                    else Godot.GD.PushWarning($"--texture-mapping: '{value}' is not stretch or tile");
                    break;
                case "--orientation":
                    if (int.TryParse(value, out var orientation) && Simulation.Shapes.Orientation.IsValidIndex(orientation))
                        parsed.Orientation = orientation;
                    else Godot.GD.PushWarning($"--orientation: '{value}' is not a whole number from 0 to 23");
                    break;
                case "--preview":
                    if (int.TryParse(value, out var preview)) parsed.Preview = preview;
                    else Godot.GD.PushWarning($"--preview: '{value}' is not an item id");
                    break;
                case "--focus":
                    if (int.TryParse(value, out var focus)) parsed.Focus = focus;
                    else Godot.GD.PushWarning($"--focus: '{value}' is not an item id");
                    break;
                case "--seed":
                    if (int.TryParse(value, out var seed)) parsed.Seed = seed;
                    else Godot.GD.PushWarning($"--seed: '{value}' is not a whole number");
                    break;
            }
        }
        return parsed;
    }

    /// <summary>"55:1,10:5" → (55,1), (10,5). A bare id gives one; malformed entries are skipped with a warning.</summary>
    static IEnumerable<(int, int)> ParseGive(string value)
    {
        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 2);
            int count = 1;
            if (int.TryParse(parts[0], out var itemId) && (parts.Length == 1 || int.TryParse(parts[1], out count)) && count > 0)
                yield return (itemId, count);
            else
                Godot.GD.PushWarning($"--give: ignoring '{entry}', expected itemId:count");
        }
    }
}
