using MiniClip.Input;

namespace MiniClip.Tray;

/// <summary>
/// Wires the native tray icon to MiniClip's commands and keeps its tooltip and menu in
/// step with the registration state.
/// </summary>
/// <remarks>
/// The tray icon is the only place the user can find MiniClip, so it doubles as a status
/// readout: when the hotkey is unavailable, the tooltip says so instead of showing a
/// shortcut that does not work. §15.
/// </remarks>
public sealed class TrayIconHost : IDisposable
{
    private readonly NativeTrayIcon _icon;
    private readonly Func<IReadOnlyList<TrayMenuEntry>> _menuFactory;
    private readonly Func<UI.PopupTheme> _themeProvider;
    private readonly Action _onBeforeMenu;
    private readonly Action _onClearHistory;
    private readonly Action _onOpenSettings;
    private readonly Action<UI.AppearanceMode> _onSetAppearance;
    private readonly Action _onExit;
    private UI.TrayMenuWindow? _menuWindow;
    private UI.NoticeWindow? _noticeWindow;

    private bool _disposed;

    public TrayIconHost(
        NativeMessageWindow window,
        Func<IReadOnlyList<TrayMenuEntry>> menuFactory,
        Func<UI.PopupTheme> themeProvider,
        Action onBeforeMenu,
        Action onClearHistory,
        Action onOpenSettings,
        Action<UI.AppearanceMode> onSetAppearance,
        Action onExit,
        string initialTooltip)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(menuFactory);

        _menuFactory = menuFactory;
        _themeProvider = themeProvider;
        _onBeforeMenu = onBeforeMenu;
        _onClearHistory = onClearHistory;
        _onOpenSettings = onOpenSettings;
        _onSetAppearance = onSetAppearance;
        _onExit = onExit;

        _icon = new NativeTrayIcon(window, initialTooltip);
        _icon.ContextMenuRequested += OnContextMenuRequested;
        _icon.LeftDoubleClick += () => _onOpenSettings();
        _icon.BalloonClicked += () => _onOpenSettings();
    }

    public bool TryCreate() => _icon.TryCreate(size => UI.TrayIconFactory.CreateIcon(size, 1.0));

    public bool IsCreated => _icon.IsCreated;

    /// <summary>Refreshes the tooltip; the icon artwork never changes, so no re-registration is needed.</summary>
    public bool UpdateTooltip(string tooltip) => _icon.Update(tooltip);

    /// <summary>Replaces the tooltip and shows it immediately, used when state changes.</summary>
    public bool Refresh(string tooltip) => _icon.IsAlive && _icon.SetTooltip(tooltip);

    /// <summary>Transient windows are recreated with the current palette the next time they open.</summary>
    public void RefreshAppearance()
    {
        _menuWindow?.Close();
        _noticeWindow?.Close();
    }

    /// <summary>
    /// Shown once on first run. V1 stores clipboard text as plain text on this machine,
    /// and the user has to be told that before they copy anything sensitive, not after. §27.
    /// </summary>
    public bool ShowFirstRunNotice(HotkeyGesture? hotkey) =>
        ShowCustomNotice("MiniClip 已在后台运行", TrayMenuBuilder.BuildFirstRunNotice(hotkey), _onOpenSettings);

    public bool ShowNotice(string title, string message, bool isFailure = false) =>
        ShowCustomNotice(title, message);

    private bool ShowCustomNotice(string title, string message, Action? onClick = null)
    {
        if (!_icon.IsCreated)
        {
            return false;
        }

        _noticeWindow?.Close();
        var notice = new UI.NoticeWindow(title, message, _themeProvider(), onClick);
        _noticeWindow = notice;
        notice.Closed += (_, _) =>
        {
            if (ReferenceEquals(_noticeWindow, notice))
            {
                _noticeWindow = null;
            }
        };
        notice.ShowAtScreenCorner();
        return true;
    }

    private void OnContextMenuRequested()
    {
        if (_menuWindow is { IsVisible: true })
        {
            _menuWindow.Close();
            return;
        }

        _onBeforeMenu();
        if (!Win32Windows.TryGetCursorPosition(out var cursor))
        {
            return;
        }

        var menu = new UI.TrayMenuWindow(_menuFactory(), _themeProvider());
        _menuWindow = menu;
        menu.CommandInvoked += Dispatch;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menuWindow, menu))
            {
                _menuWindow = null;
            }
        };
        menu.ShowAt(cursor);
    }

    private void Dispatch(int command)
    {
        switch (command)
        {
            case TrayMenuEntry.CommandClearHistory:
                _onClearHistory();
                break;

            case TrayMenuEntry.CommandSettings:
                _onOpenSettings();
                break;

            case TrayMenuEntry.CommandThemeLight:
                _onSetAppearance(UI.AppearanceMode.Light);
                break;

            case TrayMenuEntry.CommandThemeDark:
                _onSetAppearance(UI.AppearanceMode.Dark);
                break;

            case TrayMenuEntry.CommandThemeSystem:
                _onSetAppearance(UI.AppearanceMode.System);
                break;

            case TrayMenuEntry.CommandExit:
                _onExit();
                break;

            default:
                // 0 means dismissed, and the disabled status rows carry their own ids.
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _menuWindow?.Close();
        _noticeWindow?.Close();
        _icon.Dispose();
    }
}
