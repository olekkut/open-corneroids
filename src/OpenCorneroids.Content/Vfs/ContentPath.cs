namespace OpenCorneroids.Content.Vfs;

/// <summary>
/// Content paths are relative to a pack's <c>Content/</c> folder (e.g. <c>Textures/Blocks/blockTextures.png</c>),
/// use '/', and compare case-insensitively because mods are authored on Windows.
/// </summary>
public static class ContentPath
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static string Normalize(string path) =>
        string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(p => p != "."));

    public static string Combine(string first, string second) => Normalize(first + "/" + second);

    public static string Directory(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }
}
