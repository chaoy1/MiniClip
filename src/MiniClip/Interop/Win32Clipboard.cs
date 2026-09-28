using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using MiniClip.History;

namespace MiniClip.Interop;

/// <summary>
/// Clipboard access. Reads prefer <c>CF_UNICODETEXT</c> and never look at rich formats —
/// V1 is text-only on purpose. §10.1.
/// </summary>
public static class Win32Clipboard
{
    public const uint CF_TEXT = 1;
    public const uint CF_UNICODETEXT = 13;

    /// <summary>Registered once; marks a clipboard update as MiniClip's own write. §17.</summary>
    private static readonly uint MiniClipMarkerFormat = RegisterMarkerFormat();
    private static readonly uint TextPlainFormat = RegisterTextFormat("text/plain");
    private static readonly uint PlainTextFormat = RegisterTextFormat("Plain Text");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private const int OpenRetries = 5;
    private const int OpenRetryDelayMilliseconds = 4;

    private static uint RegisterMarkerFormat()
    {
        try
        {
            return NativeMethods.RegisterClipboardFormatW("MiniClip.Text");
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return 0;
        }
    }

    private static uint RegisterTextFormat(string name)
    {
        try { return NativeMethods.RegisterClipboardFormatW(name); }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { return 0; }
    }

    /// <summary>True when the clipboard offers something we can read as plain text.</summary>
    public static bool ContainsText()
    {
        try
        {
            if (IsFormatAvailable(CF_UNICODETEXT))
            {
                return true;
            }

            if (IsFormatAvailable(CF_TEXT))
            {
                return true;
            }

            // Some applications publish HTML without CF_UNICODETEXT but do publish a
            // "text" flavour under a registered name. Accept the common ones.
            return (TextPlainFormat != 0 && IsFormatAvailable(TextPlainFormat))
                   || (PlainTextFormat != 0 && IsFormatAvailable(PlainTextFormat));
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>True when the clipboard holds data but no readable text (image, file list, HTML-only).</summary>
    public static bool ContainsNonTextOnly()
    {
        try
        {
            return !ContainsText() && NativeMethods.CountClipboardFormats() > 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the clipboard as plain text. Returns null when the clipboard holds no text,
    /// is locked by another process, or the text is empty. Never throws.
    /// </summary>
    public static string? GetText()
    {
        return WithClipboard(hGlobal =>
        {
            if (hGlobal == IntPtr.Zero)
            {
                return ReadRegisteredText(TextPlainFormat) ?? ReadRegisteredText(PlainTextFormat);
            }

            var pointer = NativeMethods.GlobalLock(hGlobal);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var text = Marshal.PtrToStringUni(pointer);
                return string.IsNullOrEmpty(text) ? null : text;
            }
            finally
            {
                NativeMethods.GlobalUnlock(hGlobal);
            }
        });
    }

    /// <summary>
    /// Writes plain text, replacing the clipboard contents. Adds a private marker format
    /// so the clipboard watcher can recognise the write as MiniClip's own. §17.
    /// </summary>
    public static bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!TryOpenClipboard())
        {
            return false;
        }

        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                return false;
            }

            if (!SetUnicodeText(text))
            {
                return false;
            }

            // Best-effort marker. A failure here costs us a spurious history entry at
            // worst, so it must not fail the write.
            SetMarker();

            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>Subscribes a window to <c>WM_CLIPBOARDUPDATE</c>.</summary>
    public static bool RegisterListener(IntPtr hWnd) =>
        hWnd != IntPtr.Zero && NativeMethods.AddClipboardFormatListener(hWnd);

    /// <summary>Unsubscribes a window. Must be paired with <see cref="RegisterListener"/> on shutdown.</summary>
    public static bool UnregisterListener(IntPtr hWnd) =>
        hWnd != IntPtr.Zero && NativeMethods.RemoveClipboardFormatListener(hWnd);

    /// <summary>
    /// Monotonic counter Windows bumps on every clipboard change. MiniClip records the
    /// value around its own writes so it can ignore their echo without comparing text.
    /// </summary>
    public static uint SequenceNumber
    {
        get
        {
            try
            {
                return NativeMethods.GetClipboardSequenceNumber();
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
            {
                return 0;
            }
        }
    }

    private static bool SetUnicodeText(string text)
    {
        var byteCount = (text.Length + 1) * sizeof(char);
        var hGlobal = NativeMethods.GlobalAlloc(0x0042 /* GMEM_MOVEABLE | GMEM_ZEROINIT */, (UIntPtr)byteCount);
        if (hGlobal == IntPtr.Zero)
        {
            return false;
        }

        var pointer = NativeMethods.GlobalLock(hGlobal);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hGlobal);
            return false;
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            // GlobalAlloc with GMEM_ZEROINIT already terminated the buffer; be explicit anyway.
            Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
        }
        catch
        {
            NativeMethods.GlobalUnlock(hGlobal);
            NativeMethods.GlobalFree(hGlobal);
            throw;
        }

        NativeMethods.GlobalUnlock(hGlobal);

        if (NativeMethods.SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
        {
            // Ownership was NOT transferred, so this handle is still ours to free.
            NativeMethods.GlobalFree(hGlobal);
            return false;
        }

        // Ownership has passed to the clipboard; freeing it here would be a use-after-free.
        return true;
    }

    private static void SetMarker()
    {
        if (MiniClipMarkerFormat == 0)
        {
            return;
        }

        var marker = Encoding.UTF8.GetBytes("1");
        var hGlobal = NativeMethods.GlobalAlloc(0x0042, (UIntPtr)(marker.Length + 1));
        if (hGlobal == IntPtr.Zero)
        {
            return;
        }

        var pointer = NativeMethods.GlobalLock(hGlobal);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hGlobal);
            return;
        }

        Marshal.Copy(marker, 0, pointer, marker.Length);
        Marshal.WriteByte(pointer, marker.Length, 0);
        NativeMethods.GlobalUnlock(hGlobal);

        if (NativeMethods.SetClipboardData(MiniClipMarkerFormat, hGlobal) == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(hGlobal);
        }
    }

    /// <summary>True when the clipboard carries MiniClip's own write marker.</summary>
    public static bool HasSelfMarker() => MiniClipMarkerFormat != 0 && IsFormatAvailable(MiniClipMarkerFormat);

    private static bool IsFormatAvailable(uint format)
    {
        try
        {
            return NativeMethods.IsClipboardFormatAvailable(format);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    private static string? ReadRegisteredText(uint format)
    {
        if (format == 0 || !IsFormatAvailable(format))
            return null;

        var handle = NativeMethods.GetClipboardData(format);
        if (handle == IntPtr.Zero)
            return null;

        var size = NativeMethods.GlobalSize(handle).ToUInt64();
        if (size == 0 || size > (ulong)HistoryManager.MaxEntryLength * 4 + 4096)
            return null;

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
            return null;

        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            var length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) length = bytes.Length;
            if (length == 0) return null;

            var offset = length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            try
            {
                return StrictUtf8.GetString(bytes, offset, length - offset);
            }
            catch (DecoderFallbackException)
            {
                // Older applications sometimes publish this registered format using
                // the machine's ANSI code page rather than UTF-8.
                try
                {
                    var codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                    return Encoding.GetEncoding(codePage).GetString(bytes, 0, length);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    return null;
                }
            }
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <summary>
    /// Opens the clipboard with a short bounded retry — another process holding it is
    /// normal (every clipboard manager on the machine contends for it) and is not an error
    /// worth surfacing. This is the only place MiniClip blocks, and never for long.
    /// </summary>
    private static bool TryOpenClipboard()
    {
        for (var attempt = 0; attempt < OpenRetries; attempt++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(OpenRetryDelayMilliseconds);
        }

        return false;
    }

    private static T WithClipboard<T>(Func<IntPtr, T> read)
    {
        if (!TryOpenClipboard())
        {
            return default!;
        }

        try
        {
            return read(NativeMethods.GetClipboardData(CF_UNICODETEXT));
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }
}

/// <summary>Synthetic keyboard input. Used for exactly one thing: delivering Ctrl+V.</summary>
public static class Win32Input
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const int VK_CONTROL = 0x11;
    private const int VK_V = 0x56;

    /// <summary>
    /// Sends Ctrl+V to whatever window currently has the keyboard. The caller is
    /// responsible for having verified that this is still the intended destination.
    /// </summary>
    public static bool SendCtrlV()
    {
        var inputs = new NativeMethods.INPUT[4];

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = VK_CONTROL;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = VK_V;

        inputs[2].type = INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = VK_V;
        inputs[2].u.ki.dwFlags = KEYEVENTF_KEYUP;

        inputs[3].type = INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = VK_CONTROL;
        inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;

        return Send(inputs);
    }

    /// <summary>Sends a single key down or up. Used to release modifiers the user is still holding.</summary>
    public static bool SendKeyEvent(uint virtualKey, bool keyUp)
    {
        var inputs = new NativeMethods.INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = (ushort)virtualKey;
        inputs[0].u.ki.dwFlags = keyUp ? KEYEVENTF_KEYUP : 0;

        if (virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E)
        {
            inputs[0].u.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
        }

        return Send(inputs);
    }

    public static bool SendKeyCombo(uint virtualKey, uint flags)
    {
        var inputs = new NativeMethods.INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = (ushort)virtualKey;
        inputs[0].u.ki.dwFlags = flags;
        return Send(inputs);
    }

    private static bool Send(NativeMethods.INPUT[] inputs)
    {
        try
        {
            var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            return sent == inputs.Length;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Physically held state, used to decide which modifiers still need releasing.</summary>
    public static bool IsKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static short GetKeyState(int virtualKey) => NativeMethods.GetKeyState(virtualKey);
}

/// <summary>Global hotkey registration primitives.</summary>
public static class Win32Hotkey
{
    public static bool Register(IntPtr hWnd, int id, uint modifiers, uint virtualKey) =>
        hWnd != IntPtr.Zero && NativeMethods.RegisterHotKey(hWnd, id, modifiers, virtualKey);

    public static bool Unregister(IntPtr hWnd, int id) =>
        hWnd != IntPtr.Zero && NativeMethods.UnregisterHotKey(hWnd, id);

    /// <summary>Registers and hands back the Win32 error so the UI can explain the refusal.</summary>
    public static bool TryRegister(IntPtr hWnd, int id, uint modifiers, uint virtualKey, out int win32Error)
    {
        win32Error = NativeErrors.ERROR_SUCCESS;

        if (hWnd == IntPtr.Zero)
        {
            win32Error = NativeErrors.ERROR_INVALID_WINDOW_HANDLE;
            return false;
        }

        if (NativeMethods.RegisterHotKey(hWnd, id, modifiers, virtualKey))
        {
            return true;
        }

        win32Error = Marshal.GetLastWin32Error();
        return false;
    }
}

/// <summary>Low-level keyboard hook primitives. Install policy lives in the caller. §28.3.</summary>
public static class Win32Hook
{
    private static readonly IntPtr ModuleHandle = NativeMethods.GetModuleHandleW(null);

    public static IntPtr InstallKeyboardHook(NativeMethods.LowLevelKeyboardProc proc)
    {
        ArgumentNullException.ThrowIfNull(proc);

        try
        {
            return NativeMethods.SetWindowsHookExW(NativeConstants.WH_KEYBOARD_LL, proc, ModuleHandle, 0);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    public static bool UninstallHook(IntPtr hook) =>
        hook != IntPtr.Zero && NativeMethods.UnhookWindowsHookEx(hook);
}

/// <summary>Icon handle creation and disposal.</summary>
public static class Win32Icons
{
    /// <summary>
    /// Converts a managed bitmap to an HICON. The returned handle is owned by the
    /// caller and must be released with <see cref="DestroyIconHandle"/>, which is what
    /// <paramref name="ownedHandle"/> reports back.
    /// </summary>
    public static IntPtr CreateIconFromBitmap(System.Drawing.Bitmap bitmap, out IntPtr ownedHandle)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        ownedHandle = IntPtr.Zero;
        try
        {
            var handle = bitmap.GetHicon();
            ownedHandle = handle;
            return handle;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException)
        {
            return IntPtr.Zero;
        }
    }

    public static void DestroyIconHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
