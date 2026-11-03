namespace OpenCorneroids.Content.Tests.Byoa;

/// <summary>
/// Locates the developer's local BYOA folder: <c>OPENCORNEROIDS_BYOA</c>, else <c>byoa/</c> next to
/// <c>OpenCorneroids.sln</c>. The original files are never in the repo, so CI skips these tests.
/// </summary>
static class ByoaRoot
{
    public static readonly string? Root = Locate();

    public static readonly Lazy<ContentLibrary> Library = new(() => ContentLibrary.Scan([Root!]));

    public static string? SkipReason(string[] requiredPacks)
    {
        if (Root is null) return "no local BYOA folder (set OPENCORNEROIDS_BYOA or fill <repo>/byoa)";
        var missing = requiredPacks.Where(p => !Directory.Exists(Path.Combine(Root, p))).ToList();
        return missing.Count == 0 ? null : $"BYOA folder lacks: {string.Join(", ", missing)}";
    }

    static string? Locate()
    {
        if (Environment.GetEnvironmentVariable("OPENCORNEROIDS_BYOA") is { Length: > 0 } fromEnv)
            return Directory.Exists(fromEnv) ? fromEnv : null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "OpenCorneroids.sln"))) continue;
            var byoa = Path.Combine(dir.FullName, "byoa");
            return Directory.Exists(byoa) && Directory.EnumerateDirectories(byoa).Any() ? byoa : null;
        }
        return null;
    }
}

/// <summary>A fact that needs the listed pack folders in the local BYOA root; skipped otherwise.</summary>
sealed class ByoaFactAttribute : FactAttribute
{
    public ByoaFactAttribute(params string[] requiredPacks)
    {
        if (ByoaRoot.SkipReason(requiredPacks) is { } reason) Skip = reason;
    }
}

/// <summary>A theory that needs the listed pack folders in the local BYOA root; skipped otherwise.</summary>
sealed class ByoaTheoryAttribute : TheoryAttribute
{
    public ByoaTheoryAttribute(params string[] requiredPacks)
    {
        if (ByoaRoot.SkipReason(requiredPacks) is { } reason) Skip = reason;
    }
}
