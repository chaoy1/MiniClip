using System.Runtime.InteropServices;

namespace MiniClip.Input;

/// <summary>
/// The keys Clipboard Mode temporarily takes over. Nothing else is ever swallowed. §9.2.
/// </summary>
public enum HookKeyAction
{
    /// <summary>Not ours — pass it straight through to the foreground application.</summary>
    PassThrough,

    /// <summary>Swallow the event and act on it.</summary>
    Consume,

    /// <summary>Swallow the event and do nothing (used to release keys we captured earlier).</summary>
    Swallow,
}

/// <summary>
/// A low-level keyboard hook (<c>WH_KEYBOARD_LL</c>) that is installed <em>only</em> while
/// the candidate popup is open, so ↑ ↓ Enter Esc are routed to MiniClip instead of the
/// focused application. §8, §9, §28.3.
/// </summary>
/// <remarks>
/// Rules this type enforces so callers cannot get them wrong:
/// <list type="bullet">
/// <item>Injected events (our own <c>SendInput</c>) are never swallowed, so the Ctrl+V we
/// synthesise is always delivered.</item>
/// <item>When a key-down is consumed its matching key-up is consumed too, otherwise the
/// destination application receives a stray key-up for a key it never saw pressed.</item>
/// <item>The callback is short and allocation-free; the hook is removed unconditionally on
/// dispose, on leaving Clipboard Mode and on process exit.</item>
/// </list>
/// </remarks>
public sealed class KeyboardHook : IDisposable
{
    /// <summary>The keys Clipboard Mode owns. Deliberately tiny. §9.2.</summary>
    private static readonly uint[] NavigateKeys =
    [
        NativeConstants.VK_UP,
        NativeConstants.VK_DOWN,
        NativeConstants.VK_RETURN,
        NativeConstants.VK_ESCAPE,
    ];

    /// <summary>
    /// The modifier keys, tracked from the hook stream rather than polled.
    /// </summary>
    /// <remarks>
    /// This distinction is the difference between working and not working. The obvious
    /// implementation of "are any modifiers held?" is <c>GetAsyncKeyState</c>, and it is
    /// wrong here: the hotkey that opened the popup is Alt+V, so the user is still
    /// physically holding Alt when the first ↑ arrives. MiniClip releases Alt
    /// synthetically, but <c>SendInput</c> only queues the event — the asynchronous key
    /// state does not yet reflect it — so a poll still reports Alt as down, every
    /// navigation key looks like Alt+↑, and the whole list is dead with no error
    /// anywhere. Tracking the events as they pass through the hook is both accurate and
    /// free.
    /// </remarks>
    private readonly HashSet<uint> _modifiersDown = [];

    /// <summary>
    /// Keys whose key-down MiniClip consumed, so the matching key-up can be consumed too
    /// rather than delivered to an application that never saw the key go down.
    /// </summary>
    private readonly HashSet<uint> _swallowed = [];

    private static readonly uint[] ModifierKeys =
    [
        NativeConstants.VK_SHIFT,
        NativeConstants.VK_CONTROL,
        NativeConstants.VK_MENU,
        NativeConstants.VK_LWIN,
        NativeConstants.VK_RWIN,
    ];

    private NativeMethods.LowLevelKeyboardProc? _proc;
    private IntPtr _handle = IntPtr.Zero;
    private bool _disposed;

    /// <summary>
    /// Decides what to do with a key. Only called for the four navigation keys, with no
    /// extra modifiers held, and only on the thread that installed the hook.
    /// <paramref name="isKeyDown"/> is false for the key-up half of the pair.
    /// </summary>
    public Func<uint, bool, HookKeyAction>? KeyHandler { get; set; }

    /// <summary>Raised when installing or removing the hook fails, with a short reason.</summary>
    public event EventHandler<string>? HookFailed;

    public bool IsInstalled => _handle != IntPtr.Zero;

    /// <summary>Installs the hook. Returns false when Windows refused.</summary>
    public bool Install()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_handle != IntPtr.Zero)
        {
            return true;
        }

        _proc = HookCallback;
        _handle = Win32Hook.InstallKeyboardHook(_proc);
        if (_handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _proc = null;
            HookFailed?.Invoke(this, NativeErrors.Describe(error));
            return false;
        }

        return true;
    }

    /// <summary>Removes the hook and forgets which keys were captured.</summary>
    public void Uninstall()
    {
        if (_handle != IntPtr.Zero)
        {
            if (!Win32Hook.UninstallHook(_handle))
            {
                HookFailed?.Invoke(this, NativeErrors.Describe(Marshal.GetLastWin32Error()));
            }

            _handle = IntPtr.Zero;
        }

        _proc = null;
        _swallowed.Clear();
        _modifiersDown.Clear();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        Diagnostics.HookTrace.Record(nCode, wParam);

        // A negative nCode means "pass it on without inspection" — a hard requirement of the API.
        if (nCode < NativeConstants.HC_ACTION || _handle == IntPtr.Zero)
        {
            return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        var message = (int)wParam;
        var isKeyDown = message is NativeConstants.WM_KEYDOWN or NativeConstants.WM_SYSKEYDOWN;
        var isKeyUp = message is NativeConstants.WM_KEYUP or NativeConstants.WM_SYSKEYUP;

        if (!isKeyDown && !isKeyUp)
        {
            return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        NativeMethods.KBDLLHOOKSTRUCT data;
        try
        {
            data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        }
        catch (ArgumentException)
        {
            return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        var virtualKey = data.vkCode;

        // Never touch anything another process injected: our own Ctrl+V and our own
        // synthetic Alt-release both travel through here. Injected events are also
        // ignored for modifier tracking, so MiniClip's own release cannot confuse it.
        var injected = (data.flags & (NativeConstants.LLKHF_INJECTED | NativeConstants.LLKHF_LOWER_IL_INJECTED)) != 0;
        if (injected)
        {
            return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        // Keep the modifier picture current from the real event stream.
        if (ModifierKeys.Contains(virtualKey))
        {
            if (isKeyDown)
            {
                _modifiersDown.Add(virtualKey);
            }
            else
            {
                _modifiersDown.Remove(virtualKey);
            }

            return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        // Release the swallowed bookkeeping on the key-up half.
        if (isKeyUp && _swallowed.Remove(virtualKey))
        {
            return new IntPtr(1);
        }

        if (!isKeyDown || !NavigateKeys.Contains(virtualKey))
        {
            return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        // Only bare navigation keys belong to us. Alt+↑ or Ctrl+Enter stay with the
        // application, which is why the modifier set is maintained above.
        if (_modifiersDown.Count > 0)
        {
            return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
        }

        var action = KeyHandler?.Invoke(virtualKey, true) ?? HookKeyAction.PassThrough;
        switch (action)
        {
            case HookKeyAction.Consume:
                _swallowed.Add(virtualKey);
                return new IntPtr(1);

            case HookKeyAction.Swallow:
                return new IntPtr(1);

            default:
                return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
        }
    }

    /// <summary>
    /// True when any modifier is currently held, according to the hook's own tracking.
    /// Shift+↑ is a selection gesture in most editors and must stay there.
    /// </summary>
    private bool HasExtraModifiers() => _modifiersDown.Count > 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Uninstall();
        KeyHandler = null;
    }
}
