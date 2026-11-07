using System.IO.Compression;
using System.Reflection;
using OpenCorneroids.Content.Vfs;
using Xunit;

namespace OpenCorneroids.Content.Tests.E2E;

/// <summary>
/// Dynamic invocation bridge for Milestone M2 (Direct Archive Mod Loading).
/// </summary>
public static class ZipSourceBridge
{
    private static readonly Type? ZipSourceType =
        Type.GetType("OpenCorneroids.Content.Vfs.ZipSource, OpenCorneroids.Content");

    public static bool IsImplemented => ZipSourceType != null;

    public static bool RequireImplemented() => IsImplemented;

    public static IContentSource FromArchive(string packId, string archivePath, string contentRootPrefix = "")
    {
        if (!IsImplemented)
            throw new InvalidOperationException("ZipSource is not yet implemented (Milestone M2 in progress).");

        var method = ZipSourceType!.GetMethod("FromArchive", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(string), typeof(string)]);
        Assert.NotNull(method);
        try
        {
            return (IContentSource)method.Invoke(null, [packId, archivePath, contentRootPrefix])!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }
}
