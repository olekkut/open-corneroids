using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Ships;

/// <summary>
/// Where a camera block is looking, relative to its rest view along the block's front (its local −Z after its
/// placement turn), as in the original: a yaw and a pitch, no roll. Yaw wraps round, pitch stops at a limit
/// (<c>cl_camera_pitch_limit</c>, straight up and down by default).
/// </summary>
/// <param name="Yaw">Turn about the block's up axis, radians, in (−π, π]; positive turns left.</param>
/// <param name="Pitch">Turn about the block's right axis, radians; positive looks up.</param>
public readonly record struct CameraLook(float Yaw, float Pitch)
{
    /// <summary>The rest view, straight out of the block's front: what a camera block starts with when it is placed or
    /// loaded, as in the original.</summary>
    public static CameraLook Rest => default;

    /// <summary>
    /// The look after the mouse moved <paramref name="dxPixels"/> right and <paramref name="dyPixels"/> down, as in the
    /// original: yaw −= dx × rate, wrapped into (−π, π]; pitch −= dy × rate, held within ±<paramref name="pitchLimit"/>.
    /// </summary>
    /// <param name="radiansPerPixel">The mouse rate (0.01 × <c>cl_sensitivity</c>).</param>
    /// <param name="pitchLimit">Largest pitch either way, radians; negative counts as 0.</param>
    public CameraLook Rotate(float dxPixels, float dyPixels, float radiansPerPixel, float pitchLimit)
    {
        float limit = Math.Max(0f, pitchLimit);
        return new CameraLook(
            WrapAngle(Yaw - dxPixels * radiansPerPixel),
            Math.Clamp(Pitch - dyPixels * radiansPerPixel, -limit, limit));
    }

    /// <summary>The same look with its pitch held within ±<paramref name="pitchLimit"/>, for when the limit is lowered
    /// while a camera is turned further.</summary>
    public CameraLook Clamped(float pitchLimit)
    {
        float limit = Math.Max(0f, pitchLimit);
        return this with { Pitch = Math.Clamp(Pitch, -limit, limit) };
    }

    /// <summary>An angle brought into (−π, π], the same angle on the circle.</summary>
    public static float WrapAngle(float radians)
    {
        if (!float.IsFinite(radians)) return 0f;
        float wrapped = (float)Math.IEEERemainder(radians, 2 * Math.PI);
        if (wrapped <= -MathF.PI) wrapped += 2 * MathF.PI;
        else if (wrapped > MathF.PI) wrapped -= 2 * MathF.PI;
        return wrapped;
    }

    /// <summary>The pitch limit in radians from <c>cl_camera_pitch_limit</c>'s degrees, held within 0..90°.</summary>
    public static float PitchLimitRadians(double degrees) =>
        (float)(Math.Clamp(double.IsFinite(degrees) ? degrees : 90, 0, 90) * Math.PI / 180);

    /// <summary>
    /// How far in front of a camera block's centre <c>cl_camera_eye_forward</c> (Ours) puts the view: measured from the
    /// block's front face, so any setting above 0 takes the eye out of the block, <paramref name="halfDepth"/> +
    /// the setting. Null at 0 (or below), where the eye stays at the block's centre as in the original. The caller
    /// still keeps the centre when the cell that point lands in is taken.
    /// </summary>
    /// <param name="forward">The setting, metres past the front face.</param>
    /// <param name="halfDepth">Centre to front face: 0.5 for a grid block, half the model's depth for an object.</param>
    public static float? EyeOutsideDistance(double forward, float halfDepth) =>
        double.IsFinite(forward) && forward > 0 ? Math.Max(0f, halfDepth) + (float)forward : null;
}

/// <summary>
/// The look of every camera block on one grid that has been turned, keyed by the camera's cell. As in the original
/// the angles belong to the camera block: they stay when the pilot stands up and sits down again, go with the block
/// when a structure is launched, are forgotten when the block goes, and are not saved, so every camera looks straight
/// out of its front again after a reload.
/// </summary>
public sealed class CameraLooks
{
    readonly Dictionary<Int3, CameraLook> _looks = [];

    /// <summary>How many cameras have a look other than the rest view.</summary>
    public int Count => _looks.Count;

    /// <summary>The look of the camera at <paramref name="cell"/>; <see cref="CameraLook.Rest"/> when it was never turned.</summary>
    public CameraLook Get(Int3 cell) => _looks.TryGetValue(cell, out var look) ? look : CameraLook.Rest;

    /// <summary>Keeps <paramref name="look"/> for the camera at <paramref name="cell"/>.</summary>
    public void Set(Int3 cell, CameraLook look)
    {
        if (look == CameraLook.Rest) _looks.Remove(cell);
        else _looks[cell] = look;
    }

    /// <summary>Turns the camera at <paramref name="cell"/> by a mouse move (see <see cref="CameraLook.Rotate"/>) and
    /// returns its new look.</summary>
    public CameraLook Rotate(Int3 cell, float dxPixels, float dyPixels, float radiansPerPixel, float pitchLimit)
    {
        var look = Get(cell).Rotate(dxPixels, dyPixels, radiansPerPixel, pitchLimit);
        Set(cell, look);
        return look;
    }

    /// <summary>The block at <paramref name="cell"/> went away or was replaced: a new camera there starts at rest.</summary>
    public void Remove(Int3 cell) => _looks.Remove(cell);

    public void Clear() => _looks.Clear();

    /// <summary>Moves the looks of cameras in <paramref name="cells"/> into a new set, at <c>cell − shift</c>, as
    /// <see cref="ShipConsoleBindings.ExtractAndShift"/> does with the bindings.</summary>
    public CameraLooks ExtractAndShift(IReadOnlySet<Int3> cells, Int3 shift)
    {
        var extracted = new CameraLooks();
        foreach (var (cell, look) in _looks.Where(kv => cells.Contains(kv.Key)).ToList())
        {
            extracted._looks[cell - shift] = look;
            _looks.Remove(cell);
        }
        return extracted;
    }

    /// <summary>Takes over every look in <paramref name="other"/> (a launched structure's cameras on its new ship).</summary>
    public void CopyFrom(CameraLooks other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var (cell, look) in other._looks) _looks[cell] = look;
    }
}
