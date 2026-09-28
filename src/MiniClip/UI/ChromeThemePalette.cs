using System.Windows;
using System.Windows.Media;

namespace MiniClip.UI;

/// <summary>Neutral surfaces shared by the settings dialog and custom tray menu.</summary>
public static class ChromeThemePalette
{
    public static ResourceDictionary Create(PopupTheme theme)
    {
        var light = theme == PopupTheme.Light;
        return new ResourceDictionary
        {
            ["ChromeSurfaceBrush"] = Brush(light ? 0xFD : 0x22),
            ["ChromeFieldBrush"] = Brush(light ? 0xFF : 0x24),
            ["ChromeBorderBrush"] = Brush(light ? 0xE5 : 0x49),
            ["ChromeTextBrush"] = Brush(light ? 0x24 : 0xED),
            ["ChromeMutedBrush"] = Brush(light ? 0x74 : 0xA3),
            ["ChromeSelectedBrush"] = Brush(light ? 0xEC : 0x38),
            ["ChromePrimaryBrush"] = Brush(light ? 0x24 : 0xEE),
            ["ChromePrimaryTextBrush"] = Brush(light ? 0xFF : 0x22),
            ["ChromeErrorBrush"] = Brush(light ? 0x55 : 0xCC),
        };
    }

    private static SolidColorBrush Brush(int gray) =>
        new(System.Windows.Media.Color.FromRgb((byte)gray, (byte)gray, (byte)gray));
}
