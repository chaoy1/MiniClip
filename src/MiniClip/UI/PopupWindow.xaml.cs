using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using MiniClip.History;

namespace MiniClip.UI;

/// <summary>
/// The candidate popup: the whole visible surface of MiniClip.
/// </summary>
/// <remarks>
/// <para>Two properties define this window, and both are load-bearing:</para>
/// <list type="number">
/// <item>It never takes keyboard focus. The user's caret stays in the application they
/// were typing in, which is the reason the product exists. <c>WS_EX_NOACTIVATE</c> plus
/// <c>SetWindowPos(SWP_NOACTIVATE)</c> is how; showing the window any other way breaks it. §8.</item>
/// <item>While it is open, MiniClip is in <em>Clipboard Mode</em> and temporarily owns
/// ↑ ↓ Enter Esc through a low-level keyboard hook. Closing it — for any reason — must
/// restore normal typing immediately. §9.</item>
/// </list>
/// <para>The window is a dumb view: it owns selection and rendering, and raises intent.
/// Deciding what a paste does belongs to the controller.</para>
/// </remarks>
public partial class PopupWindow : Window
{
    /// <summary>Rows visible at once in the approved compact popup.</summary>
    public const int MaxVisibleRows = 5;

    /// <summary>Height of one compact candidate row.</summary>
    public const double RowHeight = 26;

    /// <summary>Chrome above and below the list. Part of the total height budget.</summary>
    private const double ListPadding = 5;

    private readonly ObservableCollection<ClipRow> _rows = [];
    private readonly bool _animationsEnabled;
    private readonly DispatcherTimer _scrollIndicatorTimer;
    private DispatcherTimer? _closeGuard;
    private ResourceDictionary? _themeResources;
    private IntPtr _handle = IntPtr.Zero;
    private int _anchorX;
    private int _anchorY;
    private bool _isClosing;
    private bool _isOpen;
    private bool _scrollIndicatorReady;
    private long _closeGeneration;

    public PopupWindow(PopupTheme theme = PopupTheme.Dark)
    {
        InitializeComponent();
        SetTheme(theme);

        DataContext = this;

        // Respect the system setting rather than animating anyway: a popup that fades
        // when the user has asked Windows not to animate is a bug, not a flourish.
        _animationsEnabled = SystemParameters.ClientAreaAnimation;

        _scrollIndicatorTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(700),
        };
        _scrollIndicatorTimer.Tick += (_, _) => HideScrollIndicator();
        ListScroller.ScrollChanged += OnListScrollChanged;
        ListScroller.PreviewMouseWheel += (_, _) => _scrollIndicatorReady = true;
        ListScroller.PreviewTouchDown += (_, _) => _scrollIndicatorReady = true;

        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += OnDeactivated;
        IsVisibleChanged += OnIsVisibleChanged;
        SourceInitialized += (_, _) => _handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
    }

    /// <summary>The rows currently on screen, oldest first and newest at the bottom.</summary>
    public ObservableCollection<ClipRow> Rows => _rows;

    /// <summary>Index of the highlighted row, or -1 when the list is empty.</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>The highlighted entry's text, or null when there is nothing to paste.</summary>
    public string? SelectedText =>
        SelectedIndex >= 0 && SelectedIndex < _rows.Count ? _rows[SelectedIndex].Text : null;

    /// <summary>True while the popup is on screen.</summary>
    public bool IsPopupOpen => _isOpen;

    /// <summary>True when Windows reports the popup's window as visible. Diagnostics only.</summary>
    public bool IsWindowShown =>
        _handle != IntPtr.Zero && Interop.Win32Windows.IsWindowVisible(_handle);

    /// <summary>The popup's live window handle, for diagnostics.</summary>
    public IntPtr WindowHandle => _handle;

    public PopupTheme Theme { get; private set; }

    public void SetTheme(PopupTheme theme)
    {
        if (_themeResources is not null)
        {
            Resources.MergedDictionaries.Remove(_themeResources);
        }

        _themeResources = PopupThemePalette.Create(theme);
        Resources.MergedDictionaries.Add(_themeResources);
        Theme = theme;
    }

    /// <summary>Raised when the user confirms with Enter and there is something to paste.</summary>
    public event EventHandler<string>? PasteRequested;

    /// <summary>Raised when the user dismisses with Esc, or the popup closes for any other reason.</summary>
    public event EventHandler<PopupCloseReason>? Closed1;

    /// <summary>Raised whenever the selection moves, with the new index.</summary>
    public event EventHandler<int>? SelectionChanged;

    /// <summary>
    /// Fills the list and shows the window beside the captured text caret without activating it.
    /// <paramref name="anchorX"/>/<paramref name="anchorY"/> are physical screen pixels.
    /// </summary>
    public void ShowAt(IReadOnlyList<HistoryEntry> entries, int anchorX, int anchorY, int? preferredIndex = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // A second hotkey press can reopen the same window before its previous fade has
        // finished. Invalidate both the animation callback and its safety timer first.
        _closeGeneration++;
        _closeGuard?.Stop();
        _closeGuard = null;
        BeginAnimation(OpacityProperty, null);

        _anchorX = anchorX;
        _anchorY = anchorY;
        _scrollIndicatorReady = false;
        Rebuild(entries, preferredIndex);
        _isClosing = false;

        // Establish the handle before styling and positioning it.
        if (!IsVisible)
        {
            Show();
        }

        _handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (_handle != IntPtr.Zero)
        {
            Interop.Win32WindowStyles.ApplyPopupStyles(_handle);
        }

        UpdateLayout();
        if (SelectedIndex == _rows.Count - 1)
        {
            ListScroller.ScrollToBottom();
        }
        EnsureSelectionVisible();
        HideScrollIndicator();
        var size = GetPixelSize(_handle);
        var origin = Interop.Win32WindowStyles.PositionWithinWorkArea(anchorX, anchorY, size.Width, size.Height);

        if (_handle != IntPtr.Zero)
        {
            // SWP_SHOWWINDOW with SWP_NOACTIVATE: the one call that makes the panel
            // appear without the keyboard following it.
            Interop.Win32WindowStyles.ShowAtWithoutActivating(_handle, origin.X, origin.Y);
        }

        _isOpen = true;
        PlayAppear();
    }

    /// <summary>Moves the already-open popup so it still sits beside the anchor point.</summary>
    public void MoveTo(int anchorX, int anchorY)
    {
        _anchorX = anchorX;
        _anchorY = anchorY;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var size = GetPixelSize(handle);
        var origin = Interop.Win32WindowStyles.PositionWithinWorkArea(anchorX, anchorY, size.Width, size.Height);
        Interop.Win32WindowStyles.MoveWithoutActivating(handle, origin.X, origin.Y);
    }

    /// <summary>Closes the popup and reports why. Safe to call when it is already closed.</summary>
    public void Close1(PopupCloseReason reason)
    {
        Diagnostics.DiagnosticsLog.Write("popup", $"close requested reason={reason} open={_isOpen} closing={_isClosing}");

        if (!_isOpen || _isClosing)
        {
            return;
        }

        _isClosing = true;
        _isOpen = false;
        var closeGeneration = ++_closeGeneration;

        if (!_animationsEnabled)
        {
            FinishClose(reason);
            return;
        }

        // Two independent guarantees that the popup actually goes away, because relying on
        // the animation alone is a real bug: an animation's Completed event only fires once
        // the clock has advanced and the element has been composed, so a popup that closes
        // before it has ever rendered — the self-test's exact situation, and reachable in
        // practice when the hotkey is double-tapped — would set _isClosing, never animate,
        // never complete, and stay on screen forever. The timer is the backstop.
        var closeGuard = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(140),
        };

        _closeGuard = closeGuard;
        closeGuard.Tick += (_, _) =>
        {
            closeGuard.Stop();
            if (_isClosing && closeGeneration == _closeGeneration)
                FinishClose(reason);
        };

        var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(70)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        fade.Completed += (_, _) =>
        {
            closeGuard.Stop();
            if (_isClosing && closeGeneration == _closeGeneration)
                FinishClose(reason);
        };

        closeGuard.Start();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>The item container for a row index, for diagnostics.</summary>
    public ContentPresenter? ItemContainerFor(int index) =>
        index >= 0 && index < _rows.Count
            ? RowList.ItemContainerGenerator.ContainerFromIndex(index) as ContentPresenter
            : null;

    /// <summary>Replaces the list contents while the popup is open (history changed underneath it).</summary>
    public void Refresh(IReadOnlyList<HistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var previous = SelectedText;
        Rebuild(entries, preferredIndex: null, preferText: previous);

        if (!_isOpen)
        {
            return;
        }

        UpdateLayout();
        EnsureSelectionVisible();
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // The window height follows the row count. Re-anchor beside the original caret.
        var size = GetPixelSize(handle);
        var origin = Interop.Win32WindowStyles.PositionWithinWorkArea(_anchorX, _anchorY, size.Width, size.Height);
        Interop.Win32WindowStyles.MoveWithoutActivating(handle, origin.X, origin.Y);
    }

    /// <summary>
    /// Handles ↑ ↓ Enter Esc itself and marks the event handled so WPF's normal
    /// navigation cannot act on it. This is the fallback path; the low-level hook is what
    /// catches the keys when the popup is genuinely non-activated, and the two never
    /// double-handle because the hook consumes its keys before they reach any window. §9.2.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                MoveSelection(-1);
                break;

            case Key.Down:
                MoveSelection(1);
                break;

            case Key.Enter:
                Confirm();
                break;

            case Key.Escape:
                Close1(PopupCloseReason.Escape);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Called by the controller when the low-level hook catches a navigation key.</summary>
    public bool HandleNavigationKey(uint virtualKey)
    {
        switch (virtualKey)
        {
            case Interop.NativeConstants.VK_UP:
                MoveSelection(-1);
                return true;

            case Interop.NativeConstants.VK_DOWN:
                MoveSelection(1);
                return true;

            case Interop.NativeConstants.VK_RETURN:
                Confirm();
                return true;

            case Interop.NativeConstants.VK_ESCAPE:
                Close1(PopupCloseReason.Escape);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Moves the highlight. Clamps rather than wraps: wrapping from the newest clip to the
    /// oldest on a single stray keypress is disorienting.
    /// </summary>
    public void MoveSelection(int delta)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var next = Math.Clamp(SelectedIndex + delta, 0, _rows.Count - 1);
        if (next == SelectedIndex)
        {
            return;
        }

        SetSelection(next);
    }

    public void SetSelection(int index)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _rows.Count - 1);
        _scrollIndicatorReady = true;

        if (SelectedIndex >= 0 && SelectedIndex < _rows.Count)
        {
            _rows[SelectedIndex].IsSelected = false;
        }

        SelectedIndex = index;
        _rows[index].IsSelected = true;

        EnsureSelectionVisible();
        SelectionChanged?.Invoke(this, index);
    }

    private void Confirm()
    {
        var text = SelectedText;
        if (string.IsNullOrEmpty(text))
        {
            // §9.2: with an empty history Enter must not pretend to paste.
            return;
        }

        PasteRequested?.Invoke(this, text);
    }

    private void Rebuild(IReadOnlyList<HistoryEntry> entries, int? preferredIndex, string? preferText = null)
    {
        _rows.Clear();

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            _rows.Add(new ClipRow(entries[i], i));
        }

        var isEmpty = _rows.Count == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

        var target = Math.Max(0, _rows.Count - 1);
        if (preferText is not null)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (string.Equals(_rows[i].Text, preferText, StringComparison.Ordinal))
                {
                    target = i;
                    break;
                }
            }
        }
        else if (preferredIndex is { } explicitIndex)
        {
            target = Math.Clamp(_rows.Count - 1 - explicitIndex, 0, Math.Max(0, _rows.Count - 1));
        }

        SelectedIndex = isEmpty ? -1 : target;

        if (!isEmpty)
        {
            _rows[target].IsSelected = true;
        }

        UpdateWindowHeight();
    }

    private void UpdateWindowHeight()
    {
        if (_rows.Count == 0)
        {
            Height = 64;
            return;
        }

        var visibleRows = Math.Min(_rows.Count, MaxVisibleRows);
        var listHeight = (visibleRows * RowHeight) + (ListPadding * 2);
        Height = listHeight + 2; // shell border top and bottom
    }

    /// <summary>
    /// Keeps keyboard selection visible when the history list scrolls.
    /// </summary>
    private void EnsureSelectionVisible()
    {
        if (SelectedIndex < 0 || SelectedIndex >= _rows.Count)
        {
            return;
        }

        // Keep the highlighted row visible; with a long history the selection would otherwise
        // walk off the bottom of the panel while still being the thing Enter pastes.
        if (RowList.ItemContainerGenerator.ContainerFromIndex(SelectedIndex) is FrameworkElement container)
        {
            container.BringIntoView();
        }
    }

    private ScrollBar? VerticalScrollBar =>
        ListScroller.Template?.FindName("PART_VerticalScrollBar", ListScroller) as ScrollBar;

    private void OnListScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_isOpen || !_scrollIndicatorReady || Math.Abs(e.VerticalChange) < 0.01)
        {
            return;
        }

        if (VerticalScrollBar is { } bar)
        {
            bar.Opacity = 1;
            _scrollIndicatorTimer.Stop();
            _scrollIndicatorTimer.Start();
        }
    }

    private void HideScrollIndicator()
    {
        _scrollIndicatorTimer.Stop();
        if (VerticalScrollBar is { } bar)
        {
            bar.Opacity = 0;
        }
    }

    private void PlayAppear()
    {
        if (!_animationsEnabled)
        {
            Opacity = 1;
            return;
        }

        // One orchestrated moment: 90 ms of opacity plus a 4 DIP settle. Nothing else on
        // the panel animates, which is what keeps it feeling like an instrument rather
        // than a web page.
        Opacity = 0;

        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(90)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        BeginAnimation(OpacityProperty, fade);

        var shift = new DoubleAnimation(4, 0, new Duration(TimeSpan.FromMilliseconds(90)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        ShellTransform.BeginAnimation(TranslateTransform.YProperty, shift);
    }

    private void FinishClose(PopupCloseReason reason)
    {
        if (!_isClosing)
            return;

        _closeGuard?.Stop();
        _closeGuard = null;
        HideScrollIndicator();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        ShellTransform.BeginAnimation(TranslateTransform.YProperty, null);
        ShellTransform.Y = 0;

        var handle = _handle;
        if (handle != IntPtr.Zero)
        {
            Interop.Win32WindowStyles.HideWindow(handle);
        }

        Hide();

        Diagnostics.DiagnosticsLog.Write(
            "popup",
            $"finishClose reason={reason} hwnd=0x{handle.ToInt64():X} win32Visible={Interop.Win32Windows.IsWindowVisible(handle)} wpfVisible={IsVisible}");

        _isClosing = false;

        Closed1?.Invoke(this, reason);
    }

    /// <summary>
    /// A safety net rather than a feature: if Windows ever does activate the popup, the
    /// user's keyboard is in the wrong place and the mode must end immediately.
    /// </summary>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_isOpen)
        {
            Close1(PopupCloseReason.FocusLost);
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && _isOpen)
        {
            // Hidden by something other than our own close path.
            _isOpen = false;
            Closed1?.Invoke(this, PopupCloseReason.FocusLost);
        }
    }

    private (int Width, int Height) GetPixelSize(IntPtr handle)
    {
        var scale = handle != IntPtr.Zero ? Interop.Win32Windows.GetDpiScaleForWindow(handle) : 1.0;
        return ((int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale));
    }

}

/// <summary>Why the popup went away. The controller uses this to decide what to release.</summary>
public enum PopupCloseReason
{
    /// <summary>The user pressed Esc, or pressed the hotkey again.</summary>
    Escape,

    /// <summary>A paste was confirmed; Clipboard Mode ends after the keystroke is sent.</summary>
    Pasted,

    /// <summary>The popup lost the guarantee that it is not stealing focus. Always tear down.</summary>
    FocusLost,

    /// <summary>The application is shutting down.</summary>
    Shutdown,
}
