using System.Runtime.InteropServices;
using System.Text;

namespace MiniClip.Interop;

/// <summary>Messages MiniClip's hidden window posts to itself and receives from the shell.</summary>
public enum AppMessage : int
{
    /// <summary><c>wParam</c> = hotkey id, from <c>WM_HOTKEY</c>.</summary>
    HotkeyPressed = 0x8000 + 1,

    /// <summary>The clipboard changed. The text is read by the listener, not here.</summary>
    ClipboardUpdated = 0x8000 + 2,

    /// <summary><c>lParam</c> = <see cref="TrayMessage"/>, from the tray icon callback.</summary>
    TrayCallback = 0x8000 + 3,

    /// <summary><c>wParam</c> = menu command id chosen from the tray context menu.</summary>
    TrayCommand = 0x8000 + 4,

    /// <summary>Cross-thread request to open the popup.</summary>
    ShowPopupRequested = 0x8000 + 5,

    /// <summary>Cross-thread request to toggle the popup.</summary>
    TogglePopupRequested = 0x8000 + 6,
}

/// <summary>Tray callback notifications, decoded from the icon callback message.</summary>
public enum TrayMessage : int
{
    MouseMove = 0x0200,
    LeftButtonDown = 0x0201,
    LeftButtonUp = 0x0202,
    LeftButtonDoubleClick = 0x0203,
    RightButtonDown = 0x0204,
    RightButtonUp = 0x0205,
    RightButtonDoubleClick = 0x0206,
    MiddleButtonDown = 0x0207,
    MiddleButtonUp = 0x0208,
    MiddleButtonDoubleClick = 0x0209,
    SetFocus = 0x0400,
    BalloonShow = 0x0401,
    BalloonHide = 0x0402,
    BalloonUserClick = 0x0405,
    NotifyVersion4 = 0x0406,
}

/// <summary>Handler shape for <see cref="NativeMessageWindow.Message"/>.</summary>
public delegate void WindowMessageHandler(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

/// <summary>
/// A hidden, message-only window that hosts MiniClip's global hotkey, clipboard
/// notifications and tray icon callback.
/// </summary>
/// <remarks>
/// Message-only (<c>HWND_MESSAGE</c>) is deliberate: this window must never appear,
/// never be in the z-order, and never be a candidate for activation. It exists purely
/// as an address for Windows to deliver notifications to. §16.
/// </remarks>
public sealed class NativeMessageWindow : IDisposable
{
    private readonly string _className;
    private readonly NativeMethods.WndProc _wndProc;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _handle = IntPtr.Zero;
    private ushort _atom;
    private bool _disposed;

    public NativeMessageWindow(string classNameSuffix = "MessageWindow")
    {
        // A unique class name per instance keeps two MiniClip processes (or a future
        // second instance during development) from colliding in RegisterClassExW.
        _className = $"MiniClip.{classNameSuffix}.{Environment.ProcessId}.{Guid.NewGuid():N}";

        // Rooted in a field on purpose: if this delegate is collected while Windows
        // still holds the function pointer, the next message crashes the process.
        _wndProc = WindowProc;

        try
        {
            _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            _taskbarCreatedMessage = 0;
        }
    }

    public IntPtr Handle => _handle;

    public bool IsCreated => _handle != IntPtr.Zero;

    /// <summary>The window class name registered for this instance, surfaced for diagnostics.</summary>
    public string ClassName => _className;

    /// <summary>Raised for every message the window receives, before default handling.</summary>
    public event WindowMessageHandler? Message;

    /// <summary>Raised when Explorer restarted; anything registered with the shell must re-register.</summary>
    public event Action? TaskbarCreated;

    /// <summary>Raised when window creation failed, with the Win32 error.</summary>
    public event EventHandler<string>? CreationFailed;

    /// <summary>Creates the window. Idempotent; returns true when the handle is available.</summary>
    public bool TryCreate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle != IntPtr.Zero)
        {
            return true;
        }

        var instance = NativeMethods.GetModuleHandleW(null);
        var classSize = Marshal.SizeOf<NativeMethods.WNDCLASSEXW>();

        // The class name is passed as a raw pointer, and this is not a stylistic choice.
        // Declaring lpszClassName as [MarshalAs(UnmanagedType.ByValTStr)] produces a
        // correctly-sized 584-byte WNDCLASSEXW that RegisterClassExW nevertheless rejects
        // with ERROR_INVALID_PARAMETER (87) on x64 — verified with a standalone probe
        // against three layout variants, where only the pointer forms registered. An
        // explicit HGLOBAL keeps the marshalling entirely under our control.
        var classNamePointer = Marshal.StringToHGlobalUni(_className);

        try
        {
            var windowClass = new NativeMethods.WNDCLASSEXW
            {
                cbSize = classSize,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = instance,
                lpszClassName = classNamePointer,
            };

            _atom = NativeMethods.RegisterClassExW(ref windowClass);
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePointer);
        }

        if (_atom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            CreationFailed?.Invoke(this, NativeErrors.Describe(error));
            return false;
        }

        _handle = NativeMethods.CreateWindowExW(
            dwExStyle: 0,
            lpClassName: _className,
            lpWindowName: "MiniClip",
            dwStyle: 0,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent: NativeConstants.HWND_MESSAGE,
            hMenu: IntPtr.Zero,
            hInstance: instance,
            lpParam: IntPtr.Zero);

        if (_handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnregisterClassW(_className, instance);
            _atom = 0;
            CreationFailed?.Invoke(this, NativeErrors.Describe(error));
            return false;
        }

        return true;
    }

    public void Post(AppMessage message, IntPtr wParam = default, IntPtr lParam = default)
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.PostMessageW(_handle, (int)message, wParam, lParam);
        }
    }

    public void Send(AppMessage message, IntPtr wParam = default, IntPtr lParam = default)
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.SendMessageW(_handle, (int)message, wParam, lParam);
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
        {
            TaskbarCreated?.Invoke();
            return IntPtr.Zero;
        }

        Message?.Invoke(hWnd, msg, wParam, lParam);

        return NativeMethods.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_handle);
            _handle = IntPtr.Zero;
        }

        if (_atom != 0)
        {
            NativeMethods.UnregisterClassW(_className, NativeMethods.GetModuleHandleW(null));
            _atom = 0;
        }

        Message = null;
        TaskbarCreated = null;
    }
}

/// <summary>A row in the tray context menu.</summary>
public sealed class TrayMenuItem
{
    public required string Text { get; init; }

    public required int CommandId { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsDefault { get; init; }

    public static TrayMenuItem Separator { get; } = new() { Text = string.Empty, CommandId = 0, IsSeparator = true };

    public static TrayMenuItem Command(string text, int commandId, bool enabled = true, bool isDefault = false) =>
        new() { Text = text, CommandId = commandId, IsEnabled = enabled, IsDefault = isDefault };
}

/// <summary>Notification balloon icons, mapped to the shell's <c>NIIF_*</c> flags.</summary>
public enum TrayBalloonIcon
{
    None = 0x00000000,
    Info = 0x00000001,
    Warning = 0x00000002,
    Error = 0x00000003,
    User = 0x00000004,
}

/// <summary>
/// MiniClip's system tray icon, implemented directly on <c>Shell_NotifyIcon</c>.
/// </summary>
/// <remarks>
/// Written by hand rather than through <c>NotifyIcon</c> for two reasons: the context
/// menu has to be a real Win32 menu so it matches the shell's own menus exactly, and
/// the icon has to be re-registered when Explorer restarts. §15.
/// </remarks>
public sealed class NativeTrayIcon : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_STATE = 0x00000008;
    private const int NIF_INFO = 0x00000010;
    private const int NIF_SHOWTIP = 0x00000080;

    private const int NOTIFYICON_VERSION_4 = 4;
    private const int NIIF_NOSOUND = 0x00000010;
    private const int NIIF_LARGE_ICON = 0x00000020;

    private readonly NativeMessageWindow _window;
    private readonly int _iconId;
    private readonly uint _callbackMessage = (uint)AppMessage.TrayCallback;

    private string _tooltip = "MiniClip";
    private Func<int, IntPtr>? _iconFactory;
    private IntPtr _currentIcon = IntPtr.Zero;
    private bool _created;
    private bool _disposed;

    public NativeTrayIcon(NativeMessageWindow window, string tooltip, int iconId = NativeConstants.TrayIconId)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _tooltip = tooltip ?? string.Empty;
        _iconId = iconId;

        _window.Message += OnWindowMessage;
        _window.TaskbarCreated += OnTaskbarCreated;
    }

    public bool IsCreated => _created;

    /// <summary>Raised for a single left click — MiniClip's "open the popup" gesture.</summary>
    public event Action? LeftClick;

    /// <summary>Raised for a left double click — opens the hotkey settings.</summary>
    public event Action? LeftDoubleClick;

    /// <summary>Raised when the user clicks the notification balloon.</summary>
    public event Action? BalloonClicked;

    /// <summary>Raised when the user right-clicks; the handler supplies and shows the menu.</summary>
    public event Action? ContextMenuRequested;

    /// <summary>
    /// Creates the icon. <paramref name="iconFactory"/> receives the pixel size the
    /// current DPI calls for (16 at 100%, 20 at 125%, 24 at 150%) and returns an HICON
    /// that this class takes ownership of.
    /// </summary>
    public bool TryCreate(Func<int, IntPtr> iconFactory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_created)
        {
            return true;
        }

        if (!_window.IsCreated && !_window.TryCreate())
        {
            return false;
        }

        _iconFactory = iconFactory;
        _currentIcon = ResolveIcon();

        if (!ShellNotify(NIM_ADD))
        {
            return false;
        }

        // Switch to version 4 so the callback receives the message in lParam's low word
        // and the screen coordinates in lParam's high word.
        var version = BuildData();
        version.uVersion = NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIconW(NIM_SETVERSION, ref version);

        _created = true;
        return true;
    }

    public bool SetTooltip(string tooltip)
    {
        _tooltip = tooltip ?? string.Empty;
        return _created && ShellNotify(NIM_MODIFY);
    }

    /// <summary>Updates the tooltip and the icon in one shell call — used when state changes.</summary>
    public bool Update(string tooltip)
    {
        _tooltip = tooltip ?? string.Empty;
        return _created && ShellNotify(NIM_MODIFY);
    }

    /// <summary>Replaces the icon artwork, releasing the previous handle.</summary>
    public bool SetIcon(Func<int, IntPtr> iconFactory)
    {
        _iconFactory = iconFactory;

        var previous = _currentIcon;
        _currentIcon = ResolveIcon();

        if (previous != IntPtr.Zero && previous != _currentIcon)
        {
            Win32Icons.DestroyIconHandle(previous);
        }

        return _created && ShellNotify(NIM_MODIFY);
    }

    /// <summary>
    /// Shows a notification balloon. Used once, on first run, to state that MiniClip
    /// stores clipboard text as plain text on this machine. §27.
    /// </summary>
    public bool ShowBalloon(string title, string message, TrayBalloonIcon icon = TrayBalloonIcon.Info)
    {
        if (!_created)
        {
            return false;
        }

        var data = BuildData();
        data.uFlags = NIF_INFO | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(message, 255);
        data.dwInfoFlags = (int)icon | NIIF_NOSOUND | NIIF_LARGE_ICON;

        return NativeMethods.Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    /// <summary>
    /// Shows a native context menu at the pointer and returns the chosen command id,
    /// or 0 when the user dismissed it.
    /// </summary>
    public int ShowContextMenu(IReadOnlyList<TrayMenuItem> items, bool alignLeft = true)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (_window.Handle == IntPtr.Zero || items.Count == 0)
        {
            return 0;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    NativeMethods.AppendMenuW(menu, NativeConstants.MF_SEPARATOR, IntPtr.Zero, null);
                    continue;
                }

                var flags = NativeConstants.MF_STRING;
                if (!item.IsEnabled)
                {
                    flags |= NativeConstants.MF_GRAYED;
                }

                NativeMethods.AppendMenuW(menu, flags, new IntPtr(item.CommandId), item.Text);

                if (item.IsDefault)
                {
                    NativeMethods.SetMenuDefaultItem(menu, (uint)item.CommandId, 0);
                }
            }

            if (!Win32Windows.TryGetCursorPosition(out var cursor))
            {
                return 0;
            }

            // Mandatory dance: without foregrounding the owner first, the menu does not
            // dismiss when the user clicks elsewhere on the desktop or in another app.
            NativeMethods.SetForegroundWindow(_window.Handle);

            var commandFlags = NativeConstants.TPM_RETURNCMD | NativeConstants.TPM_RIGHTBUTTON | NativeConstants.TPM_WORKAREA;
            if (alignLeft)
            {
                commandFlags |= NativeConstants.TPM_LEFTALIGN;
            }

            var command = NativeMethods.TrackPopupMenuEx(menu, commandFlags, cursor.X, cursor.Y, _window.Handle, IntPtr.Zero);

            // The same dance requires a null message afterwards, or the menu can reappear.
            NativeMethods.PostMessageW(_window.Handle, NativeConstants.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            return command;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return 0;
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void OnWindowMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != (int)_callbackMessage)
        {
            return;
        }

        var notification = DecodeNotification(wParam, lParam);

        switch ((TrayMessage)notification)
        {
            case TrayMessage.LeftButtonUp:
                LeftClick?.Invoke();
                break;

            case TrayMessage.LeftButtonDoubleClick:
                LeftDoubleClick?.Invoke();
                break;

            case TrayMessage.RightButtonUp:
            case TrayMessage.SetFocus:
                ContextMenuRequested?.Invoke();
                break;

            case TrayMessage.BalloonUserClick:
                BalloonClicked?.Invoke();
                break;
        }
    }

    /// <summary>
    /// With <c>NOTIFYICON_VERSION_4</c> the notification id is in <c>lParam</c>'s low word
    /// and the cursor coordinates are in its high word; the older layout put the
    /// notification in <c>wParam</c>. Decode both so a version change cannot silently
    /// break clicking the icon.
    /// </summary>
    private static int DecodeNotification(IntPtr wParam, IntPtr lParam)
    {
        var fromLParam = (int)(lParam.ToInt64() & 0xFFFF);
        if (Enum.IsDefined(typeof(TrayMessage), fromLParam))
        {
            return fromLParam;
        }

        var fromWParam = (int)(wParam.ToInt64() & 0xFFFF);
        return Enum.IsDefined(typeof(TrayMessage), fromWParam) ? fromWParam : 0;
    }

    private void OnTaskbarCreated()
    {
        if (!_created)
        {
            return;
        }

        // Explorer restarted and forgot every icon; put ours back.
        _created = false;
        TryCreate(_iconFactory ?? (_ => IntPtr.Zero));
    }

    private IntPtr ResolveIcon()
    {
        if (_iconFactory is null)
        {
            return IntPtr.Zero;
        }

        var dpi = Win32Windows.GetDpiForWindow(_window.Handle);
        var size = (int)Math.Round(16 * (dpi / 96.0));
        size = Math.Clamp(size, 16, 64);

        try
        {
            return _iconFactory(size);
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException)
        {
            return IntPtr.Zero;
        }
    }

    private bool ShellNotify(uint message)
    {
        if (!_window.IsCreated)
        {
            return false;
        }

        var data = BuildData();
        return NativeMethods.Shell_NotifyIconW(message, ref data);
    }

    private NativeMethods.NOTIFYICONDATAW BuildData() => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = _iconId,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
        uCallbackMessage = (int)_callbackMessage,
        hIcon = _currentIcon,
        szTip = Truncate(_tooltip, 127),
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _window.Message -= OnWindowMessage;
        _window.TaskbarCreated -= OnTaskbarCreated;

        if (_created)
        {
            var data = BuildData();
            NativeMethods.Shell_NotifyIconW(NIM_DELETE, ref data);
            _created = false;
        }

        if (_currentIcon != IntPtr.Zero)
        {
            Win32Icons.DestroyIconHandle(_currentIcon);
            _currentIcon = IntPtr.Zero;
        }

        LeftClick = null;
        LeftDoubleClick = null;
        ContextMenuRequested = null;
        BalloonClicked = null;
    }

    /// <summary>True when the icon currently reflects what MiniClip asked the shell to show.</summary>
    public bool IsAlive => _created && _window.IsCreated;
}
