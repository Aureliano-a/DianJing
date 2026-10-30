namespace NexusClick.Engine;

public readonly record struct DesktopBounds(int Left, int Top, int Width, int Height);
public static class DesktopCoordinates
{
    /// <summary>Maps physical virtual-desktop pixels into SendInput's 16-bit absolute range.</summary>
    public static ScreenPoint Normalize(ScreenPoint point, DesktopBounds bounds)
    {
        if (bounds.Width is < 1 or > 65536 || bounds.Height is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(bounds));
        long x = (long)point.X - bounds.Left, y = (long)point.Y - bounds.Top;
        if (x < 0 || x >= bounds.Width || y < 0 || y >= bounds.Height) throw new ArgumentOutOfRangeException(nameof(point), "Point is outside the virtual desktop.");
        // Address the centre of the pixel's normalized interval.
        return new ScreenPoint((int)(((2 * x + 1) * 65536L) / (2L * bounds.Width)), (int)(((2 * y + 1) * 65536L) / (2L * bounds.Height)));
    }
}
