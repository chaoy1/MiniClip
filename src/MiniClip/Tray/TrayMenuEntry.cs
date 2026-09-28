using MiniClip.Input;

namespace MiniClip.Tray;

/// <summary>
/// A tray menu row. MiniClip's menu is a status panel as much as a command list —
/// the first row is not a command at all, it tells the user whether the hotkey works. §15.
/// </summary>
public sealed record TrayMenuEntry(
    string Text,
    int CommandId,
    bool IsSeparator = false,
    bool IsEnabled = true,
    bool IsChecked = false,
    IReadOnlyList<TrayMenuEntry>? Children = null)
{
    /// <summary>Menu ids start above 100 so they can never collide with a shell id.</summary>
    public const int CommandShowStatus = 100;
    public const int CommandClearHistory = 101;
    public const int CommandChangeHotkey = 102;
    public const int CommandOpenHistoryFolder = 103;
    public const int CommandAbout = 104;
    public const int CommandExit = 105;
    public const int CommandToggleTheme = 106;
    public const int CommandSettings = 107;
    public const int CommandAppearance = 108;
    public const int CommandThemeLight = 109;
    public const int CommandThemeDark = 110;
    public const int CommandThemeSystem = 111;

    public static TrayMenuEntry Separator { get; } = new(string.Empty, 0, IsSeparator: true);

    /// <summary>The non-command status readout at the top of the menu.</summary>
    public static TrayMenuEntry Status(string text) => new(text, CommandShowStatus, IsEnabled: false);

    public static TrayMenuEntry About(string text) => new(text, CommandAbout, IsEnabled: false);

    public static TrayMenuEntry Command(string text, int commandId, bool enabled = true) =>
        new(text, commandId, IsSeparator: false, IsEnabled: enabled);

    public static TrayMenuEntry CheckedCommand(string text, int commandId, bool isChecked) =>
        new(text, commandId, IsChecked: isChecked);

    public static TrayMenuEntry Submenu(string text, int commandId, IReadOnlyList<TrayMenuEntry> children) =>
        new(text, commandId, Children: children);
}
