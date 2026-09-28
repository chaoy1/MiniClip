using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniClip.Interop;

/// <summary>Window, process, monitor and DPI queries used by the popup and the paste guard.</summary>
public static class Win32Windows
{
    private const int MaxTitleLength = 512;
    private const int MaxClassLength = 256;

    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public static uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId)
    {
        processId = 0;
        return hWnd == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(hWnd, out processId);
    }

    public static bool IsWindow(IntPtr hWnd) => hWnd != IntPtr.Zero && NativeMethods.IsWindow(hWnd);

    public static bool IsWindowVisible(IntPtr hWnd) => hWnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hWnd);

    /// <summary>Focus handle of the calling thread's input queue (almost never useful — see <see cref="Win32Focus"/>).</summary>
    public static IntPtr GetFocus() => NativeMethods.GetFocus();

    public static IntPtr GetAncestor(IntPtr hWnd, uint flags) =>
        hWnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hWnd, flags);

    /// <summary>Window title, or an empty string. Never throws.</summary>
    public static string GetWindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var buffer = new char[MaxTitleLength];
            var length = NativeMethods.GetWindowTextW(hWnd, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or MarshalDirectiveException)
        {
            return string.Empty;
        }
    }

    /// <summary>Window class name, or an empty string. Never throws.</summary>
    public static string GetClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var buffer = new char[MaxClassLength];
            var length = NativeMethods.GetClassNameW(hWnd, buffer, buffer.Length);
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or MarshalDirectiveException)
        {
            return string.Empty;
        }
    }

    /// <summary>Executable name without extension ("chrome"), lower-case. Empty when unavailable.</summary>
    public static string GetProcessName(uint processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var name = process.ProcessName;
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    public static bool TryGetWindowRect(IntPtr hWnd, out System.Drawing.Rectangle rect)
    {
        rect = System.Drawing.Rectangle.Empty;
        if (hWnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hWnd, out var native))
        {
            return false;
        }

        rect = System.Drawing.Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        return true;
    }

    /// <summary>Effective DPI of the monitor a window is on. Falls back to the system DPI.</summary>
    public static int GetDpiForWindow(IntPtr hWnd)
    {
        try
        {
            var dpi = hWnd != IntPtr.Zero ? NativeMethods.GetDpiForWindow(hWnd) : 0;
            if (dpi == 0)
            {
                dpi = NativeMethods.GetDpiForSystem();
            }

            return dpi == 0 ? 96 : (int)dpi;
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-1607 Windows has no per-window DPI query.
            return 96;
        }
    }

    public static double GetDpiScaleForWindow(IntPtr hWnd) => GetDpiForWindow(hWnd) / 96.0;

    public static bool TryGetCursorPosition(out System.Drawing.Point point)
    {
        if (NativeMethods.GetCursorPos(out var native))
        {
            point = new System.Drawing.Point(native.X, native.Y);
            return true;
        }

        point = System.Drawing.Point.Empty;
        return false;
    }

    /// <summary>
    /// Best-effort attempt to bring a window to the foreground, working around the
    /// foreground lock by temporarily joining the current foreground thread's input queue.
    /// Used only for the settings dialog the user explicitly asked for; never for the popup.
    /// </summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hWnd)
        {
            return;
        }

        var foregroundThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();

        var attached = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            }

            NativeMethods.SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}

/// <summary>Monitor and work-area geometry. Everything here is in physical pixels.</summary>
public static class Win32Screen
{
    public static IntPtr MonitorFromPoint(int x, int y, uint flags = NativeConstants.MONITOR_DEFAULTTONEAREST)
    {
        var point = new NativeMethods.POINT { X = x, Y = y };
        return NativeMethods.MonitorFromPoint(point, flags);
    }

    public static bool TryGetMonitorInfo(IntPtr hMonitor, out MonitorInfo info)
    {
        info = default;
        if (hMonitor == IntPtr.Zero)
        {
            return false;
        }

        var native = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(hMonitor, ref native))
        {
            return false;
        }

        info = new MonitorInfo(native);
        return true;
    }

    /// <summary>
    /// The work area (taskbar excluded) of the monitor nearest the given point. This is the
    /// region the popup is allowed to occupy. §14.
    /// </summary>
    public static bool TryGetWorkArea(int x, int y, out System.Drawing.Rectangle workArea)
    {
        workArea = System.Drawing.Rectangle.Empty;
        var monitor = MonitorFromPoint(x, y);
        if (monitor == IntPtr.Zero || !TryGetMonitorInfo(monitor, out var info))
        {
            return false;
        }

        workArea = info.WorkArea;
        return true;
    }

    public static int GetPrimaryMonitorHeight()
    {
        var monitor = MonitorFromPoint(0, 0, NativeConstants.MONITOR_DEFAULTTOPRIMARY);
        return TryGetMonitorInfo(monitor, out var info) ? info.MonitorRect.Height : 1080;
    }
}

/// <summary>Monitor geometry, decoded from <c>MONITORINFO</c>.</summary>
public readonly struct MonitorInfo
{
    internal MonitorInfo(NativeMethods.MONITORINFO native)
    {
        MonitorRect = System.Drawing.Rectangle.FromLTRB(
            native.rcMonitor.Left, native.rcMonitor.Top, native.rcMonitor.Right, native.rcMonitor.Bottom);
        WorkArea = System.Drawing.Rectangle.FromLTRB(
            native.rcWork.Left, native.rcWork.Top, native.rcWork.Right, native.rcWork.Bottom);
        IsPrimary = (native.dwFlags & 1) != 0;
    }

    public System.Drawing.Rectangle MonitorRect { get; }

    public System.Drawing.Rectangle WorkArea { get; }

    public bool IsPrimary { get; }
}
