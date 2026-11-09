using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;

namespace OpenCorneroids.Game.Player;

/// <summary>
/// The selected tool or weapon drawn at the lower right of the view, as the original shows it. The model is the
/// item's own icon pushed out into a slab of little cubes, <c>modelDepth</c> pixels thick (the itemset field that,
/// going by its name and values, is meant for exactly this), so any itemset's items get a model with no extra art.
/// Firing kicks it back for a moment.
/// </summary>
public partial class HeldItem : Node3D
{
    /// <summary>Size of the whole icon, in metres. Ours.</summary>
    const float ModelSize = 0.42f;
    static readonly Vector3 Rest = new(0.3f, -0.26f, -0.55f);

    PlayMode _mode = null!;
    ItemIcons _icons = null!;
    MeshInstance3D _model = null!;
    readonly Dictionary<int, ArrayMesh?> _meshes = new();
    int _shownItem = -1;
    float _kick;

    public void Setup(PlayMode mode, ItemIcons icons)
    {
        _icons = icons;
        _model = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        // Icons have their barrel or bit on the left; turned this way, that end points into the screen.
        _model.RotationDegrees = new Vector3(4, -78, 0);
        AddChild(_model);
        SetMode(mode);
    }

    public void SetMode(PlayMode mode)
    {
        if (_mode is SurvivalMode prevSurvival) prevSurvival.Fired -= OnFired;
        _mode = mode;
        _shownItem = -1;
        if (_model is not null) _model.Mesh = null;
        if (mode is SurvivalMode survival) survival.Fired += OnFired;
    }

    /// <summary>
    /// Recoil as in the original: each shot moves the weapon back by its <c>pushback</c> (0..1 units) and it slides
    /// forward again at 0.75 units a second (a 0.05 pistol kick is 5 cm, gone in under a tenth of a second).
    /// </summary>
    void OnFired(float pushback) => _kick = Math.Clamp(pushback, 0, 1);

    const float KickRecovery = 0.75f;
    /// <summary>Metres the model moves back per unit of pushback: one, as the original's units are metres here.</summary>
    const float KickScale = 1f;

    public override void _Process(double delta)
    {
        // A pilot's hands are on the controls, and the dead hold nothing.
        var item = GetParent() is FlyCamera { PilotShip: not null } or FlyCamera { IsDead: true } ? null : _mode.HeldItem;
        int id = item?.ItemId ?? -1;
        if (id != _shownItem)
        {
            _shownItem = id;
            _model.Mesh = item is null ? null : MeshOf(item);
        }
        _kick = Math.Max(0, _kick - (float)delta * KickRecovery);
        _model.Position = Rest + new Vector3(0, 0.2f, 1f) * (_kick * KickScale);
    }

    ArrayMesh? MeshOf(ItemDef item)
    {
        if (_meshes.TryGetValue(item.ItemId, out var cached)) return cached;
        var image = _icons.Of(item)?.GetImage();
        return _meshes[item.ItemId] = image is null ? null : Extrude(image, Math.Max(1, item.ModelDepth));
    }

    /// <summary>Every pixel at least half opaque becomes a cube of that colour; only faces toward empty pixels are kept.</summary>
    static ArrayMesh? Extrude(Image image, int depthPixels)
    {
        if (image.IsCompressed()) image.Decompress();
        int w = image.GetWidth(), h = image.GetHeight();
        if (w == 0 || h == 0) return null;
        float p = ModelSize / Math.Max(w, h);
        float half = depthPixels * p / 2;
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && image.GetPixel(x, y).A >= 0.5f;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int quads = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (!Solid(x, y)) continue;
            var color = image.GetPixel(x, y) with { A = 1 };
            float x0 = (x - w / 2f) * p, x1 = x0 + p;
            float y1 = (h / 2f - y) * p, y0 = y1 - p;
            Quad(new(x0, y0, half), new(x1, y0, half), new(x1, y1, half), new(x0, y1, half), Vector3.Back);
            Quad(new(x1, y0, -half), new(x0, y0, -half), new(x0, y1, -half), new(x1, y1, -half), Vector3.Forward);
            if (!Solid(x - 1, y)) Quad(new(x0, y0, -half), new(x0, y0, half), new(x0, y1, half), new(x0, y1, -half), Vector3.Left);
            if (!Solid(x + 1, y)) Quad(new(x1, y0, half), new(x1, y0, -half), new(x1, y1, -half), new(x1, y1, half), Vector3.Right);
            if (!Solid(x, y - 1)) Quad(new(x0, y1, half), new(x1, y1, half), new(x1, y1, -half), new(x0, y1, -half), Vector3.Up);
            if (!Solid(x, y + 1)) Quad(new(x0, y0, -half), new(x1, y0, -half), new(x1, y0, half), new(x0, y0, half), Vector3.Down);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                // Corners counter-clockwise seen from outside; Godot's front faces are clockwise.
                foreach (var v in new[] { a, c, b, a, d, c })
                {
                    st.SetColor(color);
                    st.SetNormal(normal);
                    st.AddVertex(v);
                }
                quads++;
            }
        }
        if (quads == 0) return null;
        st.SetMaterial(new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.8f });
        return st.Commit();
    }
}
