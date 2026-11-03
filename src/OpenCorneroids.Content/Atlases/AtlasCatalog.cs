using System.Xml.Linq;
using OpenCorneroids.Content.Vfs;
using OpenCorneroids.Content.Xml;

namespace OpenCorneroids.Content.Atlases;

public enum AtlasKind { Blocks, Sprites }

/// <param name="TexturePath">Content path, e.g. <c>Textures/Blocks/blockTextures.png</c>.</param>
/// <param name="BlockSize">Cell size in pixels of the texture the declaring pack was authored against.</param>
/// <param name="DeclaringPack">The pack whose registry file supplied this entry.</param>
public sealed record AtlasDef(
    string Key, string Name, AtlasKind Kind, string TexturePath, string? LightTexturePath, int BlockSize, string DeclaringPack);

/// <summary>
/// Atlas registry for one view. Every mod ships <c>DefaultBlocks.xml</c> and <c>SpriteTextureAtlases.xml</c> as
/// "default + mine", so in the original game installing two mods dropped one of them. Here the files from every
/// layer are merged by key, the upper layer winning per key.
/// </summary>
public sealed class AtlasCatalog
{
    public const string BlockRegistryPath = "Textures/Blocks/DefaultBlocks.xml";
    public const string SpriteRegistryPath = "Textures/Sprites/SpriteTextureAtlases.xml";

    readonly Dictionary<string, AtlasDef> _blocks = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AtlasDef> _sprites = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, AtlasDef> BlockAtlases => _blocks;
    public IReadOnlyDictionary<string, AtlasDef> SpriteAtlases => _sprites;

    public static AtlasCatalog Build(LayeredSource view, DiagnosticBag diagnostics)
    {
        var catalog = new AtlasCatalog();
        foreach (var layer in view.Layers.Reverse())
        {
            Read(layer, BlockRegistryPath, "blockTextureAtlases", AtlasKind.Blocks, catalog._blocks, diagnostics);
            Read(layer, SpriteRegistryPath, "spriteTextureAtlases", AtlasKind.Sprites, catalog._sprites, diagnostics);
        }
        return catalog;
    }

    static void Read(IContentSource layer, string path, string rootName, AtlasKind kind,
        Dictionary<string, AtlasDef> into, DiagnosticBag diagnostics)
    {
        if (!layer.Exists(path)) return;
        var file = $"{layer.PackId}:{path}";
        var bag = new DiagnosticBag();
        var doc = XmlInput.TryLoad(layer, path, bag);
        foreach (var d in bag) diagnostics.Add(d with { File = file });
        if (doc?.Root is not { } root) return;

        var r = new FieldReader(file, diagnostics);
        if (root.Name.LocalName != rootName)
        {
            r.Report(Severity.Error, Codes.UnexpectedRoot, $"expected <{rootName}>, found <{root.Name.LocalName}>", root);
            return;
        }
        foreach (var atlas in root.Elements("atlas"))
        {
            var key = r.Text(atlas, "key");
            var texture = r.Text(atlas, "texture");
            if (key.Length == 0 || texture.Length == 0)
            {
                r.Report(Severity.Error, Codes.MissingField, "<atlas> needs <key> and <texture>; skipped", atlas);
                continue;
            }
            var light = r.Text(atlas, "lightTexture");
            into[key] = new AtlasDef(key, r.Text(atlas, "name"), kind, ContentPath.Normalize(texture),
                light.Length > 0 ? ContentPath.Normalize(light) : null, r.Int(atlas, "blockSize", 32), layer.PackId);
        }
    }
}
