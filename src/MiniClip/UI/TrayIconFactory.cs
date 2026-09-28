using System.Drawing;
using System.Drawing.Drawing2D;

namespace MiniClip.UI;

/// <summary>Draws the approved monochrome M mark for the Windows tray.</summary>
public static class TrayIconFactory
{
    /// <summary>
    /// Creates the icon at the requested pixel size. The returned HICON is owned by the
    /// caller and must be released with <c>Win32Icons.DestroyIconHandle</c>.
    /// </summary>
    public static IntPtr CreateIcon(int size, double dpiScale = 1.0)
    {
        var pixels = Math.Clamp(size <= 0 ? 16 : size, 16, 128);
        using var bitmap = Render(pixels, dpiScale <= 0 ? 1.0 : dpiScale);
        return bitmap.GetHicon();
    }

    /// <summary>Renders the mark into a managed bitmap.</summary>
    public static Bitmap Render(int pixels, double dpiScale = 1.0)
    {
        var bitmap = new Bitmap(pixels, pixels, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.Clear(Color.Transparent);

        // Keep the runtime tray mark aligned with design/icon-concepts/02-letter-m.svg.
        var unit = pixels / 128f;
        using (var tile = RoundedRect(0, 0, pixels, pixels, 27 * unit))
        using (var tileBrush = new SolidBrush(Color.FromArgb(0x20, 0x20, 0x20)))
        {
            graphics.FillPath(tileBrush, tile);
        }

        using var pen = new Pen(Color.White, 10 * unit)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        graphics.DrawLines(pen,
        [
            new PointF(34 * unit, 94 * unit),
            new PointF(34 * unit, 35 * unit),
            new PointF(64 * unit, 69 * unit),
            new PointF(94 * unit, 35 * unit),
            new PointF(94 * unit, 94 * unit),
        ]);

        return bitmap;
    }

    private static GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
