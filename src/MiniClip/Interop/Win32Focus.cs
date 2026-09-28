namespace MiniClip.Interop;

/// <summary>
/// Cross-thread focus inspection.
/// </summary>
/// <remarks>
/// <c>GetFocus()</c> only returns a handle when the calling thread owns the focused
/// window, which is never true for MiniClip — the whole point of the product is that
/// the focused control belongs to somebody else's process. <c>GetGUIThreadInfo</c> is
/// the supported way to ask "which control does <em>that</em> thread consider focused",
/// so it is what the paste guard uses to detect that the user moved somewhere else
/// between opening the popup and pressing Enter. §18 / §25 scenario 6.
/// </remarks>
public static class Win32Focus
{
    private const uint GUI_INMOVESIZE = 0x0002;
    private const uint GUI_INMENUMODE = 0x0004;
    private const uint GUI_POPUPMENUMODE = 0x0010;
    private const uint GUI_SYSTEMMENUMODE = 0x0008;

    public enum AnchorSource { Win32Caret, AccessibleCaret, AutomationCaret, AutomationFocus, FocusedWindow, RootWindow, Unavailable }

    public readonly record struct FocusAnchorResult(System.Drawing.Point Point, AnchorSource Source);

    private readonly record struct AutomationAnchor(System.Drawing.Point? Caret, System.Drawing.Point? Focus,
        AnchorSource CaretSource = AnchorSource.AutomationCaret);

    /// <summary>A snapshot of where the user's keyboard focus actually is.</summary>
    public readonly record struct FocusSnapshot(
        IntPtr FocusedWindow,
        IntPtr ActiveWindow,
        IntPtr RootWindow,
        uint ThreadId,
        uint ProcessId,
        bool InMenuMode)
    {
        public static FocusSnapshot Empty { get; } = new(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 0, false);

        public bool IsUsable => FocusedWindow != IntPtr.Zero || ActiveWindow != IntPtr.Zero;

        /// <summary>
        /// True when this snapshot still describes the same destination as
        /// <paramref name="other"/>. Deliberately tolerant: some applications recreate
        /// their focused child control without the user actually going anywhere, so a
        /// zero handle on either side falls back to comparing root windows.
        /// </summary>
        public bool MatchesTarget(FocusSnapshot other)
        {
            if (RootWindow != other.RootWindow)
            {
                return false;
            }

            if (FocusedWindow == IntPtr.Zero || other.FocusedWindow == IntPtr.Zero)
            {
                return true;
            }

            return FocusedWindow == other.FocusedWindow;
        }
    }

    /// <summary>Focus state of whatever thread currently owns the foreground window.</summary>
    public static FocusSnapshot CaptureForegroundFocus()
    {
        var foreground = Win32Windows.GetForegroundWindow();
        return foreground == IntPtr.Zero ? FocusSnapshot.Empty : CaptureForWindow(foreground);
    }

    /// <summary>Returns the screen position of the focused text caret when Windows exposes it.</summary>
    public static bool TryGetCaretAnchor(FocusSnapshot snapshot, out System.Drawing.Point anchor)
    {
        var resolved = ResolveAnchor(snapshot, out _);
        anchor = resolved.Point;
        return resolved.Source is AnchorSource.Win32Caret or AnchorSource.AccessibleCaret or AnchorSource.AutomationCaret;
    }

    public static FocusAnchorResult ResolveAnchor(FocusSnapshot snapshot) => ResolveAnchor(snapshot, out _);

    public static FocusAnchorResult ResolveAnchor(FocusSnapshot snapshot,
        out Task<FocusAnchorResult?>? deferredAutomation)
    {
        deferredAutomation = null;
        if (snapshot.ThreadId != 0 && !snapshot.InMenuMode)
        {
            var info = new NativeMethods.GUITHREADINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>(),
            };
            if (NativeMethods.GetGUIThreadInfo(snapshot.ThreadId, ref info)
                && info.hwndCaret != IntPtr.Zero
                && NativeMethods.IsWindow(info.hwndCaret))
            {
                var point = new NativeMethods.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
                if (NativeMethods.ClientToScreen(info.hwndCaret, ref point)
                    && IsPlausiblePoint(snapshot.RootWindow, point.X, point.Y))
                {
                    return new FocusAnchorResult(new System.Drawing.Point(point.X, point.Y), AnchorSource.Win32Caret);
                }
            }

            var automation = ProbeAutomationAnchor(snapshot, out var pending);
            if (pending is not null)
            {
                deferredAutomation = ResolveDeferredAutomation(pending);
            }
            if (automation.Caret is { } caret)
            {
                return new FocusAnchorResult(caret, automation.CaretSource);
            }
            if (automation.Focus is { } focusedControl)
            {
                return new FocusAnchorResult(focusedControl, AnchorSource.AutomationFocus);
            }
        }

        var hWnd = snapshot.FocusedWindow != IntPtr.Zero
            ? snapshot.FocusedWindow
            : snapshot.ActiveWindow;
        if (hWnd != IntPtr.Zero && Win32Windows.TryGetWindowRect(hWnd, out var rect)
            && hWnd != snapshot.RootWindow && rect.Width > 0 && rect.Height > 0)
        {
            return new FocusAnchorResult(new System.Drawing.Point(rect.Left + 16,
                rect.Top + Math.Min(32, rect.Height / 2)), AnchorSource.FocusedWindow);
        }

        if (snapshot.RootWindow != IntPtr.Zero
            && Win32Windows.TryGetWindowRect(snapshot.RootWindow, out rect)
            && rect.Width > 0 && rect.Height > 0)
        {
            return new FocusAnchorResult(new System.Drawing.Point(rect.Left + 16, rect.Top + 32),
                AnchorSource.RootWindow);
        }

        return new FocusAnchorResult(System.Drawing.Point.Empty, AnchorSource.Unavailable);
    }

    private static AutomationAnchor ProbeAutomationAnchor(FocusSnapshot snapshot,
        out Task<AutomationAnchor>? pending)
    {
        pending = null;
        // Chromium/Electron may host accessibility elements in a renderer process.
        // Keep the provider call bounded and validate both process identity and geometry.
        if (System.Threading.Interlocked.CompareExchange(ref _automationProbeRunning, 1, 0) == 0)
        {
            var probe = Task.Run(() =>
            {
                try
                {
                    return TryGetAutomationAnchor(snapshot);
                }
                finally
                {
                    System.Threading.Volatile.Write(ref _automationProbeRunning, 0);
                }
            });
            if (probe.Wait(TimeSpan.FromMilliseconds(180)))
            {
                return probe.Result;
            }
            pending = probe;
        }

        return default;
    }

    private static async Task<FocusAnchorResult?> ResolveDeferredAutomation(Task<AutomationAnchor> pending)
    {
        var result = await pending.ConfigureAwait(false);
        if (result.Caret is { } caret)
            return new FocusAnchorResult(caret, result.CaretSource);
        if (result.Focus is { } focus)
            return new FocusAnchorResult(focus, AnchorSource.AutomationFocus);
        return null;
    }

    private static int _automationProbeRunning;

    private static AutomationAnchor TryGetAutomationAnchor(FocusSnapshot snapshot)
    {
        try
        {
            if (TryGetAccessibleCaretAnchor(snapshot, out var accessibleCaret))
            {
                return new AutomationAnchor(accessibleCaret, null, AnchorSource.AccessibleCaret);
            }

            var element = System.Windows.Automation.AutomationElement.FocusedElement;
            if (element is null
                || !Win32Windows.TryGetWindowRect(snapshot.RootWindow, out var rootRect)
                || rootRect.Width <= 0 || rootRect.Height <= 0)
            {
                return default;
            }

            var actualPid = (uint)element.Current.ProcessId;
            var elementRect = element.Current.BoundingRectangle;
            if (!IsSameApplication(snapshot.ProcessId, actualPid)
                || !IsUsefulFocusRect(rootRect, elementRect))
            {
                var rootElement = System.Windows.Automation.AutomationElement.FromHandle(snapshot.RootWindow);
                var focusedChild = rootElement.FindFirst(
                    System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(
                        System.Windows.Automation.AutomationElement.HasKeyboardFocusProperty, true));
                if (focusedChild is not null
                    && IsSameApplication(snapshot.ProcessId, (uint)focusedChild.Current.ProcessId)
                    && IsUsefulFocusRect(rootRect, focusedChild.Current.BoundingRectangle))
                {
                    element = focusedChild;
                    elementRect = element.Current.BoundingRectangle;
                }
            }

            if (!IsSameApplication(snapshot.ProcessId, (uint)element.Current.ProcessId))
            {
                return default;
            }

            System.Drawing.Point? focus = null;
            if (IsUsefulFocusRect(rootRect, elementRect))
            {
                focus = new System.Drawing.Point((int)Math.Round(elementRect.Left) + 16,
                    (int)Math.Round(elementRect.Top) + Math.Min(32, (int)Math.Round(elementRect.Height / 2)));
            }

            if (!element.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out var pattern)
                || pattern is not System.Windows.Automation.TextPattern textPattern)
            {
                return new AutomationAnchor(null, focus);
            }

            var selection = textPattern.GetSelection();
            if (selection.Length == 0)
            {
                return new AutomationAnchor(null, focus);
            }
            foreach (var rect in selection[0].GetBoundingRectangles())
            {
                if (IsPlausibleCaretRect(rootRect, elementRect, rect))
                {
                    return new AutomationAnchor(
                        new System.Drawing.Point((int)Math.Round(rect.Left), (int)Math.Round(rect.Top)), focus);
                }
            }
            return new AutomationAnchor(null, focus);
        }
        catch (Exception)
        {
            // UI Automation providers live outside this process and may fail in
            // provider-specific ways. A failed optional probe must fall back to
            // the focused control instead of breaking the global hotkey.
            return default;
        }
    }

    private static bool TryGetAccessibleCaretAnchor(FocusSnapshot snapshot, out System.Drawing.Point anchor)
    {
        anchor = System.Drawing.Point.Empty;
        if (snapshot.RootWindow == IntPtr.Zero || !Win32Windows.IsWindow(snapshot.RootWindow))
            return false;

        try
        {
            // Some Chromium-based editors paint their own caret, so hwndCaret is
            // null even though MSAA exposes a caret object through oleacc.
            const uint objidCaret = 0xFFFFFFF8;
            var iid = typeof(Accessibility.IAccessible).GUID;
            var caretWindow = snapshot.FocusedWindow != IntPtr.Zero
                ? snapshot.FocusedWindow : snapshot.RootWindow;
            var windows = IsForeground(snapshot.RootWindow)
                ? new[] { caretWindow, IntPtr.Zero }
                : new[] { caretWindow };
            foreach (var window in windows)
            {
                var hr = NativeMethods.AccessibleObjectFromWindow(window, objidCaret,
                    ref iid, out var accessible);
                if (hr != 0 || accessible is null)
                    continue;

                accessible.accLocation(out var x, out var y, out var width, out var height, 0);
                if (width < 0 || height <= 0 || !IsPlausiblePoint(snapshot.RootWindow, x, y))
                    continue;

                anchor = new System.Drawing.Point(x, y);
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsSameApplication(uint expectedPid, uint actualPid)
    {
        if (actualPid == expectedPid && actualPid != 0) return true;
        var expectedName = Win32Windows.GetProcessName(expectedPid);
        return expectedName.Length > 0
            && string.Equals(expectedName, Win32Windows.GetProcessName(actualPid), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlausiblePoint(IntPtr root, int x, int y) =>
        Win32Windows.TryGetWindowRect(root, out var rect)
        && x >= rect.Left + 4 && x <= rect.Right - 4
        && y >= rect.Top + 24 && y <= rect.Bottom - 4;

    private static bool IsPlausibleElementRect(System.Drawing.Rectangle root, System.Windows.Rect element) =>
        !element.IsEmpty && double.IsFinite(element.Left) && double.IsFinite(element.Top)
        && element.Width > 20 && element.Height > 10
        && element.Left >= root.Left - 8 && element.Top >= root.Top - 8
        && element.Right <= root.Right + 8 && element.Bottom <= root.Bottom + 8;

    private static bool IsUsefulFocusRect(System.Drawing.Rectangle root, System.Windows.Rect element) =>
        IsPlausibleElementRect(root, element)
        && !(element.Width >= root.Width * 0.9 && element.Height >= root.Height * 0.8);

    internal static bool IsPlausibleCaretRect(System.Drawing.Rectangle root, System.Windows.Rect element,
        System.Windows.Rect caret) =>
        !caret.IsEmpty && double.IsFinite(caret.Left) && double.IsFinite(caret.Top)
        && caret.Left >= root.Left + 4 && caret.Left <= root.Right - 4
        && caret.Top >= root.Top + 24 && caret.Top <= root.Bottom - 4
        && (element.IsEmpty || (caret.Left >= element.Left - 8 && caret.Left <= element.Right + 8
                                 && caret.Top >= element.Top - 8 && caret.Top <= element.Bottom + 8));

    /// <summary>Finds a non-mouse anchor if the target has no detectable text caret.</summary>
    public static System.Drawing.Point FocusAnchor(FocusSnapshot snapshot)
    {
        return ResolveAnchor(snapshot, out _).Point;
    }

    /// <summary>Focus state of whatever thread owns <paramref name="hWnd"/>.</summary>
    public static FocusSnapshot CaptureForWindow(IntPtr hWnd)
    {
        if (!Win32Windows.IsWindow(hWnd))
        {
            return FocusSnapshot.Empty;
        }

        var threadId = Win32Windows.GetWindowThreadProcessId(hWnd, out var processId);
        if (threadId == 0)
        {
            return FocusSnapshot.Empty;
        }

        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>(),
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info))
        {
            // Fall back to what we can observe without thread info.
            return new FocusSnapshot(IntPtr.Zero, hWnd, RootOf(hWnd), threadId, processId, false);
        }

        var focused = info.hwndFocus != IntPtr.Zero ? info.hwndFocus : info.hwndActive;
        var root = focused != IntPtr.Zero ? RootOf(focused) : RootOf(hWnd);
        if (root == IntPtr.Zero)
        {
            root = RootOf(hWnd);
        }

        var inMenuMode = (info.flags & (GUI_INMENUMODE | GUI_POPUPMENUMODE | GUI_SYSTEMMENUMODE | GUI_INMOVESIZE)) != 0;

        return new FocusSnapshot(
            focused,
            info.hwndActive != IntPtr.Zero ? info.hwndActive : hWnd,
            root,
            threadId,
            processId,
            inMenuMode);
    }

    /// <summary>Top-level (root) window of <paramref name="hWnd"/>.</summary>
    public static IntPtr RootOf(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var root = Win32Windows.GetAncestor(hWnd, NativeConstants.GA_ROOT);
        return root != IntPtr.Zero ? root : hWnd;
    }

    /// <summary>True when the given top-level window is currently in the foreground.</summary>
    public static bool IsForeground(IntPtr rootWindow) =>
        rootWindow != IntPtr.Zero && Win32Windows.GetForegroundWindow() == rootWindow;

    /// <summary>
    /// Describes the destination for the popup's status line. Never includes clipboard
    /// content, and shortens anything that could be a long document title.
    /// </summary>
    public static string DescribeTarget(IntPtr rootWindow)
    {
        if (rootWindow == IntPtr.Zero)
        {
            return "未知窗口";
        }

        var process = Win32Windows.GetProcessName(Win32Windows.GetWindowThreadProcessId(rootWindow, out _));
        var title = Win32Windows.GetWindowTitle(rootWindow);

        if (string.IsNullOrEmpty(process))
        {
            return string.IsNullOrEmpty(title) ? "当前窗口" : Clamp(title);
        }

        if (IsBrowser(process))
        {
            return "浏览器";
        }

        return string.IsNullOrEmpty(title) ? FriendlyProcess(process) : Clamp(title);
    }

    private static bool IsBrowser(string process) =>
        process.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
        process.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
        process.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
        process.Equals("brave", StringComparison.OrdinalIgnoreCase) ||
        process.Equals("vivaldi", StringComparison.OrdinalIgnoreCase);

    private static string FriendlyProcess(string process) => process.ToLowerInvariant() switch
    {
        "notepad" => "记事本",
        "code" => "VS Code",
        "windowsterminal" or "wt" => "Windows 终端",
        "explorer" => "文件资源管理器",
        "cmd" or "conhost" => "命令提示符",
        "powershell" or "pwsh" => "PowerShell",
        "wezterm-gui" => "WezTerm",
        "devenv" => "Visual Studio",
        "winword" => "Word",
        "excel" => "Excel",
        _ => process,
    };

    private static string Clamp(string value)
    {
        const int max = 40;
        return value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
    }
}
