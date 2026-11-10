using System.Numerics;
using OpenCorneroids.Simulation.Navigation;
using OpenCorneroids.Simulation.Settings;

namespace OpenCorneroids.Simulation.Tests.Navigation;

public class BeaconsTests
{
    [Fact]
    public void The_settings_default_to_the_original_beacon_tool()
    {
        var store = new CvarStore();
        Assert.Equal(Beacons.Tuning.Original, Beacons.Tuning.From(store));
        Assert.Equal(new Beacons.Tuning(6, 2, 20), Beacons.Tuning.Original);
        Assert.True(store.Bool(Cvars.BeaconMarkers));
        Assert.All([Cvars.BeaconReach, Cvars.BeaconPlaceDistance, Cvars.BeaconNameMax],
            c => Assert.True(c.Flags.HasFlag(CvarFlags.World) && Cvars.All.Contains(c)));
        Assert.True(Cvars.BeaconMarkers.Flags.HasFlag(CvarFlags.User) && Cvars.All.Contains(Cvars.BeaconMarkers));
    }

    [Fact]
    public void A_name_is_cut_to_twenty_characters_as_in_the_original()
    {
        Assert.Equal("ABCDEFGHIJKLMNOPQRST", Beacons.Clean("ABCDEFGHIJKLMNOPQRSTUVWXY"));
        Assert.Equal(20, Beacons.Clean(new string('x', 25))!.Length);
        Assert.Equal("Base", Beacons.Clean("  Base  "));
        Assert.Equal("Bas", Beacons.Clean("Base", nameMax: 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void An_empty_name_puts_no_beacon(string? typed) => Assert.Null(Beacons.Clean(typed));

    [Fact]
    public void A_beacon_exactly_at_the_reach_is_picked_and_one_just_beyond_it_is_not()
    {
        Assert.Equal(0, Beacons.Pick([new Beacon(new Vector3(6, 0, 0), "a")], Vector3.Zero));
        Assert.Equal(-1, Beacons.Pick([new Beacon(new Vector3(6.01f, 0, 0), "a")], Vector3.Zero));
        Assert.Equal(-1, Beacons.Pick([], Vector3.Zero));
    }

    [Fact]
    public void The_nearest_beacon_wins_and_the_first_of_two_as_near()
    {
        Beacon[] beacons =
        [
            new(new Vector3(0, 5, 0), "far"),
            new(new Vector3(0, 0, -2), "near"),
            new(new Vector3(0, 0, 2), "as near"),
        ];
        Assert.Equal(1, Beacons.Pick(beacons, Vector3.Zero));
        Assert.Equal(0, Beacons.Pick(beacons, new Vector3(0, 6, 0), reach: 1.5f));
        Assert.Equal(-1, Beacons.Pick(beacons, new Vector3(0, 6, 0), reach: 0.5f));
    }

    [Fact]
    public void A_new_beacon_goes_two_metres_ahead_along_the_view()
    {
        var at = Beacons.PlaceAt(new Vector3(1, 2, 3), new Vector3(0, 0, -5));
        Assert.Equal(new Vector3(1, 2, 1), at);
        Assert.Equal(new Vector3(1, 2, 3), Beacons.PlaceAt(new Vector3(1, 2, 3), Vector3.Zero));
        Assert.Equal(new Vector3(4, 0, 0), Beacons.PlaceAt(Vector3.Zero, Vector3.UnitX, distance: 4));
    }

    [Fact]
    public void A_loaded_world_keeps_its_beacons_cut_to_the_name_limit_and_drops_nameless_ones()
    {
        var into = new List<Beacon>();
        int dropped = Beacons.Restore(
        [
            new Beacon(new Vector3(1, 2, 3), "Base"),
            new Beacon(new Vector3(4, 5, 6), "A name that is far too long"),
            new Beacon(Vector3.Zero, "  "),
            new Beacon(new Vector3(float.NaN, 0, 0), "Lost"),
        ], into);

        Assert.Equal(2, dropped);
        Assert.Equal([new Beacon(new Vector3(1, 2, 3), "Base"), new Beacon(new Vector3(4, 5, 6), "A name that is far t")], into);
    }
}
