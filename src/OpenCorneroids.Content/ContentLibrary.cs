using OpenCorneroids.Content.Atlases;
using OpenCorneroids.Content.Discovery;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Content.Vfs;

namespace OpenCorneroids.Content;

/// <summary>An itemset found during a scan; only its header has been read.</summary>
public sealed record ItemsetEntry(PackInfo Pack, string Path, ItemsetHeader Header);

/// <summary>A fully loaded itemset together with the view and atlases it resolved through.</summary>
public sealed record LoadedItemset(
    ItemsetEntry Entry, ItemsetDef? Itemset, LayeredSource View, AtlasCatalog Atlases,
    AtlasGrid? BlockAtlas, AtlasGrid? SpriteAtlas, DiagnosticBag Diagnostics);

/// <summary>
/// Entry point: scan BYOA roots, list itemsets, load one through its layered view
/// (enabled texture packs, then the itemset's own pack, then the base install).
/// </summary>
public sealed class ContentLibrary
{
    ContentLibrary(IReadOnlyList<PackInfo> packs, PackInfo? baseInstall, IReadOnlyList<ItemsetEntry> itemsets, DiagnosticBag diagnostics)
    {
        Packs = packs;
        BaseInstall = baseInstall;
        Itemsets = itemsets;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<PackInfo> Packs { get; }
    /// <summary>Supplies files a mod relies on but doesn't ship (Nav's default atlases, for one). Newest vanilla by default.</summary>
    public PackInfo? BaseInstall { get; }
    public IReadOnlyList<ItemsetEntry> Itemsets { get; }
    public DiagnosticBag Diagnostics { get; }

    public static ContentLibrary Scan(IEnumerable<string> roots, string? baseInstallId = null)
    {
        var diagnostics = new DiagnosticBag();
        var packs = PackScanner.Scan(roots, diagnostics);

        var baseInstall = baseInstallId is not null
            ? packs.FirstOrDefault(p => p.Id.Equals(baseInstallId, StringComparison.OrdinalIgnoreCase))
            : packs.Where(p => p.IsVanilla).MaxBy(p => p.VanillaVersion);
        if (baseInstall is null && packs.Count > 0)
            diagnostics.Report(Severity.Warning, Codes.NoBaseInstall, baseInstallId is null
                ? "no unmodified vanilla install found; mods that rely on default atlases may not resolve"
                : $"base install '{baseInstallId}' not found");

        var itemsets = new List<ItemsetEntry>();
        foreach (var pack in packs)
        foreach (var path in pack.ItemsetPaths)
        {
            var bag = new DiagnosticBag();
            var header = ItemsetParser.ScanHeader(pack.Source, path, bag);
            foreach (var d in bag) diagnostics.Add(d with { File = $"{pack.Id}:{path}" });
            if (header is not null) itemsets.Add(new ItemsetEntry(pack, path, header));
        }
        return new ContentLibrary(packs, baseInstall, itemsets, diagnostics);
    }

    public PackInfo? FindPack(string id) => Packs.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public LayeredSource CreateView(PackInfo pack, IEnumerable<PackInfo>? texturePacks = null)
    {
        var layers = new List<IContentSource>();
        layers.AddRange((texturePacks ?? []).Where(t => t != pack).Select(t => t.Source));
        layers.Add(pack.Source);
        if (BaseInstall is not null && BaseInstall != pack) layers.Add(BaseInstall.Source);
        return new LayeredSource(layers);
    }

    public LoadedItemset Load(ItemsetEntry entry, IEnumerable<PackInfo>? texturePacks = null)
    {
        var diagnostics = new DiagnosticBag();
        var view = CreateView(entry.Pack, texturePacks);
        var file = $"{entry.Pack.Id}:{entry.Path}";

        var parseBag = new DiagnosticBag();
        var itemset = ItemsetParser.Parse(entry.Pack.Source, entry.Path, parseBag);
        var atlases = AtlasCatalog.Build(view, parseBag);
        var blocks = Resolve(entry.Header.BlockTextureKey, atlases.BlockAtlases, "block", view, parseBag, file);
        var sprites = Resolve(entry.Header.SpriteTextureKey, atlases.SpriteAtlases, "sprite", view, parseBag, file);
        if (itemset is not null) ItemsetValidator.Validate(itemset, blocks, sprites, parseBag);

        // Itemset diagnostics carry the bare content path; qualify them with the pack so they're unambiguous.
        foreach (var d in parseBag)
            diagnostics.Add(d.File == entry.Path ? d with { File = file } : d);
        return new LoadedItemset(entry, itemset, view, atlases, blocks, sprites, diagnostics);
    }

    static AtlasGrid? Resolve(string? key, IReadOnlyDictionary<string, AtlasDef> atlases, string kind,
        LayeredSource view, DiagnosticBag diagnostics, string file)
    {
        if (key is null) return null;
        if (atlases.TryGetValue(key, out var def)) return AtlasGrid.Load(def, view, diagnostics);
        diagnostics.Report(Severity.Error, Codes.AtlasMissing, $"{kind} atlas '{key}' isn't declared anywhere in {view.PackId}", file);
        return null;
    }
}
