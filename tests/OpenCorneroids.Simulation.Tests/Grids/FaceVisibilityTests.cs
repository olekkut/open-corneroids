using OpenCorneroids.Simulation.Grids;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Tests.Grids;

public class FaceVisibilityTests
{
    static readonly FaceBlock Air = default;
    static readonly FaceBlock Armor = new(1);
    static readonly FaceBlock Rock = new(2);
    static readonly FaceBlock Window = new(3, Cutout: true);
    static readonly FaceBlock OtherWindow = new(4, Cutout: true);
    static readonly FaceBlock Camera = new(5, SeeThrough: true);

    static readonly Int3[] Normals = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

    /// <summary>Meshes a row of blocks along +X the way the chunk mesher does, returning each drawn face.</summary>
    static List<(int X, Int3 Normal)> Mesh(params FaceBlock[] row)
    {
        FaceBlock At(Int3 p) => p.Y == 0 && p.Z == 0 && p.X >= 0 && p.X < row.Length ? row[p.X] : Air;
        var faces = new List<(int, Int3)>();
        for (int x = 0; x < row.Length; x++)
            foreach (var n in Normals)
                if (FaceVisibility.Drawn(row[x], At(new Int3(x, 0, 0) + n)))
                    faces.Add((x, n));
        return faces;
    }

    [Fact]
    public void Armor_camera_armor_shows_the_two_inner_armor_faces_as_in_the_original()
    {
        var faces = Mesh(Armor, Camera, Armor);

        Assert.Contains((0, new Int3(1, 0, 0)), faces);
        Assert.Contains((2, new Int3(-1, 0, 0)), faces);
        // The camera draws all six of its own faces.
        Assert.Equal(6, faces.Count(f => f.X == 1));
        // Each armor block: its outer end and four sides, plus the inner face against the camera.
        Assert.Equal(6, faces.Count(f => f.X == 0));
        Assert.Equal(6, faces.Count(f => f.X == 2));
    }

    [Fact]
    public void Armor_rock_armor_hides_the_inner_faces()
    {
        var faces = Mesh(Armor, Rock, Armor);

        Assert.DoesNotContain((0, new Int3(1, 0, 0)), faces);
        Assert.DoesNotContain((1, new Int3(-1, 0, 0)), faces);
        Assert.Equal(14, faces.Count);
    }

    [Fact]
    public void Two_cameras_side_by_side_draw_both_faces_between_them()
    {
        var faces = Mesh(Camera, Camera);

        Assert.Equal(12, faces.Count);
    }

    [Fact]
    public void Windows_keep_their_old_rule()
    {
        Assert.False(FaceVisibility.Drawn(Window, Window));          // between two panes of the same glass
        Assert.True(FaceVisibility.Drawn(Window, OtherWindow));
        Assert.True(FaceVisibility.Drawn(Armor, Window));
        Assert.False(FaceVisibility.Drawn(Window, Armor));
        Assert.True(FaceVisibility.Drawn(Window, Camera));
    }

    [Fact]
    public void Air_shows_a_face_unless_an_opaque_object_model_fills_it()
    {
        Assert.True(FaceVisibility.Drawn(Armor, Air));
        Assert.False(FaceVisibility.Drawn(Armor, Air, neighbourHiddenByObject: true));
        Assert.True(FaceVisibility.Drawn(Camera, Air, neighbourHiddenByObject: true));
        Assert.False(FaceVisibility.Drawn(Air, Armor));
    }

    [Fact]
    public void The_object_model_flag_only_counts_when_the_neighbour_is_air()
    {
        Assert.True(FaceVisibility.Drawn(Armor, Camera, neighbourHiddenByObject: true));
        Assert.True(FaceVisibility.Drawn(Armor, Window, neighbourHiddenByObject: true));
        Assert.False(FaceVisibility.Drawn(Armor, Rock, neighbourHiddenByObject: false));
    }

    [Theory]
    [InlineData("camera", true)]
    [InlineData("armor", false)]
    [InlineData("window", false)]
    [InlineData("gun", false)]
    [InlineData("door", false)]
    [InlineData("light", false)]
    [InlineData("Camera", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_camera_blocks_are_see_through_as_in_the_original(string? kind, bool seeThrough) =>
        Assert.Equal(seeThrough, FaceVisibility.IsSeeThroughKind(kind));

    [Theory]
    [InlineData(false, false, true)]   // an opaque model
    [InlineData(true, false, false)]   // a window model
    [InlineData(false, true, false)]   // a camera model
    [InlineData(true, true, false)]
    public void Only_opaque_object_models_hide_grid_faces(bool cutout, bool seeThrough, bool hides) =>
        Assert.Equal(hides, FaceVisibility.ObjectHidesFaces(cutout, seeThrough));
}
