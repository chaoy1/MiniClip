using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MiniClip.UI;

/// <summary>A short-lived, non-activating monochrome status notice.</summary>
public partial class NoticeWindow : Window
{
    private readonly DispatcherTimer _dismissTimer;

    public NoticeWindow(string title, string message, PopupTheme theme, Action? onClick = null)
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(ChromeThemePalette.Create(theme));
        TitleText.Text = title;
        BodyText.Text = message;

        _dismissTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _dismissTimer.Tick += (_, _) => Close();
        Closed += (_, _) => _dismissTimer.Stop();
        MouseLeftButtonUp += (_, _) =>
        {
            Close();
            onClick?.Invoke();
        };
    }

    public void ShowAtScreenCorner()
    {
        Left = -10000;
        Top = -10000;
        Show();
        UpdateLayout();

        var handle = new WindowInteropHelper(this).Handle;
        Place(handle);
        // WPF may update a per-monitor window's DPI after the first move.
        UpdateLayout();
        Place(handle);
        _dismissTimer.Start();
    }

    private void Place(IntPtr handle)
    {
        var scale = Interop.Win32Windows.GetDpiScaleForWindow(handle);
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        if (!Interop.Win32Screen.TryGetWorkArea(0, 0, out var work))
        {
            return;
        }

        var x = Math.Max(work.Left, work.Right - width - 12);
        var y = Math.Max(work.Top, work.Bottom - height - 12);
        Interop.NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
            Interop.NativeConstants.SWP_NOSIZE | Interop.NativeConstants.SWP_NOZORDER
            | Interop.NativeConstants.SWP_NOACTIVATE | Interop.NativeConstants.SWP_NOOWNERZORDER);
    }
}
