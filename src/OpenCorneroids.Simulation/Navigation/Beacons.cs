using System.Numerics;
using OpenCorneroids.Simulation.Settings;

namespace OpenCorneroids.Simulation.Navigation;

/// <summary>A named point in space the player marked with the beacon tool.</summary>
/// <param name="Position">World position, m. A beacon stays where it was put; it does not move with a ship.</param>
/// <param name="Message">Its name, shown under its marker: at most <see cref="Beacons.NameMax"/> characters as in the
/// original (<c>sv_beacon_name_max</c>).</param>
public sealed record Beacon(Vector3 Position, string Message);

/// <summary>
/// The beacon tool (B), as in the original. It is not an item: B on foot puts it in hand and empties the hotbar
/// selection, and choosing a slot, the wheel or the right button put it away. Each frame it picks the nearest beacon
/// within 6 m of the player, which is marked with a red box. A left click (on release) removes the picked beacon at
/// once, or with none picked asks for a name of up to 20 characters and puts a beacon with that name 2 m ahead along
/// the view; an empty name puts none. Beacons have no model and no light: they are an orange marker with the distance
/// and the name, drawn whether or not target squares are on. They are saved with the world, with no limit on how many.
/// </summary>
public static class Beacons
{
    /// <summary>How near a beacon must be to the player to be picked (and removed), m, as in the original.</summary>
    public const float Reach = 6f;

    /// <summary>How far ahead along the view a new beacon is put, m, as in the original.</summary>
    public const float PlaceDistance = 2f;

    /// <summary>Longest beacon name, characters, as in the original.</summary>
    public const int NameMax = 20;

    /// <summary>The beacon rules; <see cref="Original"/> or the <c>sv_beacon_*</c> settings.</summary>
    /// <param name="Reach">Pick and remove reach, m (<c>sv_beacon_reach</c>).</param>
    /// <param name="PlaceDistance">How far ahead a new beacon goes, m (<c>sv_beacon_place_distance</c>).</param>
    /// <param name="NameMax">Longest name, characters (<c>sv_beacon_name_max</c>).</param>
    public readonly record struct Tuning(float Reach, float PlaceDistance, int NameMax)
    {
        /// <summary>The original game's values.</summary>
        public static Tuning Original => new(Beacons.Reach, Beacons.PlaceDistance, Beacons.NameMax);

        public static Tuning From(CvarStore cvars)
        {
            ArgumentNullException.ThrowIfNull(cvars);
            return new(cvars.Float(Cvars.BeaconReach), cvars.Float(Cvars.BeaconPlaceDistance), cvars.Int(Cvars.BeaconNameMax));
        }
    }

    /// <summary>
    /// The name a beacon gets from what the player typed: without leading and trailing spaces and cut to
    /// <paramref name="nameMax"/> characters (longer names are cut, as in the original). Null when nothing is left, which
    /// puts no beacon.
    /// </summary>
    public static string? Clean(string? typed, int nameMax = NameMax)
    {
        if (typed is null) return null;
        var name = typed.Trim();
        if (name.Length > Math.Max(1, nameMax)) name = name[..Math.Max(1, nameMax)].TrimEnd();
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Index in <paramref name="beacons"/> of the beacon nearest <paramref name="from"/> no further than
    /// <paramref name="reach"/> (a beacon exactly at the reach counts), or -1 when none is. The first of equally near
    /// beacons wins.
    /// </summary>
    public static int Pick(IReadOnlyList<Beacon> beacons, Vector3 from, float reach = Reach)
    {
        ArgumentNullException.ThrowIfNull(beacons);
        int best = -1;
        float bestSquared = reach * reach;
        for (int i = 0; i < beacons.Count; i++)
        {
            float d = Vector3.DistanceSquared(beacons[i].Position, from);
            if (d > bestSquared || (best >= 0 && d == bestSquared)) continue;
            best = i;
            bestSquared = d;
        }
        return best;
    }

    /// <summary>Where a new beacon goes: <paramref name="distance"/> m along the view from the eye.</summary>
    public static Vector3 PlaceAt(Vector3 eye, Vector3 forward, float distance = PlaceDistance)
    {
        var length = forward.Length();
        var along = length > 1e-6f && float.IsFinite(length) ? forward / length : Vector3.Zero;
        return eye + along * distance;
    }

    /// <summary>
    /// The beacons of a loaded world, kept as saved except that names longer than <paramref name="nameMax"/> are cut and
    /// a beacon whose name is blank is dropped (none could have been made so). Returns how many were dropped.
    /// </summary>
    public static int Restore(IEnumerable<Beacon> saved, ICollection<Beacon> into, int nameMax = NameMax)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(into);
        int dropped = 0;
        foreach (var beacon in saved)
        {
            if (Clean(beacon.Message, nameMax) is { } name && IsFinite(beacon.Position)) into.Add(beacon with { Message = name });
            else dropped++;
        }
        return dropped;
    }

    static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
