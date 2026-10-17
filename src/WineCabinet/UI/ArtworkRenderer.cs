using System.Drawing.Drawing2D;

namespace WineCabinet;

internal static class ArtworkRenderer
{
    public static RectangleF CalculateContainRectangle(Size sourceSize, Rectangle bounds)
    {
        if (sourceSize.Width <= 0 || sourceSize.Height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return RectangleF.Empty;

        var scale = Math.Min(
            bounds.Width / (float)sourceSize.Width,
            bounds.Height / (float)sourceSize.Height);
        var width = sourceSize.Width * scale;
        var height = sourceSize.Height * scale;
        return new RectangleF(
            bounds.Left + (bounds.Width - width) / 2f,
            bounds.Top + (bounds.Height - height) / 2f,
            width,
            height);
    }

    public static void DrawContained(Graphics graphics, Image image, Rectangle bounds)
    {
        var destination = CalculateContainRectangle(image.Size, bounds);
        if (destination.IsEmpty) return;

        var state = graphics.Save();
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(
            image,
            destination,
            new RectangleF(0, 0, image.Width, image.Height),
            GraphicsUnit.Pixel);
        graphics.Restore(state);
    }
}
