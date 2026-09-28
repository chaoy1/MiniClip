using System.Runtime.InteropServices;
using MiniClip.History;
using MiniClip.Input;
using MiniClip.Paste;
using MiniClip.Settings;
using MiniClip.Storage;
using MiniClip.Tray;

namespace MiniClip;

/// <summary>
/// Wires the pieces together and owns the one piece of state the product is really
/// about: whether MiniClip is currently in <em>Clipboard Mode</em>.
/// </summary>
/// <remarks>
/// <para>Clipboard Mode is a promise with two halves. While it is on, ↑ ↓ Enter Esc
/// belong to MiniClip. While it is off — and it must return to off on every path,
/// including an exception — MiniClip touches nothing. Almost every method here exists
/// to keep that promise, which is why entering and leaving are the only two operations
/// that are allowed to touch the hook, the popup and the keyboard at once. §9.</para>
/// <para>The other promise is the paste guard: the text goes to the window the user was
/// in when they pressed the hotkey, or it goes nowhere. §18.</para>
/// </remarks>
public sealed class MiniClipController : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly JsonStorage _storage;
    private readonly ClipboardWatcher _watcher;
    private readonly HotkeyManager _hotkeys;
    private readonly KeyboardHook _keyboardHook = new();
    private readonly PasteController _paste = new();
    private readonly HistoryManager _history;
    private readonly HistoryWriteTracker _historyWrites = new();
    private readonly Dispatcher _dispatcher;
    private readonly Func<Task<string?>> _clipboardTextReader;
    private readonly Func<uint> _clipboardSequenceReader;

    private MiniClipSettings _settings = MiniClipSettings.Default;
    private PopupWindow? _popup;
    private PasteTarget _target = PasteTarget.Empty;
    private uint _ignoredSequence;
    private uint _clearedClipboardSequence;
    private long _historyEpoch;
    private bool _isPasting;
    private bool _historyWritesBlocked;
    private bool _disposed;

    public MiniClipController(NativeMessageWindow window, Dispatcher dispatcher,
        JsonStorage? storage = null, SettingsStore? settingsStore = null,
        Func<Task<string?>>? clipboardTextReader = null,
        Func<uint>? clipboardSequenceReader = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _storage = storage ?? new JsonStorage();
        _settingsStore = settingsStore ?? new SettingsStore();
        _history = new HistoryManager(dispatcher);
        _clipboardTextReader = clipboardTextReader ?? (() => ClipboardReader.ReadTextAsync());
        _clipboardSequenceReader = clipboardSequenceReader ?? (() => Win32Clipboard.SequenceNumber);
        _watcher = new ClipboardWatcher(window);
        _hotkeys = new HotkeyManager(window);
        MessageWindow = window;

        window.Message += OnWindowMessage;
    }

    /// <summary>The hidden window that hosts the hotkey, clipboard notifications and tray icon.</summary>
    public NativeMessageWindow MessageWindow { get; }

    public HistoryManager History => _history;

    public HotkeyManager Hotkeys => _hotkeys;

    public MiniClipSettings Settings => _settings;
    public bool HasUnreadableHistory => _historyWritesBlocked;
    public bool HasStoredHistoryFile => File.Exists(_storage.HistoryPath);

    /// <summary>True while ↑ ↓ Enter Esc are owned by MiniClip.</summary>
    public bool IsClipboardMode { get; private set; }

    /// <summary>Raised whenever the state that the tray menu renders has changed.</summary>
    public event EventHandler? StateChanged;

    // ------------------------------------------------------------------ start-up

    /// <summary>
    /// Restores history and settings, then registers the hotkey and the clipboard
    /// listener. Every step reports failure rather than proceeding as if it worked. §16.
    /// </summary>
    public async Task<bool> StartAsync()
    {
        if (!MessageWindow.IsCreated && !MessageWindow.TryCreate())
        {
            return false;
        }

        _settings = await _settingsStore.LoadAsync().ConfigureAwait(true);

        var staging = new List<string>(HistoryManager.DefaultCapacity);
        var load = await _storage.LoadAsync(staging).ConfigureAwait(true);
        await _history.ResetAsync(staging).ConfigureAwait(true);

        if (load.Outcome == StorageOutcome.Loaded && load.Count != _history.Count)
        {
            // A hand-edited or older history file may exceed the active limits.
            // Rewrite it once so the disk copy matches the bounded in-memory list.
            _historyWrites.MarkChanged();
            await PersistAsync().ConfigureAwait(true);
        }

        switch (load.Outcome)
        {
            case StorageOutcome.Corrupt:
                // The file is preserved, never overwritten; start empty and say so once.
                LastNotice = "历史文件损坏，已保留原文件并以空历史启动";
                break;

            case StorageOutcome.Failed:
                LastNotice = "历史读取失败，已暂停写入以保护原文件；可在设置中打开文件位置检查";
                _historyWritesBlocked = true;
                break;
        }

        _watcher.Updated += OnClipboardUpdated;
        if (!_watcher.Start())
        {
            LastNotice = "无法监听剪贴板";
            return false;
        }

        _keyboardHook.KeyHandler = OnNavigationKey;
        _keyboardHook.HookFailed += (_, message) => LastNotice = message;

        var gesture = ResolveHotkey(_settings.Hotkey);
        _hotkeys.Register(gesture);
        _hotkeys.RegistrationChanged += (_, _) => RaiseStateChanged();

        _history.Changed += (_, _) =>
        {
            _historyWrites.MarkChanged();
            RaiseStateChanged();
        };

        RaiseStateChanged();
        return true;
    }

    /// <summary>Last thing worth telling the user through the tray.</summary>
    public string? LastNotice { get; private set; }

    public UI.AppearanceMode Appearance => UI.AppearanceModeExtensions.Parse(_settings.Theme);

    public PopupTheme Theme => UI.AppearanceModeExtensions.Resolve(Appearance, UI.AppearanceModeExtensions.SystemIsLight());

    public async Task<bool> ChangeAppearanceAsync(UI.AppearanceMode appearance)
    {
        if (appearance == Appearance)
        {
            return true;
        }

        var next = _settings with { Theme = appearance.ToString() };
        if (!await _settingsStore.SaveAsync(next).ConfigureAwait(true))
        {
            LastNotice = "外观设置保存失败";
            return false;
        }

        _settings = next;
        _popup?.SetTheme(Theme);
        RaiseStateChanged();
        return true;
    }

    public void RefreshSystemAppearance()
    {
        if (Appearance != UI.AppearanceMode.System) return;
        _popup?.SetTheme(Theme);
        RaiseStateChanged();
    }

    public async Task<bool> MarkPrivacyNoticeShownAsync()
    {
        if (_settings.PrivacyNoticeShown) return true;
        var next = _settings with { PrivacyNoticeShown = true };
        if (!await _settingsStore.SaveAsync(next).ConfigureAwait(true)) return false;
        _settings = next;
        return true;
    }

    /// <summary>True when the combination in settings could not be used and Alt+V took over.</summary>
    public bool IsUsingFallbackHotkey { get; private set; }

    private HotkeyGesture ResolveHotkey(string configured)
    {
        IsUsingFallbackHotkey = false;

        if (!HotkeyGesture.TryParse(configured, out var gesture) || !gesture.IsValid)
        {
            IsUsingFallbackHotkey = true;
            return HotkeyGesture.Default;
        }

        return gesture;
    }

    /// <summary>Applies a new hotkey chosen in settings. Returns the registration result.</summary>
    public async Task<HotkeyRegistrationResult> ChangeHotkeyAsync(HotkeyGesture gesture)
    {
        var previous = _hotkeys.Current;

        var result = _hotkeys.Register(gesture);
        if (!result.IsRegistered)
        {
            // Put the working combination back rather than leaving the user with none.
            if (previous is { } restore)
            {
                _hotkeys.Register(restore);
            }

            RaiseStateChanged();
            return result;
        }

        // A successful re-registration is the only way to release the old combination,
        // which Register already did; persist so it survives a restart.
        var next = _settings with { Hotkey = gesture.ToCanonicalString() };
        if (!await _settingsStore.SaveAsync(next).ConfigureAwait(true))
        {
            var restored = previous is { } restore && _hotkeys.Register(restore).IsRegistered;
            LastNotice = restored
                ? "快捷键设置保存失败，已恢复原快捷键"
                : "快捷键设置保存失败，原快捷键也未能恢复；请重新设置";
            RaiseStateChanged();
            return new HotkeyRegistrationResult(HotkeyRegistrationStatus.Failed, gesture, LastNotice);
        }
        _settings = next;

        IsUsingFallbackHotkey = false;
        LastNotice = null;
        RaiseStateChanged();
        return result;
    }

    // ------------------------------------------------------------------ clipboard

    private async void OnClipboardUpdated(object? sender, ClipboardUpdatedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }
        var historyEpoch = _historyEpoch;
        if (e.SequenceNumber != 0 && e.SequenceNumber == _clearedClipboardSequence)
        {
            // A notification queued before ClearHistoryAsync can enter this handler
            // afterward. Ignore the clipboard generation that existed at clear time.
            return;
        }

        // Our own paste writes the clipboard, and that write raises this very event. Two
        // independent facts identify the echo, and both must agree:
        //   1. the clipboard sequence number matches the one our write produced, and
        //   2. the clipboard carries MiniClip's private marker format.
        // The marker is what makes this safe. A sequence-number comparison on its own is
        // not: the counter is a shared global, so an unrelated copy by another program can
        // legitimately land on the same number, and that user's clip would then be silently
        // discarded. Requiring the marker means only data MiniClip itself put there is
        // skipped. The sequence is consumed rather than left set, so a stale value can never
        // suppress a later copy. §17.
        var selfWriteSequence = _isPasting ? _paste.LastSelfWriteSequence : _ignoredSequence;
        if (e.SequenceNumber != 0
            && e.SequenceNumber == selfWriteSequence
            && Win32Clipboard.HasSelfMarker())
        {
            _ignoredSequence = 0;
            return;
        }

        if (e.SequenceNumber != 0 && e.SequenceNumber != _clipboardSequenceReader())
        {
            // The notification sat in the message queue while a newer copy replaced it.
            // Its original data can no longer be recovered from the system clipboard.
            return;
        }

        try
        {
            var text = await _clipboardTextReader().ConfigureAwait(true);
            if (_disposed || historyEpoch != _historyEpoch
                || (e.SequenceNumber != 0 && e.SequenceNumber != _clipboardSequenceReader()))
            {
                return;
            }
            if (string.IsNullOrEmpty(text))
            {
                // Also covers images, file lists and HTML-only payloads: V1 ignores them. §10.1.
                return;
            }

            var change = await _history.AddAsync(text).ConfigureAwait(true);
            if (!change.ListChanged)
            {
                return;
            }

            await PersistAsync().ConfigureAwait(true);

            if (_popup is { IsPopupOpen: true })
            {
                // Keep an open popup truthful: new clips appear at the top immediately.
                _popup.Refresh(_history.Snapshot());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let a clipboard hiccup reach the message loop.
            LastNotice = "读取剪贴板失败，已跳过这次复制";
            RaiseStateChanged();
        }
    }

    /// <summary>Writes the history through to disk. Failures are reported, never swallowed. §12.</summary>
    private async Task PersistAsync()
    {
        if (_historyWritesBlocked)
        {
            LastNotice = "历史读取失败，已暂停写入以保护原文件";
            RaiseStateChanged();
            return;
        }
        var texts = new List<string>(_history.Count);
        foreach (var entry in _history.Snapshot())
        {
            texts.Add(entry.Text);
        }
        var snapshotVersion = _historyWrites.Version;

        var result = await _storage.SaveAsync(texts).ConfigureAwait(true);

        if (result.Outcome == StorageOutcome.Failed)
        {
            LastNotice = "历史保存失败，本次记录只在内存中";
            RaiseStateChanged();
        }
        else
        {
            _historyWrites.MarkSaved(snapshotVersion);
            if (LastNotice == "历史保存失败，本次记录只在内存中")
                LastNotice = null;
        }
    }

    // ------------------------------------------------------------------ hotkey

    private void OnWindowMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeConstants.WM_HOTKEY)
        {
            return;
        }

        Diagnostics.DiagnosticsLog.Write("hotkey", $"WM_HOTKEY id={wParam.ToInt32()} expected={_hotkeys.HotkeyId}");

        if (wParam.ToInt32() != _hotkeys.HotkeyId)
        {
            return;
        }

        TogglePopup();
    }

    /// <summary>
    /// Opens the popup, or closes it when it is already open — pressing the hotkey twice
    /// must not stack two popups. §9.2.
    /// </summary>
    public void TogglePopup()
    {
        Diagnostics.DiagnosticsLog.Write("mode", $"toggle requested clipboardMode={IsClipboardMode}");

        if (IsClipboardMode)
        {
            ExitClipboardMode(PopupCloseReason.Escape);
            return;
        }

        EnterClipboardMode();
    }

    private void EnterClipboardMode()
    {
        if (IsClipboardMode || _disposed)
        {
            return;
        }

        try
        {
            // Record the destination BEFORE anything appears on screen. §18.
            _target = PasteTarget.Capture();
            var target = _target;
            var anchorStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var anchorResult = Win32Focus.ResolveAnchor(_target.Snapshot, out var deferredAutomation);
            var anchor = anchorResult.Point;
            Diagnostics.DiagnosticsLog.Write("anchor",
                $"source={anchorResult.Source} x={anchor.X} y={anchor.Y} elapsedMs={System.Diagnostics.Stopwatch.GetElapsedTime(anchorStarted).TotalMilliseconds:F1} targetProcessId={_target.Snapshot.ProcessId}");

            _popup ??= CreatePopup();
            var currentTheme = Theme;
            if (_popup.Theme != currentTheme)
            {
                _popup.SetTheme(currentTheme);
            }

            var showStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            _popup.ShowAt(_history.Snapshot(), anchor.X, anchor.Y, preferredIndex: 0);
            Diagnostics.DiagnosticsLog.Write("performance",
                $"popupShowMs={System.Diagnostics.Stopwatch.GetElapsedTime(showStarted).TotalMilliseconds:F1} rows={_history.Count}");

            // A hidden popup is indistinguishable from a crash to the user, so the
            // visibility Windows actually reports is logged rather than assumed.
            Diagnostics.DiagnosticsLog.Write(
                "mode",
                $"showAt hwnd=0x{_popup.WindowHandle.ToInt64():X} win32Visible={_popup.IsWindowShown} wpfVisible={_popup.IsVisible} theme={_popup.Theme}");

            // The hook goes on only now, and it owns exactly four keys.
            var hookInstalled = _keyboardHook.Install();
            if (!hookInstalled)
            {
                LastNotice = "键盘拦截不可用，已关闭候选框";
                _popup.Close1(PopupCloseReason.FocusLost);
                _target = PasteTarget.Empty;
                RaiseStateChanged();
                return;
            }

            IsClipboardMode = true;

            if (deferredAutomation is not null)
            {
                _ = ApplyDeferredAnchorAsync(deferredAutomation, target, _popup);
            }

            // Alt is still physically down from the hotkey. Leave it down and the next real
            // keypress is delivered to the application as an Alt shortcut. §9.2.
            PasteController.ReleaseHeldModifiers();

            Diagnostics.DiagnosticsLog.Write(
                "mode",
                $"entered rows={_history.Count} targetProcessId={_target.Snapshot.ProcessId} hook={hookInstalled}");
        }
        catch (Exception ex)
        {
            // Entering the mode must not be able to leave the app half-armed.
            Diagnostics.DiagnosticsLog.Write("mode", $"enter failed {ex.GetType().Name}");
            IsClipboardMode = false;
            _keyboardHook.Uninstall();
            _popup?.Close1(PopupCloseReason.FocusLost);
            _target = PasteTarget.Empty;
            LastNotice = "候选框打开失败";
            RaiseStateChanged();
            return;
        }

        RaiseStateChanged();
    }

    private async Task ApplyDeferredAnchorAsync(Task<Win32Focus.FocusAnchorResult?> deferred,
        PasteTarget target, PopupWindow popup)
    {
        try
        {
            if (await Task.WhenAny(deferred, Task.Delay(600)).ConfigureAwait(true) != deferred)
                return;
            var anchor = await deferred.ConfigureAwait(true);
            if (anchor is not { } resolved || _disposed || !IsClipboardMode
                || !ReferenceEquals(_target, target) || !ReferenceEquals(_popup, popup)
                || !popup.IsPopupOpen || !target.StillValid())
                return;

            popup.MoveTo(resolved.Point.X, resolved.Point.Y);
            Diagnostics.DiagnosticsLog.Write("anchor",
                $"deferredSource={resolved.Source} x={resolved.Point.X} y={resolved.Point.Y}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Diagnostics.DiagnosticsLog.Write("anchor", $"deferredFailed={ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Leaves Clipboard Mode and restores normal typing. This is the single exit path:
    /// Esc, Enter, a second hotkey press, a focus change, an exception and shutdown all
    /// arrive here, which is what makes "no leaked keys" checkable by reading one method. §9.
    /// </summary>
    public void ExitClipboardMode(PopupCloseReason reason)
    {

        if (!IsClipboardMode)
        {
            return;
        }

        IsClipboardMode = false;

        _keyboardHook.Uninstall();

        _popup?.Close1(reason);

        _target = PasteTarget.Empty;

        RaiseStateChanged();
    }

    /// <summary>
    /// Appends a trace line outside the diagnostics log, so the two can be compared when
    /// the logging path itself is suspect. States only — never clip content.
    /// </summary>
    /// <remarks>
    /// Kept, unused by default, because it earned its place: when Clipboard Mode appeared
    /// to ignore the arrow keys, the structured log could not distinguish "the hook was
    /// never called" from "the hook was called and declined", and this file could. Enable
    /// it by calling it from <see cref="OnNavigationKey"/> while investigating a key-routing
    /// report; it is not on the normal path because it writes to disk on every keystroke.
    /// </remarks>
    private static void WriteTrace(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "miniclip-trace.txt"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tracing must never be the reason something fails.
        }
    }

    private PopupWindow CreatePopup()
    {
        var popup = new PopupWindow(Theme);
        popup.PasteRequested += OnPasteRequested;
        popup.Closed1 += (_, reason) =>
        {
            // Whatever closed it, Clipboard Mode must end with it.
            if (IsClipboardMode)
            {
                ExitClipboardMode(reason);
            }
        };

        return popup;
    }

    // ------------------------------------------------------------------ navigation

    private HookKeyAction OnNavigationKey(uint virtualKey, bool isKeyDown)
    {
        Diagnostics.HookTrace.RecordKey(virtualKey);

        if (!IsClipboardMode || !isKeyDown)
        {
            return HookKeyAction.PassThrough;
        }

        try
        {
            // The popup is not activated, so it cannot receive these keys as window
            // messages; the hook hands them to it directly.
            var handled = _popup is not null && _popup.HandleNavigationKey(virtualKey);

            return handled ? HookKeyAction.Consume : HookKeyAction.PassThrough;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            // If navigation itself fails, get out of the user's way immediately.
            ExitClipboardMode(PopupCloseReason.FocusLost);
            return HookKeyAction.Swallow;
        }
    }

    // ------------------------------------------------------------------ paste

    private async void OnPasteRequested(object? sender, string text)
    {

        if (!IsClipboardMode || _isPasting)
        {
            return;
        }

        _isPasting = true;
        try
        {
            var target = _target;

            // Leave Clipboard Mode first so the hook cannot see, or interfere with, the
            // Ctrl+V we are about to synthesise. §18.
            ExitClipboardMode(PopupCloseReason.Pasted);

            PasteResult result;
            try
            {
                result = await _paste.PasteAsync(text, target).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new PasteResult(PasteStatus.InputFailed);
                LastNotice = "粘贴失败";
            }

            // Remember the sequence our own write produced so the watcher ignores it.
            _ignoredSequence = _paste.LastSelfWriteSequence;

            if (!result.Succeeded)
            {
                LastNotice = result.Message;
            }

            RaiseStateChanged();
        }
        finally
        {
            _isPasting = false;
        }
    }

    // ------------------------------------------------------------------ commands

    /// <summary>Empties memory and disk together. The system clipboard is left alone. §12, §15.</summary>
    public async Task<bool> ClearHistoryAsync()
    {
        ExitClipboardMode(PopupCloseReason.Escape);

        // Any clipboard read that began before the user's clear is no longer allowed to
        // repopulate the store after deletion, even if it finishes successfully.
        _historyEpoch++;
        _clearedClipboardSequence = _clipboardSequenceReader();

        await _history.ClearAsync().ConfigureAwait(true);
        var clearedVersion = _historyWrites.Version;

        var delete = await _storage.DeleteAsync().ConfigureAwait(true);
        if (delete.Outcome == StorageOutcome.Failed)
        {
            LastNotice = "历史文件删除失败，重启后可能恢复";
            _historyWritesBlocked = true;
        }
        else
        {
            _historyWrites.MarkSaved(clearedVersion);
            _historyWritesBlocked = false;
            LastNotice = null;
        }

        RaiseStateChanged();
        return delete.Outcome != StorageOutcome.Failed;
    }

    /// <summary>Final best-effort save on the way out. Not the only save path, by design. §1.3.</summary>
    public async Task FlushAsync()
    {
        if (!_historyWrites.IsDirty)
        {
            return;
        }

        try
        {
            await PersistAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Shutting down; nothing useful left to do with the failure.
        }
    }

    public void OpenHistoryFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(JsonStorage.DefaultHistoryPath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            ShellOpen(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastNotice = "无法打开历史文件位置";
            RaiseStateChanged();
        }
    }

    private static void ShellOpen(string path)
    {
        NativeMethods.ShellExecuteW(IntPtr.Zero, "open", path, null, null, 1);
    }

    public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ExitClipboardMode(PopupCloseReason.Shutdown);

        MessageWindow.Message -= OnWindowMessage;
        _watcher.Updated -= OnClipboardUpdated;
        _watcher.Dispose();
        _keyboardHook.Dispose();
        _hotkeys.Dispose();

        _popup?.Close();
        _popup = null;
    }
}
