using System.Text;

namespace OpenCorneroids.Simulation.Settings;

/// <summary>
/// The current value of every setting. One store serves a whole game session: world rules are reset and reloaded
/// when a world loads, player preferences are kept across worlds. Not thread-safe; read and write it on one thread.
/// </summary>
public sealed class CvarStore
{
    readonly Dictionary<string, Cvar> _byName = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Cvar> _ordered = [];
    readonly Dictionary<Cvar, double> _values = [];

    /// <param name="definitions">The settings this store holds; <see cref="Cvars.All"/> when null.</param>
    public CvarStore(IEnumerable<Cvar>? definitions = null)
    {
        foreach (var cvar in definitions ?? Cvars.All)
        {
            if (!_byName.TryAdd(cvar.Name, cvar))
                throw new ArgumentException($"Setting '{cvar.Name}' is defined twice.", nameof(definitions));
            _ordered.Add(cvar);
            _values[cvar] = cvar.Default;
        }
    }

    /// <summary>Raised after a value really changed, with the new value.</summary>
    public event Action<Cvar, double>? Changed;

    /// <summary>Every setting, in definition order.</summary>
    public IReadOnlyList<Cvar> All => _ordered;

    /// <summary>
    /// True once <c>sv_cheats</c> has been 1 in this world, even if it was turned off again. Saved with the world, so
    /// a world played with cheats can always be told apart.
    /// </summary>
    public bool CheatsUsed { get; set; }

    public bool CheatsEnabled => _values.TryGetValue(Cvars.Cheats, out var on) && on != 0;

    public Cvar? Find(string name) => _byName.GetValueOrDefault((name ?? "").Trim());

    public double Get(Cvar cvar) => _values.TryGetValue(cvar, out var value)
        ? value
        : throw new ArgumentException($"Setting '{cvar.Name}' is not in this store.", nameof(cvar));

    public bool Bool(Cvar cvar) => Get(cvar) != 0;
    public float Float(Cvar cvar) => (float)Get(cvar);
    public int Int(Cvar cvar) => (int)Math.Round(Get(cvar));

    public bool IsDefault(Cvar cvar) => Get(cvar) == cvar.Default;

    /// <summary>Sets <paramref name="cvar"/> from console text; false with a reason the player can read.</summary>
    public bool TrySet(Cvar cvar, string text, out string? error)
    {
        ArgumentNullException.ThrowIfNull(cvar);
        if (!cvar.TryParse(text, out var value, out error)) return false;
        return TrySet(cvar, value, out error);
    }

    /// <summary>Sets <paramref name="cvar"/>, refusing values outside its range and cheats while cheats are off.</summary>
    public bool TrySet(Cvar cvar, double value, out string? error)
    {
        ArgumentNullException.ThrowIfNull(cvar);
        error = null;
        if (!_values.ContainsKey(cvar))
        {
            error = $"Setting '{cvar.Name}' is not in this store.";
            return false;
        }
        if (cvar.Kind == CvarKind.Bool) value = value != 0 ? 1 : 0;
        if (!double.IsFinite(value) || value < cvar.Min || value > cvar.Max ||
            (cvar.Kind == CvarKind.Int && value != Math.Round(value)))
        {
            error = $"{cvar.Format(value)} is outside {cvar.Name}'s range {cvar.RangeText}.";
            return false;
        }
        if (cvar.IsCheat && !CheatsEnabled && value != cvar.Default)
        {
            error = $"{cvar.Name} is a cheat; set sv_cheats 1 first.";
            return false;
        }
        Assign(cvar, value);
        return true;
    }

    /// <summary>Sets a value from code; throws when it is invalid. Cheat settings still need <c>sv_cheats</c>.</summary>
    public void Set(Cvar cvar, double value)
    {
        if (!TrySet(cvar, value, out var error)) throw new ArgumentException(error, nameof(value));
    }

    public void Set(Cvar cvar, bool value) => Set(cvar, value ? 1 : 0);

    public void Reset(Cvar cvar) => Assign(cvar, cvar.Default);

    /// <summary>Resets every setting that has any of the <paramref name="scope"/> flags (every setting for None).</summary>
    public void ResetAll(CvarFlags scope = CvarFlags.None)
    {
        foreach (var cvar in _ordered.Where(c => InScope(c, scope)).ToList()) Assign(cvar, cvar.Default);
    }

    /// <summary>Settings in <paramref name="scope"/> that are not at their default, in definition order.</summary>
    public IReadOnlyList<Cvar> Differences(CvarFlags scope = CvarFlags.None) =>
        [.. _ordered.Where(c => InScope(c, scope) && !IsDefault(c))];

    /// <summary>Changed settings in <paramref name="scope"/> as name → value text, for a save file.</summary>
    public IReadOnlyDictionary<string, string> Export(CvarFlags scope) =>
        Differences(scope).ToDictionary(c => c.Name, c => c.Format(Get(c)), StringComparer.Ordinal);

    /// <summary>
    /// Applies saved values (from <see cref="Export"/>) to the settings in <paramref name="scope"/>; everything else in
    /// scope goes back to its default first, so a loaded world has exactly the rules it was saved with. Returns what
    /// could not be applied (unknown names from a newer build, values out of range); those keep their defaults.
    /// </summary>
    public IReadOnlyList<string> Import(IReadOnlyDictionary<string, string> saved, CvarFlags scope)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ResetAll(scope);
        var problems = new List<string>();
        // sv_cheats first, so the cheat settings saved beside it can be applied.
        foreach (var (name, text) in saved.OrderBy(p => Find(p.Key) == Cvars.Cheats ? 0 : 1))
        {
            if (Find(name) is not { } cvar)
                problems.Add($"unknown setting '{name}' ignored");
            else if (!InScope(cvar, scope))
                problems.Add($"'{name}' does not belong here and was ignored");
            else if (!TrySet(cvar, text, out var error))
                problems.Add(error!);
        }
        return problems;
    }

    /// <summary>
    /// A config file holding the changed settings in <paramref name="scope"/>, one <c>name "value"</c> per line, which
    /// <c>exec</c> reads back.
    /// </summary>
    public string WriteConfig(CvarFlags scope, string header)
    {
        var text = new StringBuilder();
        foreach (var line in (header ?? "").Split('\n'))
            text.Append("// ").Append(line.TrimEnd('\r')).Append('\n');
        foreach (var cvar in Differences(scope))
            text.Append(cvar.Name).Append(" \"").Append(cvar.Format(Get(cvar))).Append("\"\n");
        return text.ToString();
    }

    /// <summary>
    /// Applies the <c>name value</c> lines of a config file written by <see cref="WriteConfig"/> to the settings in
    /// <paramref name="scope"/>, without resetting anything else. Blank lines and <c>//</c> comments are skipped; lines
    /// that are not a known setting in scope are reported and skipped. Commands are not run here: that is the console's
    /// <c>exec</c>.
    /// </summary>
    public IReadOnlyList<string> ApplyConfig(string text, CvarFlags scope)
    {
        var problems = new List<string>();
        int number = 0;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            number++;
            var line = raw.Trim();
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment].Trim();
            if (line.Length == 0) continue;
            int space = line.IndexOfAny([' ', '\t']);
            var name = space < 0 ? line : line[..space];
            var value = space < 0 ? "" : line[(space + 1)..].Trim().Trim('"');
            if (Find(name) is not { } cvar || !InScope(cvar, scope))
                problems.Add($"line {number}: '{name}' is not a setting that belongs here");
            else if (!TrySet(cvar, value, out var error))
                problems.Add($"line {number}: {error}");
        }
        return problems;
    }

    static bool InScope(Cvar cvar, CvarFlags scope) => scope == CvarFlags.None || (cvar.Flags & scope) != 0;

    void Assign(Cvar cvar, double value)
    {
        if (!_values.TryGetValue(cvar, out var old))
            throw new ArgumentException($"Setting '{cvar.Name}' is not in this store.", nameof(cvar));
        if (old == value) return;
        _values[cvar] = value;
        Changed?.Invoke(cvar, value);

        if (cvar == Cvars.Cheats)
        {
            if (value != 0) CheatsUsed = true;
            else foreach (var cheat in _ordered.Where(c => c.IsCheat && !IsDefault(c)).ToList()) Assign(cheat, cheat.Default);
        }
    }
}
