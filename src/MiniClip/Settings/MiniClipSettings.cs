namespace MiniClip.Settings;

/// <summary>
/// The user's persisted preferences. The shortcut and popup appearance are
/// deliberately small enough that a
/// hand-edited settings.json is still readable.
/// </summary>
public sealed record MiniClipSettings
{
    /// <summary>Canonical hotkey string, e.g. "Alt+V". Parse with <c>HotkeyGesture.TryParse</c>.</summary>
    public string Hotkey { get; init; } = "Alt+V";

    /// <summary>
    /// When true the popup lists the newest clip first. Kept as a field so an
    /// existing settings.json keeps working if a later version flips the default.
    /// </summary>
    public bool NewestFirst { get; init; } = true;

    /// <summary>
    /// Records whether the first-run privacy notice has been shown. §27 requires
    /// telling the user that history is stored as plain text on this machine.
    /// </summary>
    public bool PrivacyNoticeShown { get; init; }

    /// <summary>Popup palette: "Dark" or "Light". Unknown values fall back to dark.</summary>
    public string Theme { get; init; } = "Dark";

    public static MiniClipSettings Default { get; } = new();
}
