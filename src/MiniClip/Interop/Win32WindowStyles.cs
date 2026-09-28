using System.Runtime.InteropServices;

namespace MiniClip.Interop;

/// <summary>
/// Window-style helpers for the candidate popup.
/// </summary>
/// <remarks>
/// This is the load-bearing part of the product. MiniClip is only useful if the window
/// that appears is one the user is <em>not</em> talking to: the application they were
/// typing in has to keep the keyboard. That means two things, and they are different:
/// <list type="number">
/// <item><c>WS_EX_NOACTIVATE</c> — the window can be clicked and still never take
/// activation. Set as an extended style, at creation time.</item>
/// <item><c>SWP_NOACTIVATE</c> / <c>SW_SHOWNOACTIVATE</c> — every show and move is
/// performed without asking the shell to activate anything. A single <c>ShowWindow</c>
/// without this flag hands the keyboard to the popup and the whole design collapses. §8.</item>
/// </list>
/// </remarks>
public static class Win32WindowStyles
{
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_HIDE = 0;

    /// <summary>
    /// Applies the popup's extended styles: no activation, topmost, and out of the
    /// Alt+Tab list and the taskbar. Returns false when the window handle was invalid.
    /// </summary>
    public static bool ApplyPopupStyles(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
        {
            return false;
        }

        var current = NativeMethods.GetWindowLongW(hWnd, NativeConstants.GWL_EXSTYLE);
        var desired = current
                      | NativeConstants.WS_EX_NOACTIVATE
                      | NativeConstants.WS_EX_TOOLWINDOW
                      | NativeConstants.WS_EX_TOPMOST;

        if (desired != current)
        {
            NativeMethods.SetWindowLongW(hWnd, NativeConstants.GWL_EXSTYLE, desired);
        }

        var style = NativeMethods.GetWindowLongW(hWnd, NativeConstants.GWL_STYLE);
        var desiredStyle = style | NativeConstants.WS_POPUP;
        if (desiredStyle != style)
        {
            NativeMethods.SetWindowLongW(hWnd, NativeConstants.GWL_STYLE, desiredStyle);
        }

        return true;
    }

    /// <summary>Shows the window without letting it become the foreground window.</summary>
    public static bool ShowWithoutActivating(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.ShowWindow(hWnd, SW_SHOWNOACTIVATE);
    }

    /// <summary>Moves the window without activating it or changing its z-order.</summary>
    public static bool MoveWithoutActivating(IntPtr hWnd, int x, int y)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            hWnd, IntPtr.Zero, x, y, 0, 0,
            NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER |
            NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_NOOWNERZORDER);
    }

    /// <summary>Shows and moves in one call, still without activating.</summary>
    public static bool ShowAtWithoutActivating(IntPtr hWnd, int x, int y)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            hWnd, IntPtr.Zero, x, y, 0, 0,
            NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER |
            NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_NOOWNERZORDER |
            NativeConstants.SWP_SHOWWINDOW);
    }

    public static bool HideWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.ShowWindow(hWnd, SW_HIDE);
    }

    /// <summary>
    /// Places a <paramref name="width"/> × <paramref name="height"/> rectangle near the
    /// anchor point, fully inside the work area of the monitor that contains it, nudging
    /// it inward at every edge so it is never clipped by a screen edge or the taskbar. §14.
    /// </summary>
    /// <remarks>
    /// The anchor is the text caret. The popup sits four pixels above it, with the
    /// newest row nearest the caret. When there is no room above, it sits below it.
    /// </remarks>
    public static System.Drawing.Point PositionWithinWorkArea(
        int anchorX, int anchorY, int width, int height, int offsetX = 0, int offsetY = 4)
    {
        var x = anchorX + offsetX;
        var y = anchorY - offsetY - height;

        if (!Win32Screen.TryGetWorkArea(anchorX, anchorY, out var work) || work.Width <= 0 || work.Height <= 0)
        {
            // No monitor information at all: fall back to the primary display's rough size.
            return new System.Drawing.Point(
                Math.Max(0, Math.Min(x, Math.Max(0, 1920 - width))),
                Math.Max(0, Math.Min(y, Math.Max(0, Win32Screen.GetPrimaryMonitorHeight() - height))));
        }

        if (x + width > work.Right)
        {
            x = anchorX - offsetY - width;
        }

        if (y < work.Top)
        {
            y = anchorY + 20;
        }

        // Clamp when neither side of the caret has enough room.
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));

        return new System.Drawing.Point(x, y);
    }

    /// <summary>
    /// True when MiniClip is allowed to inject input into the given window's process.
    /// A normal-integrity process cannot drive an elevated one, and Windows fails the
    /// SendInput silently, so this is checked before we promise the user a paste. §28.2.
    /// </summary>
    public static bool CanSendInputTo(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
        {
            return false;
        }

        var targetProcessId = Win32Windows.GetWindowThreadProcessId(hWnd, out var targetPid);
        if (targetProcessId == 0 || targetPid == 0)
        {
            return true;
        }

        // Same process: always fine.
        if (targetPid == (uint)Environment.ProcessId)
        {
            return true;
        }

        var (targetOk, targetLevel) = GetIntegrityLevel(targetPid);
        if (!targetOk)
        {
            // We cannot even query the target. Refuse rather than risk typing into a
            // window we do not understand.
            return false;
        }

        var (selfOk, selfLevel) = GetIntegrityLevel((uint)Environment.ProcessId);
        if (!selfOk)
        {
            return false;
        }

        return selfLevel >= targetLevel;
    }

    private static (bool Ok, int Level) GetIntegrityLevel(uint processId)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = NativeMethods.OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
            if (handle == IntPtr.Zero)
            {
                // Denied. Treat as "higher than us" rather than guessing it is safe.
                return (false, int.MaxValue);
            }

            if (!NativeMethods.OpenProcessToken(handle, 0x0008 /* TOKEN_QUERY */, out var token))
            {
                return (false, int.MaxValue);
            }

            try
            {
                var size = 0;
                NativeMethods.GetTokenInformation(token, 25 /* TokenIntegrityLevel */, IntPtr.Zero, 0, ref size);
                if (size <= 0)
                {
                    return (false, int.MaxValue);
                }

                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!NativeMethods.GetTokenInformation(token, 25, buffer, size, ref size))
                    {
                        return (false, int.MaxValue);
                    }

                    // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; } }
                    var sid = Marshal.ReadIntPtr(buffer);
                    if (sid == IntPtr.Zero)
                    {
                        return (false, int.MaxValue);
                    }

                    // The integrity level is the last sub-authority of the SID.
                    var subAuthorityCount = Marshal.ReadByte(sid, 1);
                    if (subAuthorityCount == 0)
                    {
                        return (false, int.MaxValue);
                    }

                    var lastSubAuthority = Marshal.ReadInt32(sid, 8 + ((subAuthorityCount - 1) * 4));
                    return (true, lastSubAuthority);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException)
        {
            return (false, int.MaxValue);
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }
}
