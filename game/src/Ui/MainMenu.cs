using Godot;
using OpenCorneroids.Content;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Game.Content;

namespace OpenCorneroids.Game.Ui;

/// <summary>Lists the itemsets found in the BYOA folders and the texture packs that can be layered on top.</summary>
public partial class MainMenu : Control
{
    public event Action<ItemsetEntry, IReadOnlyList<PackInfo>>? PlayRequested;

    ContentLibrary _library = null!;
    ItemList _itemsets = null!;
    readonly List<(CheckBox Box, PackInfo Pack)> _texturePacks = new();

    public void Setup(ContentLibrary library, IReadOnlyList<string> roots, string? error)
    {
        _library = library;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0.03f, 0.035f, 0.06f) };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(720, 0) };
        column.AddThemeConstantOverride("separation", 10);
        center.AddChild(column);

        column.AddChild(Text("Open Corneroids", 40));
        column.AddChild(Text("Bring Your Own Assets — choose an itemset", 18, new Color(1, 1, 1, 0.7f)));
        if (error is not null) column.AddChild(Text(error, 15, new Color(1f, 0.5f, 0.45f), wrap: true));

        if (library.Itemsets.Count == 0)
        {
            column.AddChild(Text(
                "No Corneroids content found. Copy your Corneroids install and any mods into:\n\n" +
                $"    {ByoaPaths.UserRoot}\n\n" +
                "(see README.txt there), then restart.\n\nFolders searched:\n    " + string.Join("\n    ", roots),
                16, wrap: true));
            return;
        }

        _itemsets = new ItemList { CustomMinimumSize = new Vector2(0, 280) };
        _itemsets.AddThemeFontSizeOverride("font_size", 16);
        foreach (var entry in library.Itemsets)
        {
            var h = entry.Header;
            var tag = entry.Pack == library.BaseInstall ? "  (base)" : "";
            _itemsets.AddItem($"{h.Name} — {h.Author}    [{entry.Pack.Id}]{tag}");
        }
        int preferred = Math.Max(0, library.Itemsets.ToList().FindIndex(e => e.Pack == library.BaseInstall));
        _itemsets.Select(preferred);
        _itemsets.ItemActivated += index => Play((int)index);
        column.AddChild(_itemsets);

        var packs = library.Packs.Where(p => p.Kind == PackKind.TexturePack).ToList();
        if (packs.Count > 0)
        {
            column.AddChild(Text("Texture packs (layered over the itemset's own textures):", 15));
            foreach (var pack in packs)
            {
                var box = new CheckBox { Text = pack.Id };
                column.AddChild(box);
                _texturePacks.Add((box, pack));
            }
        }

        var play = new Button { Text = "Play", CustomMinimumSize = new Vector2(0, 44) };
        play.AddThemeFontSizeOverride("font_size", 20);
        play.Pressed += () => Play(_itemsets.GetSelectedItems() is [var selected, ..] ? selected : preferred);
        column.AddChild(play);

        var baseText = library.BaseInstall is { } b ? $"Base install: {b.Id} (vanilla {b.VanillaVersion})" : "No vanilla install found";
        column.AddChild(Text($"{baseText}.  {library.Packs.Count} packs in: {string.Join(", ", roots)}", 13, new Color(1, 1, 1, 0.5f), wrap: true));
        play.GrabFocus();
    }

    void Play(int index) =>
        PlayRequested?.Invoke(_library.Itemsets[index], _texturePacks.Where(t => t.Box.ButtonPressed).Select(t => t.Pack).ToList());

    static Label Text(string text, int size, Color? color = null, bool wrap = false)
    {
        var label = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        return label;
    }
}
