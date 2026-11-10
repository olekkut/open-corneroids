using System.Globalization;

namespace OpenCorneroids.Simulation.Settings;

/// <summary>Where a setting lives and who may change it.</summary>
[Flags]
public enum CvarFlags
{
    None = 0,
    /// <summary>A game rule (<c>sv_</c>): saved with the world, and in multiplayer owned by the server.</summary>
    World = 1,
    /// <summary>A player preference (<c>cl_</c>): saved in the player's own config file, whatever world is loaded.</summary>
    User = 2,
    /// <summary>Only changes while <c>sv_cheats</c> is 1, and goes back to its default when cheats are turned off.</summary>
    Cheat = 4,
}

/// <summary>The kind of value a setting holds. Every value is kept as a <see cref="double"/>.</summary>
public enum CvarKind
{
    /// <summary>0 or 1; accepts on/off, true/false, yes/no.</summary>
    Bool,
    /// <summary>A whole number.</summary>
    Int,
    /// <summary>A real number.</summary>
    Float,
}

/// <summary>
/// The definition of one console setting: its name, the default (the original game's value where the original has
/// one), the range it may take and what it does. Values live in a <see cref="CvarStore"/>, so the definitions can be
/// shared freely.
/// </summary>
public sealed class Cvar
{
    public Cvar(string name, CvarKind kind, double defaultValue, CvarFlags flags, string description,
        double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        if (name.Any(c => char.IsWhiteSpace(c) || c == ';' || c == '"'))
            throw new ArgumentException($"Setting name '{name}' may not contain spaces, ';' or '\"'.", nameof(name));
        if (kind == CvarKind.Bool) (min, max) = (0, 1);
        if (min > max) throw new ArgumentException($"Setting '{name}': min {min} is above max {max}.", nameof(min));
        if (!double.IsFinite(defaultValue) || defaultValue < min || defaultValue > max)
            throw new ArgumentException($"Setting '{name}': default {defaultValue} is outside {min}..{max}.", nameof(defaultValue));
        if (kind == CvarKind.Int && defaultValue != Math.Round(defaultValue))
            throw new ArgumentException($"Setting '{name}': default {defaultValue} is not a whole number.", nameof(defaultValue));
        if (flags.HasFlag(CvarFlags.World) && flags.HasFlag(CvarFlags.User))
            throw new ArgumentException($"Setting '{name}' cannot be both a world rule and a user preference.", nameof(flags));

        Name = name;
        Kind = kind;
        Default = defaultValue;
        Flags = flags;
        Description = description;
        Min = min;
        Max = max;
    }

    public string Name { get; }
    public CvarKind Kind { get; }
    public double Default { get; }
    public CvarFlags Flags { get; }
    public string Description { get; }
    public double Min { get; }
    public double Max { get; }

    public bool IsCheat => Flags.HasFlag(CvarFlags.Cheat);

    /// <summary>
    /// The value is read once, when a world loads (the inventory size, for one): changing it does nothing until the
    /// world is loaded again, and the console says so.
    /// </summary>
    public bool AppliesOnLoad { get; init; }

    /// <summary>The value as typed in the console and written to config files: <c>1</c>, <c>20</c>, <c>0.25</c>.</summary>
    public string Format(double value) => Kind switch
    {
        CvarKind.Bool => value != 0 ? "1" : "0",
        CvarKind.Int => ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
        _ => value.ToString("0.######", CultureInfo.InvariantCulture),
    };

    /// <summary>The allowed values, for help text: <c>0/1</c>, <c>0..512</c>, <c>0.1..50</c>, <c>any</c>.</summary>
    public string RangeText => Kind == CvarKind.Bool
        ? "0/1"
        : (double.IsNegativeInfinity(Min) && double.IsPositiveInfinity(Max))
            ? "any"
            : $"{(double.IsNegativeInfinity(Min) ? "" : Format(Min))}..{(double.IsPositiveInfinity(Max) ? "" : Format(Max))}";

    /// <summary>
    /// Reads <paramref name="text"/> as a value of this setting. Out-of-range numbers are refused rather than clamped,
    /// so a typo never silently becomes the limit; whole-number settings refuse fractions.
    /// </summary>
    public bool TryParse(string text, out double value, out string? error)
    {
        value = 0;
        error = null;
        var t = (text ?? "").Trim();
        if (Kind == CvarKind.Bool)
        {
            switch (t.ToLowerInvariant())
            {
                case "1" or "on" or "true" or "yes" or "enable" or "enabled":
                    value = 1;
                    return true;
                case "0" or "off" or "false" or "no" or "disable" or "disabled":
                    value = 0;
                    return true;
                default:
                    error = $"'{t}' is not a valid value for {Name}; expected 0 or 1.";
                    return false;
            }
        }

        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
        {
            error = $"'{t}' is not a number; {Name} takes {RangeText}.";
            return false;
        }
        if (Kind == CvarKind.Int && parsed != Math.Round(parsed))
        {
            error = $"{Name} takes whole numbers ({RangeText}).";
            return false;
        }
        if (parsed < Min || parsed > Max)
        {
            error = $"{t} is outside {Name}'s range {RangeText}.";
            return false;
        }
        value = parsed;
        return true;
    }

    public override string ToString() => Name;
}
