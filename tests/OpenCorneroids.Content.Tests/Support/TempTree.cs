using System.Text;

namespace OpenCorneroids.Content.Tests.Support;

/// <summary>A scratch folder tree under the temp directory, deleted on dispose.</summary>
sealed class TempTree : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "open-corneroids-tests", Guid.NewGuid().ToString("N"));

    public TempTree() => Directory.CreateDirectory(Root);

    public string Write(string relative, string text) => Write(relative, new UTF8Encoding(false).GetBytes(text));

    public string Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string At(params string[] parts) => Path.Combine([Root, .. parts]);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
    }
}
