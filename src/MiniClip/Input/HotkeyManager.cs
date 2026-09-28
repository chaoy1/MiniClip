namespace MiniClip.Input;

/// <summary>Why a hotkey could not be registered, in terms the user can act on.</summary>
public enum HotkeyRegistrationStatus
{
    NotAttempted,

    /// <summary>Windows accepted the combination.</summary>
    Registered,

    /// <summary>Another program already owns this combination (Win32 error 1409). Recoverable by choosing another.</summary>
    AlreadyInUse,

    /// <summary>The combination is rejected by MiniClip's own safety rules — see <see cref="HotkeyGesture.IsValid"/>.</summary>
    InvalidCombination,

    /// <summary>Windows refused for some other reason.</summary>
    Failed,
}

public sealed record HotkeyRegistrationResult(HotkeyRegistrationStatus Status, HotkeyGesture Gesture, string Message)
{
    public bool IsRegistered => Status == HotkeyRegistrationStatus.Registered;
}

/// <summary>
/// Owns the global hotkey that opens the candidate popup. Exactly one hotkey is
/// registered at a time, and a failed registration is always surfaced — MiniClip
/// must never pretend the shortcut works when Windows refused it. §15, §29 stage 2.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly NativeMessageWindow _window;
    private readonly int _hotkeyId;
    private bool _registered;
    private bool _disposed;

    public HotkeyManager(NativeMessageWindow window, int hotkeyId = NativeConstants.FirstHotkeyId)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _hotkeyId = hotkeyId;
    }

    /// <summary>The combination currently owned by MiniClip, if any.</summary>
    public HotkeyGesture? Current { get; private set; }

    public HotkeyRegistrationStatus Status { get; private set; } = HotkeyRegistrationStatus.NotAttempted;

    /// <summary>Last message describing the registration state, ready for the tray tooltip or menu.</summary>
    public string StatusMessage { get; private set; } = "尚未注册快捷键";

    /// <summary>Raised after every registration attempt, successful or not.</summary>
    public event EventHandler<HotkeyRegistrationResult>? RegistrationChanged;

    /// <summary>
    /// Registers <paramref name="gesture"/>, replacing any previous registration.
    /// The old combination is released first so switching hotkeys cannot deadlock on itself.
    /// </summary>
    public HotkeyRegistrationResult Register(HotkeyGesture gesture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UnregisterInternal();

        if (!gesture.IsValid)
        {
            return Publish(new HotkeyRegistrationResult(
                HotkeyRegistrationStatus.InvalidCombination,
                gesture,
                "这个组合不能用作全局快捷键：需要包含 Ctrl、Alt 或 Shift，并且不能是系统保留的组合"));
        }

        if (!_window.IsCreated && !_window.TryCreate())
        {
            return Publish(new HotkeyRegistrationResult(
                HotkeyRegistrationStatus.Failed,
                gesture,
                "无法创建消息窗口，快捷键不可用"));
        }

        // MOD_NOREPEAT is essential, not a nicety: without it, holding the hotkey makes
        // Windows repeat WM_HOTKEY at the keyboard repeat rate, which thrashes the popup
        // open and closed dozens of times a second. §9.2.
        var modifiers = gesture.Win32Modifiers | (uint)HotkeyModifiers.NoRepeat;
        if (!Win32Hotkey.TryRegister(_window.Handle, _hotkeyId, modifiers, gesture.VirtualKey, out var error))
        {
            var status = error == NativeErrors.ERROR_HOTKEY_ALREADY_REGISTERED
                ? HotkeyRegistrationStatus.AlreadyInUse
                : HotkeyRegistrationStatus.Failed;

            var message = status == HotkeyRegistrationStatus.AlreadyInUse
                ? $"{gesture.ToDisplayString()} 已被其他程序占用"
                : $"注册 {gesture.ToDisplayString()} 失败：{NativeErrors.Describe(error)}";

            return Publish(new HotkeyRegistrationResult(status, gesture, message));
        }

        _registered = true;
        Current = gesture;

        return Publish(new HotkeyRegistrationResult(
            HotkeyRegistrationStatus.Registered,
            gesture,
            $"{gesture.ToDisplayString()}"));
    }

    /// <summary>Releases the hotkey. Safe to call when nothing is registered.</summary>
    public void Unregister()
    {
        var had = UnregisterInternal();
        if (had)
        {
            Current = null;
            Publish(new HotkeyRegistrationResult(HotkeyRegistrationStatus.NotAttempted, default, "快捷键已停用"));
        }
    }

    private bool UnregisterInternal()
    {
        if (!_registered)
        {
            return false;
        }

        if (_window.IsCreated)
        {
            Win32Hotkey.Unregister(_window.Handle, _hotkeyId);
        }

        _registered = false;
        Current = null;
        return true;
    }

    /// <summary>The hotkey id used in the message window's <c>WM_HOTKEY</c> notifications.</summary>
    public int HotkeyId => _hotkeyId;

    private HotkeyRegistrationResult Publish(HotkeyRegistrationResult result)
    {
        Status = result.Status;
        StatusMessage = result.Message;
        if (result.IsRegistered)
        {
            Current = result.Gesture;
        }

        RegistrationChanged?.Invoke(this, result);
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterInternal();
    }
}
