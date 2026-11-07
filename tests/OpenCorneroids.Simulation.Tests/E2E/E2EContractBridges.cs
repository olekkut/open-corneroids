using System.IO.Compression;
using System.Reflection;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Persistence;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.E2E;

/// <summary>
/// Dynamic invocation bridges for anticipated contracts from PROJECT.md § Interface Contracts.
/// Enables tests to compile cleanly across milestone progression, executing real logic
/// as soon as milestones land while safely returning early if a milestone is pending.
/// </summary>
public static class WrlReaderBridge
{
    private static readonly Type? ReaderType =
        Type.GetType("OpenCorneroids.Simulation.Persistence.CorneroidsWrlReader, OpenCorneroids.Simulation");

    public static bool IsImplemented => ReaderType != null;

    public static bool RequireImplemented() => IsImplemented;

    public static WorldSnapshot Read(string sqliteFilePath, string? defaultItemsetKey = null)
    {
        if (!IsImplemented)
            throw new InvalidOperationException("CorneroidsWrlReader is not yet implemented (Milestone M1 in progress).");

        var method = ReaderType!.GetMethod("Read", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(string)]);
        Assert.NotNull(method);
        try
        {
            return (WorldSnapshot)method.Invoke(null, [sqliteFilePath, defaultItemsetKey])!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }

    public static WorldSnapshot ReadFromConnection(SqliteConnection connection, string? defaultItemsetKey = null)
    {
        if (!IsImplemented)
            throw new InvalidOperationException("CorneroidsWrlReader is not yet implemented (Milestone M1 in progress).");

        var method = ReaderType!.GetMethod("ReadFromConnection", BindingFlags.Public | BindingFlags.Static, [typeof(SqliteConnection), typeof(string)]);
        Assert.NotNull(method);
        try
        {
            return (WorldSnapshot)method.Invoke(null, [connection, defaultItemsetKey])!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }
}

public static class SpaceboxBridge
{
    private static readonly Type? BridgeType =
        Type.GetType("OpenCorneroids.Simulation.Persistence.SpaceboxStructureBridge, OpenCorneroids.Simulation");

    public static bool IsImplemented => BridgeType != null;

    public static bool RequireImplemented() => IsImplemented;

    public static ShipSnapshot Import(Stream sbsStream, string name = "Imported Structure")
    {
        if (!IsImplemented)
            throw new InvalidOperationException("SpaceboxStructureBridge is not yet implemented (Milestone M3 in progress).");

        var method = BridgeType!.GetMethod("Import", BindingFlags.Public | BindingFlags.Static, [typeof(Stream), typeof(string)]);
        Assert.NotNull(method);
        try
        {
            return (ShipSnapshot)method.Invoke(null, [sbsStream, name])!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }

    public static void Export(ShipSnapshot ship, Stream destinationStream, string author = "OpenCorneroids")
    {
        if (!IsImplemented)
            throw new InvalidOperationException("SpaceboxStructureBridge is not yet implemented (Milestone M3 in progress).");

        var method = BridgeType!.GetMethod("Export", BindingFlags.Public | BindingFlags.Static, [typeof(ShipSnapshot), typeof(Stream), typeof(string)]);
        Assert.NotNull(method);
        try
        {
            method.Invoke(null, [ship, destinationStream, author]);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }
}

public static class NbtBridge
{
    private static readonly Type? NbtCompoundType =
        Type.GetType("OpenCorneroids.Simulation.Persistence.Nbt.NbtCompound, OpenCorneroids.Simulation");

    public static bool IsImplemented => NbtCompoundType != null;

    public static bool RequireImplemented() => IsImplemented;
}
