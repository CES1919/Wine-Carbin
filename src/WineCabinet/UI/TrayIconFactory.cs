using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WineCabinet;

internal static class TrayIconFactory
{
    public static Icon Create(Image? artwork, Size size)
    {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            if (artwork is not null)
            {
                ArtworkRenderer.DrawContained(graphics, artwork, new Rectangle(0, 0, width, height));
            }
            else
            {
                using var brush = new SolidBrush(Color.FromArgb(255, 229, 224));
                graphics.FillRectangle(brush, 0, 0, width, height);
            }
        }

        // Icon.FromHandle does not own the handle, so clone the image and release it here.
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
