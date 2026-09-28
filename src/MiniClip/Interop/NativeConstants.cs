namespace MiniClip.Interop;

/// <summary>
/// Win32 constants the app needs that are not structurally part of the interop
/// layer's own API (message ids, menu flags, hook codes). Kept in one place so
/// there is a single source of truth.
/// </summary>
public static class NativeConstants
{
    // Window messages
    public const int WM_NULL = 0x0000;
    public const int WM_COMMAND = 0x0111;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    // Low-level keyboard hook
    public const int WH_KEYBOARD_LL = 13;
    public const int HC_ACTION = 0;

    /// <summary>Set in <c>KBDLLHOOKSTRUCT.flags</c> for synthetic events (including our own SendInput).</summary>
    public const uint LLKHF_INJECTED = 0x00000010;
    public const uint LLKHF_LOWER_IL_INJECTED = 0x00000002;
    public const uint LLKHF_EXTENDED = 0x00000001;
    public const uint LLKHF_UP = 0x00000080;

    // Virtual keys we care about
    public const uint VK_SHIFT = 0x10;
    public const uint VK_CONTROL = 0x11;
    public const uint VK_MENU = 0x12;      // Alt
    public const uint VK_LWIN = 0x5B;
    public const uint VK_RWIN = 0x5C;
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_RETURN = 0x0D;
    public const uint VK_UP = 0x26;
    public const uint VK_DOWN = 0x28;
    public const uint VK_V = 0x56;

    // Monitor
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

    // GetAncestor
    public const uint GA_PARENT = 1;
    public const uint GA_ROOT = 2;
    public const uint GA_ROOTOWNER = 3;

    // Window styles. WS_POPUP is 0x80000000, which does not fit in a signed int — as a
    // long it refuses the cast at the SetWindowLongW call site, so it is declared as the
    // negative int it actually is when read back through GetWindowLongW.
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_NOSENDCHANGING = 0x0400;

    // Menu flags
    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_CHECKED = 0x00000008;
    public const uint MF_GRAYED = 0x00000001;
    public const uint MF_DISABLED = 0x00000002;

    public const uint TPM_LEFTALIGN = 0x0000;
    public const uint TPM_RIGHTALIGN = 0x0008;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_WORKAREA = 0x10000;

    // Ids
    public const int TrayIconId = 1;
    public const int FirstHotkeyId = 0x4D43; // 'MC' — arbitrary but stable

    /// <summary><c>CreateWindowExW</c> parent for a message-only window.</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);
}

/// <summary>
/// Readable names for the Win32 errors MiniClip actually branches on. Anything
/// else is reported as a number rather than guessed at.
/// </summary>
public static class NativeErrors
{
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_INVALID_WINDOW_HANDLE = 1400;
    public const int ERROR_CLIPBOARD_NOT_OPEN = 1418;
    public const int ERROR_NOT_ENOUGH_MEMORY = 8;
    public const int ERROR_INVALID_PARAMETER = 87;

    public static string Describe(int errorCode) => errorCode switch
    {
        ERROR_SUCCESS => "已成功",
        ERROR_HOTKEY_ALREADY_REGISTERED => "快捷键已被其他程序占用",
        ERROR_ACCESS_DENIED => "系统拒绝了该操作（权限不足）",
        ERROR_INVALID_WINDOW_HANDLE => "窗口句柄无效",
        ERROR_CLIPBOARD_NOT_OPEN => "剪贴板忙",
        ERROR_NOT_ENOUGH_MEMORY => "内存不足",
        ERROR_INVALID_PARAMETER => "参数无效",
        _ => $"Windows 错误 {errorCode}",
    };
}
