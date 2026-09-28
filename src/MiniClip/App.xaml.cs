using System.Threading;
using System.Windows;
using System.Windows.Interop;
using MiniClip.Input;
using MiniClip.Settings;
using MiniClip.Tray;
using Microsoft.Win32;

namespace MiniClip;

/// <summary>
/// Application entry point and composition root.
/// </summary>
/// <remarks>
/// <para>MiniClip has no main window. It is a tray icon, a global hotkey and one popup,
/// so <c>ShutdownMode</c> is explicit: closing a window must never end the process. §13, §16.</para>
/// <para>Startup order matters and is deliberate: the message window first (everything
/// else needs somewhere to receive notifications), then history and settings, then the
/// hotkey, then the tray. A failure at any step is reported rather than swallowed,
/// because a clipboard tool that appears to run while doing nothing is worse than one
/// that says it is broken. §16.</para>
/// </remarks>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\MiniClip.SingleInstance.v1";

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private NativeMessageWindow? _messageWindow;
    private MiniClipController? _controller;
    private TrayIconHost? _tray;
    private DispatcherTimer? _heartbeat;
    private bool _selfTestMode;
    private bool _exiting;
    private string? _lastPresentedNotice;
    private HotkeyRegistrationStatus _lastHotkeyStatus;

    [STAThread]
    public static int Main()
    {
        // The generated entry point is disabled in the project file, so this method is
        // the only one. Doing it by hand keeps startup explicit: the dispatcher exists
        // before anything registers a window, a hook or a hotkey against it.

        // A hidden self-test mode. It exists because most of MiniClip's risk is in
        // behaviour only Windows can confirm, and "the code sets WS_EX_NOACTIVATE" is a
        // claim while "the live window has WS_EX_NOACTIVATE" is evidence. It exits
        // without starting the real app, the hotkey, or the tray icon.
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Length > 1 && arguments[1] is "--selftest" or "--self-test")
        {
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown, _selfTestMode = true };
            app.InitializeComponent();
            var output = arguments.Length > 2 ? arguments[2] : null;
            // Run the normal WPF dispatcher while probing multiple popup windows. A
            // nested DispatcherFrame without Application.Run can shut the Application
            // down when the first hidden test window closes, making later probes fail.
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                var failures = Diagnostics.SelfTest.Run(output);
                app.Shutdown(failures);
            }), DispatcherPriority.ApplicationIdle);
            return app.Run();
        }

        // A timed run: start the real application, stay resident for N seconds, then exit.
        // This exists because the tray app has no window and no console, so "does it stay
        // alive and quiet while idle?" cannot be observed from outside without the
        // launching shell's job object killing it the moment that shell returns.
        var runSeconds = ParseRunSeconds(arguments);
        if (runSeconds is { } seconds)
        {
            var application = new App();
            application.InitializeComponent();
            application.ScheduleTimedExit(seconds);
            return application.Run();
        }

        var normal = new App();
        normal.InitializeComponent();
        return normal.Run();
    }

    /// <summary>Reads <c>--run &lt;seconds&gt;</c> from the command line, or null when absent.</summary>
    private static int? ParseRunSeconds(string[] arguments)
    {
        for (var i = 1; i < arguments.Length - 1; i++)
        {
            if (arguments[i] is "--run" or "-r" && int.TryParse(arguments[i + 1], out var seconds))
            {
                return Math.Clamp(seconds, 1, 3600);
            }
        }

        return null;
    }

    /// <summary>Installs the timer that ends a <c>--run</c> session gracefully.</summary>
    private void ScheduleTimedExit(int seconds)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(seconds),
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Diagnostics.DiagnosticsLog.Write("lifecycle", $"timed run complete after {seconds}s");
            ExitApplication();
        };

        timer.Start();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var startupStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        base.OnStartup(e);

        if (_selfTestMode)
        {
            return;
        }

        // Two copies of MiniClip would fight over the same hotkey, the same history file
        // and the same clipboard notifications. Refuse to be the second one.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        _ownsMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            Shutdown(0);
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // The log exists so a shipped build can be diagnosed without a debugger attached.
        // It records states and counts only — never clipboard text. §27.
        Diagnostics.DiagnosticsLog.Start();

        // A tray application that disappears on its own is the worst possible failure: no
        // window, no dialog, nothing to look at. These three hooks make the exit path
        // legible after the fact, including the case where the dispatcher just stops.
        Exit += (_, _) => Diagnostics.DiagnosticsLog.Write("lifecycle", "Application.Exit");
        Dispatcher.ShutdownStarted += (_, _) => Diagnostics.DiagnosticsLog.Write("lifecycle", "dispatcher shutdown started");
        Dispatcher.ShutdownFinished += (_, _) => Diagnostics.DiagnosticsLog.Write("lifecycle", "dispatcher shutdown finished");

        _heartbeat = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30),
        };

        _heartbeat.Tick += (_, _) => Diagnostics.DiagnosticsLog.Write("lifecycle", Diagnostics.DiagnosticsLog.ReadMemory());
        _heartbeat.Start();

        // Unhandled exceptions must not leave a stale global keyboard hook installed.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Diagnostics.DiagnosticsLog.Write("lifecycle", "ProcessExit");
            Teardown();
        };

        _messageWindow = new NativeMessageWindow("MiniClipHost");
        if (!_messageWindow.TryCreate())
        {
            // Nothing can work without it, so say so and leave rather than run invisibly.
            MessageBox.Show(
                "MiniClip 无法创建消息窗口，程序将退出。",
                "MiniClip",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _controller = new MiniClipController(_messageWindow, Dispatcher.CurrentDispatcher);

        if (!await _controller.StartAsync().ConfigureAwait(true))
        {
            MessageBox.Show(
                "MiniClip 初始化失败，程序将退出。",
                "MiniClip",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _tray = new TrayIconHost(
            _messageWindow,
            BuildTrayMenu,
            themeProvider: () => _controller?.Theme ?? UI.PopupTheme.Dark,
            onBeforeMenu: () => _controller?.ExitClipboardMode(UI.PopupCloseReason.FocusLost),
            onClearHistory: () => _ = ClearHistoryAsync(),
            onOpenSettings: OpenSettings,
            onSetAppearance: mode => _ = SetAppearanceAsync(mode),
            onExit: ExitApplication,
            initialTooltip: BuildTooltip());

        if (!_tray.TryCreate())
        {
            MessageBox.Show(
                "MiniClip 无法创建托盘图标，程序将退出。",
                "MiniClip",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        _controller.StateChanged += (_, _) => RefreshTray();

        await MaybeShowFirstRunNoticeAsync().ConfigureAwait(true);
        RefreshTray();
        Diagnostics.DiagnosticsLog.Write("performance",
            $"startupReadyMs={System.Diagnostics.Stopwatch.GetElapsedTime(startupStarted).TotalMilliseconds:F1} {Diagnostics.DiagnosticsLog.ReadMemory()}");
    }

    /// <summary>
    /// States the privacy position once, on first run, in the user's own words rather
    /// than in a README they will not open. §27.
    /// </summary>
    private async Task MaybeShowFirstRunNoticeAsync()
    {
        if (_controller is null || _tray is null)
        {
            return;
        }

        if (_controller.Settings.PrivacyNoticeShown)
        {
            return;
        }

        _tray.ShowFirstRunNotice(_controller.Hotkeys.Current);

        if (!await _controller.MarkPrivacyNoticeShownAsync().ConfigureAwait(true))
        {
            _tray.ShowNotice("MiniClip", "首次使用提示状态保存失败，下次启动会再次显示。", isFailure: true);
        }
    }

    // ------------------------------------------------------------------ tray state

    private IReadOnlyList<TrayMenuEntry> BuildTrayMenu() =>
        TrayMenuBuilder.Build(
            _controller?.Hotkeys.Current,
            _controller?.Hotkeys.Status ?? HotkeyRegistrationStatus.NotAttempted,
            _controller?.History.Count ?? 0,
            _controller?.Appearance ?? UI.AppearanceMode.Dark,
            _controller?.HasUnreadableHistory ?? false,
            _controller?.HasStoredHistoryFile ?? false);

    private string BuildTooltip() =>
        TrayMenuBuilder.BuildTooltip(
            _controller?.Hotkeys.Current,
            _controller?.Hotkeys.Status ?? HotkeyRegistrationStatus.NotAttempted,
            _controller?.History.Count ?? 0);

    private void RefreshTray()
    {
        if (_tray is null || _controller is null || !_tray.IsCreated)
        {
            return;
        }

        _tray.UpdateTooltip(BuildTooltip());

        // A failed registration is the one state the user cannot infer from the window
        // being gone, so it also gets a balloon.
        if (_controller.LastNotice is { } notice && notice != _lastPresentedNotice)
        {
            _lastPresentedNotice = notice;
            _tray.ShowNotice("MiniClip", notice, isFailure: true);
        }
        if (_controller.LastNotice is null) _lastPresentedNotice = null;

        if (_controller.Hotkeys.Status != _lastHotkeyStatus
            && _controller.Hotkeys.Status is HotkeyRegistrationStatus.AlreadyInUse or HotkeyRegistrationStatus.InvalidCombination or HotkeyRegistrationStatus.Failed)
        {
            _tray.ShowNotice("MiniClip 快捷键不可用", _controller.Hotkeys.StatusMessage, isFailure: true);
        }
        _lastHotkeyStatus = _controller.Hotkeys.Status;
    }

    // ------------------------------------------------------------------ commands

    private async Task ClearHistoryAsync()
    {
        if (_controller is null || _tray is null)
        {
            return;
        }

        var count = _controller.History.Count;
        if (count == 0 && !_controller.HasUnreadableHistory && !_controller.HasStoredHistoryFile)
        {
            return;
        }

        var confirmation = new UI.ClearHistoryConfirmationWindow(count, _controller.Theme,
            _controller.HasUnreadableHistory, _controller.HasStoredHistoryFile);
        var handle = new WindowInteropHelper(confirmation).EnsureHandle();
        Win32Windows.ForceForeground(handle);
        confirmation.ShowDialog();
        if (!confirmation.Confirmed)
        {
            return;
        }

        await _controller.ClearHistoryAsync().ConfigureAwait(true);
        _tray.UpdateTooltip(BuildTooltip());
    }

    private async Task SetAppearanceAsync(UI.AppearanceMode appearance)
    {
        if (_controller is null)
        {
            return;
        }

        if (!await _controller.ChangeAppearanceAsync(appearance).ConfigureAwait(true))
        {
            _tray?.ShowNotice("MiniClip", "外观设置保存失败", isFailure: true);
        }
        else
        {
            _tray?.RefreshAppearance();
        }
    }

    private void OpenSettings()
    {
        if (_controller is null)
        {
            return;
        }

        var current = _controller.Hotkeys.Current ?? HotkeyGesture.Default;
        var dialog = new UI.HotkeySettingsWindow(
            current,
            startWithWindows: StartupRegistration.IsEnabled(),
            register: _controller.ChangeHotkeyAsync,
            theme: _controller.Theme,
            historyCount: _controller.History.Count,
            openHistoryFolder: _controller.OpenHistoryFolder);

        // The dialog is the one window that may take focus: the user asked for it by
        // clicking the tray icon, so activating it is correct here and wrong for the popup.
        var handle = new WindowInteropHelper(dialog).EnsureHandle();
        Win32Windows.ForceForeground(handle);

        dialog.PlayEntrance();
        var accepted = dialog.ShowDialog();

        if (accepted == true)
        {
            PersistStartupChoice(dialog.StartWithWindowsResult, dialog.StartWithWindowsChanged);
        }

        RefreshTray();
    }

    private void PersistStartupChoice(bool startWithWindows, bool changed)
    {
        if (changed && !StartupRegistration.SetEnabled(startWithWindows))
        {
            _tray?.ShowNotice("MiniClip", "开机启动设置失败", isFailure: true);
        }
    }

    private async void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        Diagnostics.DiagnosticsLog.Write("lifecycle", "exit requested");

        try
        {
            if (_controller is not null)
            {
                await _controller.FlushAsync().ConfigureAwait(true);
                Diagnostics.DiagnosticsLog.Write("lifecycle", "history flushed");
            }
        }
        finally
        {
            Teardown();
            Shutdown(0);
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_controller?.Appearance != UI.AppearanceMode.System) return;
            _controller.RefreshSystemAppearance();
            _tray?.RefreshAppearance();
        }));

    private void Teardown()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _heartbeat?.Stop();
        _heartbeat = null;

        // Order matters on the way out: release the hook and the hotkey before the window
        // they were registered against disappears.
        _controller?.Dispose();
        _controller = null;

        _tray?.Dispose();
        _tray = null;

        _messageWindow?.Dispose();
        _messageWindow = null;

        // ReleaseMutex throws ApplicationException unless the calling thread owns the
        // mutex, and ProcessExit does not run on the thread that took it. Guarding on
        // ownership is what makes teardown safe to call from both the tray's exit path
        // (correct thread) and the process-exit hook (wrong thread). Without this the
        // process ends with an unhandled exception, which silently destroys the
        // documented "--selftest exit code = failure count" contract.
        var mutex = Interlocked.Exchange(ref _singleInstanceMutex, null);
        if (mutex is not null)
        {
            try
            {
                if (_ownsMutex)
                {
                    mutex.ReleaseMutex();
                }
            }
            catch (ApplicationException)
            {
                // Not the owning thread. The handle is being disposed anyway, and the OS
                // releases an abandoned mutex, so there is nothing left to do.
            }
            finally
            {
                mutex.Dispose();
            }
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A background clipboard tool must not die because one message handler threw.
        // Release the keyboard, keep running, and let the user notice the popup is gone.
        Diagnostics.DiagnosticsLog.Write("error", $"dispatcher={e.Exception.GetType().Name}");
        _controller?.ExitClipboardMode(UI.PopupCloseReason.FocusLost);
        _tray?.ShowNotice("MiniClip", "程序遇到错误，已关闭候选框", isFailure: true);
        e.Handled = true;
    }
}
