using System.Drawing;

namespace WineCabinet;

internal static class LauncherPlacementRules
{
    private const int EdgeMargin = 18;

    public static Point DefaultLocation(Size window, int rightExtent, Rectangle workingArea) => new(
        workingArea.Right - rightExtent - window.Width / 2 - EdgeMargin,
        workingArea.Top + (workingArea.Height - window.Height) / 2);

    public static bool TryRestore(Point saved, Size window, IReadOnlyList<Rectangle> workingAreas, out Point location)
    {
        var bounds = new Rectangle(saved, window);
        foreach (var area in workingAreas)
        {
            var visible = Rectangle.Intersect(bounds, area);
            if (visible.Width * 2 < window.Width || visible.Height * 2 < window.Height) continue;
            location = new Point(
                Math.Clamp(saved.X, area.Left, Math.Max(area.Left, area.Right - window.Width)),
                Math.Clamp(saved.Y, area.Top, Math.Max(area.Top, area.Bottom - window.Height)));
            return true;
        }

        location = Point.Empty;
        return false;
    }
}
