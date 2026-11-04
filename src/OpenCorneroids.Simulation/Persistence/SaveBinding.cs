using OpenCorneroids.Content.Itemsets;

namespace OpenCorneroids.Simulation.Persistence;

public enum BindingStatus
{
    /// <summary>Same itemset content as when the world was saved.</summary>
    Exact,
    /// <summary>Same itemset key but edited content; every block the world uses still exists, so it can load with a warning.</summary>
    ContentChanged,
    /// <summary>Same itemset key, but blocks the world uses were removed; loading would lose or misplace them.</summary>
    Incompatible,
    /// <summary>A different itemset altogether; its blockIds mean unrelated blocks.</summary>
    DifferentItemset,
}

/// <param name="MissingBlockIds">Used blockIds the current itemset lacks, ascending. Only non-empty for <see cref="BindingStatus.Incompatible"/>.</param>
/// <param name="Message">Explains the status to the player.</param>
public sealed record BindingResult(BindingStatus Status, IReadOnlyList<int> MissingBlockIds, string Message);

/// <summary>
/// Decides whether a saved world can be loaded with the itemset that is active now. Worlds store ORIGINAL blockIds, so
/// an edited mod keeps working as long as the blocks actually placed survive; palette entries no cell references
/// (left over after mining) don't count.
/// </summary>
public static class SaveBinding
{
    public static BindingResult Check(WorldSnapshot snapshot, ItemsetDef current, string currentPackId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(current);
        var saved = snapshot.Itemset;

        if (!string.Equals(saved.Key, current.Header.Key, StringComparison.Ordinal))
            return new(BindingStatus.DifferentItemset, [],
                $"This world was made with itemset '{saved.Name}' ({saved.Key}) from '{saved.PackId}', but '{current.Header.Name}' ({current.Header.Key}) from '{currentPackId}' is active.");

        if (string.Equals(saved.Fingerprint, current.Fingerprint, StringComparison.OrdinalIgnoreCase))
            return new(BindingStatus.Exact, [], $"Itemset '{current.Header.Name}' matches the one this world was saved with.");

        var missing = UsedBlockIds(snapshot).Where(id => !current.ItemsByBlockId.ContainsKey(id)).Order().ToArray();
        if (missing.Length > 0)
            return new(BindingStatus.Incompatible, missing,
                $"Itemset '{current.Header.Name}' from '{currentPackId}' no longer defines {missing.Length} block type(s) this world uses (blockIds {string.Join(", ", missing)}).");

        return new(BindingStatus.ContentChanged, [],
            $"Itemset '{current.Header.Name}' from '{currentPackId}' has changed since this world was saved (it was from '{saved.PackId}'); every block the world uses still exists, but their properties may differ.");
    }

    /// <summary>Distinct blockIds referenced by at least one cell or placed object, air excluded.</summary>
    public static IReadOnlySet<int> UsedBlockIds(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var palette = snapshot.Palette;
        var referenced = new bool[palette.Count];
        foreach (var cell in snapshot.Cells)
        {
            if (cell >= referenced.Length)
                throw new ArgumentException($"Cell value {cell} is outside the {palette.Count}-entry palette.", nameof(snapshot));
            referenced[cell] = true;
        }

        var used = new HashSet<int>();
        for (int i = 0; i < referenced.Length; i++)
            if (referenced[i] && palette[i] != WorldSnapshot.AirBlockId) used.Add(palette[i]);
        foreach (var placed in snapshot.Objects) used.Add(placed.BlockId);
        return used;
    }
}
