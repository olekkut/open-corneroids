// Lists every pack and itemset in one or more BYOA roots, loads each itemset through its view, and prints what
// resolved and what went wrong. Exit code 1 if anything failed with an error.
//
//   dotnet run --project tools/OpenCorneroids.ContentDump -- [--verbose] [--textures <packId>...] [root...]
// With no root, uses byoa/ next to OpenCorneroids.sln.
using OpenCorneroids.Content;

var verbose = args.Contains("--verbose");
var texturePackIds = args.SkipWhile(a => a != "--textures").Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList();
var roots = args.Where(a => !a.StartsWith("--") && !texturePackIds.Contains(a)).ToList();
if (roots.Count == 0 && FindDefaultRoot() is { } defaultRoot) roots.Add(defaultRoot);
if (roots.Count == 0)
{
    Console.Error.WriteLine("No BYOA root given and no byoa/ folder found next to OpenCorneroids.sln.");
    return 2;
}

var library = ContentLibrary.Scan(roots);
var texturePacks = texturePackIds.Select(id => library.FindPack(id) ?? throw new ArgumentException($"no pack '{id}'")).ToList();

Console.WriteLine($"Roots: {string.Join(", ", roots)}");
Console.WriteLine($"Base install: {library.BaseInstall?.Id ?? "(none)"}");
Console.WriteLine();
Console.WriteLine("Packs:");
foreach (var pack in library.Packs)
{
    var vanilla = pack.VanillaVersion is { } v ? $"  vanilla {v}" : "";
    Console.WriteLine($"  {pack.Kind,-12} {pack.Id}{vanilla}  ({pack.Source.Paths.Count} files)");
}

var failed = library.Diagnostics.HasErrors;
Console.WriteLine();
Console.WriteLine("Itemsets:");
foreach (var entry in library.Itemsets)
{
    var loaded = library.Load(entry, texturePacks);
    var h = entry.Header;
    Console.WriteLine($"  {entry.Pack.Id}:{entry.Path}");
    Console.WriteLine($"    \"{h.Name}\" by {h.Author}  key={h.Key} version={h.Version}");
    if (loaded.Itemset is { } set)
        Console.WriteLine($"    {set.Items.Count} items, {set.Blocks.Count()} blocks, {set.Blueprints.Count} blueprints, " +
                          $"{set.Projectiles.Count} projectiles  fingerprint {set.Fingerprint[..12]}");
    Console.WriteLine($"    view: {loaded.View.PackId}");
    foreach (var atlas in new[] { loaded.BlockAtlas, loaded.SpriteAtlas }.OfType<OpenCorneroids.Content.Atlases.AtlasGrid>())
        Console.WriteLine($"    {atlas.Def.Kind,-7} '{atlas.Def.Key}' declared by {atlas.Def.DeclaringPack}, " +
                          $"{atlas.Def.TexturePath} served by {atlas.ServedBy} ({atlas.Columns}x{atlas.Rows} cells of {atlas.CellPixels}px)");
    Print(loaded.Diagnostics, "    ");
    failed |= loaded.Diagnostics.HasErrors;
}

if (library.Diagnostics.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Scan diagnostics:");
    Print(library.Diagnostics, "  ");
}
return failed ? 1 : 0;

void Print(DiagnosticBag diagnostics, string indent)
{
    var shown = diagnostics.Where(d => verbose || d.Severity > Severity.Info).ToList();
    foreach (var d in shown) Console.WriteLine(indent + d);
    var hidden = diagnostics.Count - shown.Count;
    if (hidden > 0) Console.WriteLine($"{indent}({hidden} info diagnostics; --verbose to show)");
}

static string? FindDefaultRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "OpenCorneroids.sln")))
            return Directory.Exists(Path.Combine(dir.FullName, "byoa")) ? Path.Combine(dir.FullName, "byoa") : null;
    return null;
}
