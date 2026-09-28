using System.Runtime.InteropServices;

namespace MiniClip.Interop;

/// <summary>
/// Raw Win32 entry points and structures. Nothing in here makes a policy decision —
/// it is a thin, literal mirror of the Windows API so the rest of the app can be read
/// without a header file open on the second monitor.
/// </summary>
/// <remarks>
/// Public rather than internal because the low-level keyboard hook's callback delegate
/// and its <c>KBDLLHOOKSTRUCT</c> payload cross the boundary into
/// <c>MiniClip.Input</c>; a delegate type is part of a public method's signature, so it
/// has to be at least as accessible as that method.
/// </remarks>
public static class NativeMethods
{
    public const string User32 = "user32.dll";
    public const string Kernel32 = "kernel32.dll";
    public const string Shell32 = "shell32.dll";
    public const string Gdi32 = "gdi32.dll";
    public const string Dwmapi = "dwmapi.dll";
    public const string Oleacc = "oleacc.dll";

    // ------------------------------------------------------------------ window class

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;

        /// <summary>
        /// A raw <c>LPCWSTR</c>, not a marshalled string, and that is deliberate.
        /// Declaring this as <c>[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]</c>
        /// yields a struct of exactly the right size (584 bytes on x64) that
        /// <c>RegisterClassExW</c> nevertheless rejects with ERROR_INVALID_PARAMETER.
        /// Measured on this machine against three variants; only the pointer forms work.
        /// </summary>
        public IntPtr lpszClassName;

        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    // ------------------------------------------------------------------ window

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(
        int dwExStyle, string lpClassName, string? lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport(User32, SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport(User32, SetLastError = true)]
    public static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport(User32)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport(User32, CharSet = CharSet.Unicode)]
    public static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport(User32, SetLastError = true)]
    public static extern bool PostMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport(User32, SetLastError = true)]
    public static extern bool PostThreadMessageW(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessageW(string lpString);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern uint GetCurrentThreadId();

    [DllImport(User32)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport(User32, SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport(User32, SetLastError = true)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport(User32, SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport(User32, SetLastError = true)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport(User32)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport(User32)]
    public static extern IntPtr GetFocus();

    [DllImport(User32)]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassNameW(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport(User32, SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(User32, SetLastError = true)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(User32, SetLastError = true)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport(User32, SetLastError = true)]
    public static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    [DllImport(User32, SetLastError = true)]
    public static extern int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport(User32, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport(User32, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport(User32, SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport(User32, SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport(User32, SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport(User32, SetLastError = true)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport(Oleacc)]
    public static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible? accessible);

    [DllImport(User32)]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport(User32)]
    public static extern uint GetDpiForSystem();

    [DllImport(User32)]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport(User32)]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    // ------------------------------------------------------------------ hotkey + hook

    [DllImport(User32, SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport(User32, SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport(User32, SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport(User32)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // ------------------------------------------------------------------ clipboard

    [DllImport(User32, SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport(User32, SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport(User32, SetLastError = true)]
    public static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport(User32, SetLastError = true)]
    public static extern bool CloseClipboard();

    [DllImport(User32, SetLastError = true)]
    public static extern bool EmptyClipboard();

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport(User32, SetLastError = true)]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport(User32)]
    public static extern uint EnumClipboardFormats(uint format);

    [DllImport(User32)]
    public static extern int CountClipboardFormats();

    [DllImport(User32, SetLastError = true)]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterClipboardFormatW(string lpszFormat);

    // ------------------------------------------------------------------ memory + input

    [DllImport(Kernel32, SetLastError = true)]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport(User32, SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport(User32)]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport(User32)]
    public static extern short GetKeyState(int nVirtKey);

    // ------------------------------------------------------------------ icons

    [DllImport(User32, SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport(Shell32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    // ------------------------------------------------------------------ menus

    [DllImport(User32, SetLastError = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport(User32, SetLastError = true)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport(User32, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport(User32, SetLastError = true)]
    public static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, uint fByPos);

    [DllImport(User32, SetLastError = true)]
    public static extern int TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    // ------------------------------------------------------------------ shell helpers

    [DllImport(Shell32, CharSet = CharSet.Unicode)]
    public static extern bool ShellExecuteW(IntPtr hwnd, string? lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);

    [DllImport(User32, SetLastError = true)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    // ------------------------------------------------------------------ tokens (integrity level)

    [DllImport(Kernel32, SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport(Kernel32, SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, ref int returnLength);
}
