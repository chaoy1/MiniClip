using MiniClip.Input;
using MiniClip.UI;

namespace MiniClip.Tray;

/// <summary>
/// Builds the tray menu contents. Kept separate from the native tray plumbing so the
/// menu's wording and state logic can be read — and changed — in one obvious place. §15.
/// </summary>
/// <remarks>
/// Design notes, so the next person does not "simplify" them away:
/// <list type="bullet">
/// <item>Row 1 is a greyed-out status line, not a command. When the hotkey failed to
/// register, this row is where the failure is stated — MiniClip never pretends the
/// shortcut works. §15, §29 stage 2.</item>
/// <item>Keep frequent actions in the short menu; secondary controls live in Settings.</item>
/// <item>"退出" is last and separated, so the row above the bottom edge is never a
/// destructive action.</item>
/// </list>
/// </remarks>
public static class TrayMenuBuilder
{
    public const string AppName = "MiniClip";

    /// <summary>
    /// The short menu. <paramref name="hotkey"/> is the combination MiniClip currently
    /// owns, or null when registration failed.
    /// </summary>
    public static IReadOnlyList<TrayMenuEntry> Build(HotkeyGesture? hotkey, HotkeyRegistrationStatus status,
        int historyCount, AppearanceMode appearance, bool hasUnreadableHistory = false,
        bool hasStoredHistoryFile = false)
    {
        var entries = new List<TrayMenuEntry>(7)
        {
            TrayMenuEntry.Status(BuildStatusLine(hotkey, status)),
            TrayMenuEntry.Separator,
        };

        // Naming the row after its result: the user is clearing a store, and the count
        // makes the outcome checkable. Disabled when there is nothing to clear, because
        // a destructive command that cannot do anything should not invite a click.
        var clearLabel = hasUnreadableHistory ? "清空异常历史…"
            : historyCount > 0 ? $"清空历史（{historyCount} 条）…"
            : hasStoredHistoryFile ? "清空历史文件…" : "清空历史（已为空）";
        entries.Add(TrayMenuEntry.Command(clearLabel, TrayMenuEntry.CommandClearHistory,
            enabled: historyCount > 0 || hasUnreadableHistory || hasStoredHistoryFile));

        entries.Add(TrayMenuEntry.Submenu("外观设置", TrayMenuEntry.CommandAppearance,
        [
            TrayMenuEntry.CheckedCommand("浅色", TrayMenuEntry.CommandThemeLight, appearance == AppearanceMode.Light),
            TrayMenuEntry.CheckedCommand("深色", TrayMenuEntry.CommandThemeDark, appearance == AppearanceMode.Dark),
            TrayMenuEntry.CheckedCommand("跟随系统", TrayMenuEntry.CommandThemeSystem, appearance == AppearanceMode.System),
        ]));

        entries.Add(TrayMenuEntry.Command("设置…", TrayMenuEntry.CommandSettings));

        entries.Add(TrayMenuEntry.Separator);
        entries.Add(TrayMenuEntry.Command($"退出 {AppName}", TrayMenuEntry.CommandExit));

        return entries;
    }

    /// <summary>
    /// The status row. It answers one question — "is this thing listening?" — and when
    /// the answer is no it says why in the same breath.
    /// </summary>
    public static string BuildStatusLine(HotkeyGesture? hotkey, HotkeyRegistrationStatus status) => status switch
    {
        HotkeyRegistrationStatus.Registered when hotkey is not null =>
            $"{AppName} 已就绪 · {hotkey.Value.ToDisplayString()}",

        HotkeyRegistrationStatus.AlreadyInUse when hotkey is not null =>
            $"{hotkey.Value.ToDisplayString()} 已被其他程序占用",

        HotkeyRegistrationStatus.InvalidCombination =>
            "当前快捷键组合不可用",

        HotkeyRegistrationStatus.Failed =>
            "快捷键注册失败，点下方重新设置",

        _ => "快捷键未启用",
    };

    /// <summary>The tray tooltip. Windows truncates past 127 characters, so it stays short.</summary>
    public static string BuildTooltip(HotkeyGesture? hotkey, HotkeyRegistrationStatus status, int historyCount)
    {
        var hotkeyText = status == HotkeyRegistrationStatus.Registered && hotkey is not null
            ? hotkey.Value.ToDisplayString()
            : "快捷键不可用";

        return $"{AppName} · {hotkeyText} · {historyCount} 条记录";
    }

    /// <summary>
    /// Shown once, on the first run, because V1 stores clipboard text as plain text on
    /// disk and the user has to be told that up front rather than in a README. §27.
    /// </summary>
    public static string BuildFirstRunNotice(HotkeyGesture? hotkey)
    {
        var hotkeyText = hotkey?.ToDisplayString() ?? "Alt + V";
        return $"复制文本后按 {hotkeyText} 选择并粘贴。历史以明文保存在本机，不联网、不上传。";
    }
}
