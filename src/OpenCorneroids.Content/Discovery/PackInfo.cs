using OpenCorneroids.Content.Vfs;

namespace OpenCorneroids.Content.Discovery;

public enum PackKind
{
    /// <summary>A full install or repack: <c>Corneroids.exe</c> next to <c>Content/</c>. Can serve as the base install.</summary>
    GameInstall,
    /// <summary>Itemset XML (and usually atlases) without the game.</summary>
    ItemsetMod,
    /// <summary>Textures only: a mirrored <c>Content/Textures</c> tree, or loose atlas PNGs.</summary>
    TexturePack,
}

/// <summary>One dropped folder or archive. <see cref="RootPath"/> is the folder or archive itself; <see cref="Source"/> is its content.</summary>
public sealed record PackInfo(string Id, PackKind Kind, string RootPath, IContentSource Source, Version? VanillaVersion)
{
    public bool IsVanilla => VanillaVersion is not null;

    public IEnumerable<string> ItemsetPaths => Kind == PackKind.TexturePack ? [] : Source.List("Blocksets", ".xml");
}

/// <summary>Fingerprints of unmodified vanilla installs (SHA-256 of <c>Blocksets/defaultItemset.xml</c> as shipped).</summary>
public static class KnownVanilla
{
    static readonly Dictionary<string, Version> ByDefaultItemsetSha256 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["603db3ec811b6776baf17af13fed07df72d3104c0148378b7689e0160c455244"] = new(1, 2, 0),
        ["1cf146df6733e591c167cb85ced03987c74ff2cfd150ccc17487bc84aca8946e"] = new(1, 0, 1, 2),
    };

    public static Version? Identify(string sha256) => ByDefaultItemsetSha256.GetValueOrDefault(sha256);
}
