namespace OpenCorneroids.Content;

/// <summary>A rectangle on an atlas grid, in cells (not pixels). Origin top-left, +y down, as in the XML.</summary>
public readonly record struct CellRect(int X, int Y, int W, int H)
{
    public bool IsEmpty => W <= 0 || H <= 0;
    public override string ToString() => $"({X},{Y} {W}x{H})";
}

public readonly record struct Rgb8(byte R, byte G, byte B);
