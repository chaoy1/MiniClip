using System.Windows;
using System.Windows.Media;

namespace MiniClip.UI;

public enum PopupTheme
{
    Dark,
    Light,
}

/// <summary>The popup's two neutral palettes. The tray and settings dialog keep their own chrome.</summary>
public static class PopupThemePalette
{
    public static PopupTheme Parse(string? value) =>
        Enum.TryParse<PopupTheme>(value, ignoreCase: true, out var theme)
            ? theme
            : PopupTheme.Dark;

    public static ResourceDictionary Create(PopupTheme theme)
    {
        var light = theme == PopupTheme.Light;
        return new ResourceDictionary
        {
            ["PopupSurfaceBrush"] = Brush(light ? 0xFD : 0x1B),
            ["PopupSelectionBrush"] = Brush(light ? 0xEC : 0x30),
            ["PopupBorderBrush"] = Brush(light ? 0xE5 : 0x45),
            ["PopupTextBrush"] = Brush(light ? 0x24 : 0xF0),
            ["PopupSecondaryTextBrush"] = Brush(light ? 0x74 : 0xAB),
            ["PopupScrollBrush"] = Brush(light ? 0xA7 : 0x5A),
        };
    }

    private static SolidColorBrush Brush(int gray) =>
        new(System.Windows.Media.Color.FromRgb((byte)gray, (byte)gray, (byte)gray));
}
