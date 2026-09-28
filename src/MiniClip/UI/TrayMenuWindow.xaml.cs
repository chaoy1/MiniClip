using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MiniClip.Tray;
using Button = System.Windows.Controls.Button;

namespace MiniClip.UI;

/// <summary>A small, keyboard-accessible monochrome menu for the tray icon.</summary>
public partial class TrayMenuWindow : Window
{
    private readonly List<Button> _commands = [];
    private readonly PopupTheme _theme;
    private readonly List<Button> _appearanceCommands = [];
    private Button? _appearanceButton;
    private bool _closing;
    private bool _hasActivated;

    public TrayMenuWindow(IReadOnlyList<TrayMenuEntry> entries, PopupTheme theme)
    {
        InitializeComponent();
        _theme = theme;
        AppearancePopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
        [
            new CustomPopupPlacement(new System.Windows.Point(targetSize.Width - 2, -5), PopupPrimaryAxis.Vertical),
        ];
        Resources.MergedDictionaries.Add(ChromeThemePalette.Create(theme));

        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                RowsPanel.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(12, 5, 12, 5),
                    Background = (Brush)FindResource("ChromeBorderBrush"),
                });
            }
            else if (!entry.IsEnabled)
            {
                RowsPanel.Children.Add(new TextBlock
                {
                    Text = entry.Text,
                    Height = 26,
                    Padding = new Thickness(12, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("ChromeMutedBrush"),
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            else
            {
                var button = new Button
                {
                    Content = entry.Children is null ? entry.Text : BuildSubmenuLabel(entry.Text),
                    Tag = entry.CommandId,
                    Style = (Style)FindResource("MenuButtonStyle"),
                };
                if (entry.Children is { } children)
                {
                    _appearanceButton = button;
                    AppearancePopup.PlacementTarget = button;
                    button.Click += (_, _) => OpenAppearance();
                    button.MouseEnter += (_, _) => OpenAppearance();
                    foreach (var child in children)
                    {
                        var choice = new Button
                        {
                            Content = BuildChoiceLabel(child.Text, child.IsChecked),
                            Tag = child.CommandId,
                            Style = (Style)FindResource("MenuButtonStyle"),
                        };
                        choice.Click += (_, _) => Invoke(child.CommandId);
                        AppearanceRows.Children.Add(choice);
                        _appearanceCommands.Add(choice);
                    }
                }
                else
                {
                    button.Click += (_, _) => Invoke(entry.CommandId);
                    button.MouseEnter += (_, _) => AppearancePopup.IsOpen = false;
                }
                RowsPanel.Children.Add(button);
                _commands.Add(button);
            }
        }

        PreviewKeyDown += OnPreviewKeyDown;
        AppearanceRows.PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) =>
        {
            if (!_hasActivated || _closing)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_closing && IsVisible)
                {
                    Close();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    public event Action<int>? CommandInvoked;

    public int CommandCount => _commands.Count;

    private static Grid BuildSubmenuLabel(string text)
    {
        var panel = new Grid { Width = 160 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(new TextBlock { Text = text });
        var chevron = new TextBlock { Text = "›", FontSize = 16 };
        Grid.SetColumn(chevron, 1);
        panel.Children.Add(chevron);
        return panel;
    }

    private static DockPanel BuildChoiceLabel(string text, bool selected)
    {
        var panel = new DockPanel();
        var mark = new TextBlock { Text = selected ? "✓" : string.Empty, Width = 16 };
        DockPanel.SetDock(mark, Dock.Left);
        panel.Children.Add(mark);
        panel.Children.Add(new TextBlock { Text = text });
        return panel;
    }

    private void OpenAppearance()
    {
        AppearancePopup.IsOpen = true;
        AppearanceShell.Resources.MergedDictionaries.Clear();
        AppearanceShell.Resources.MergedDictionaries.Add(ChromeThemePalette.Create(_theme));
    }

    public void ShowAt(System.Drawing.Point cursor)
    {
        // Create offscreen, then use physical coordinates for mixed-DPI monitors.
        Left = -10000;
        Top = -10000;
        Show();
        UpdateLayout();

        var handle = new WindowInteropHelper(this).Handle;
        var scale = Interop.Win32Windows.GetDpiScaleForWindow(handle);
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var origin = PositionNearTray(cursor, width, height, RightSubmenuReserve(scale));
        Interop.NativeMethods.SetWindowPos(handle, IntPtr.Zero, origin.X, origin.Y, 0, 0,
            Interop.NativeConstants.SWP_NOSIZE | Interop.NativeConstants.SWP_NOZORDER
            | Interop.NativeConstants.SWP_NOACTIVATE | Interop.NativeConstants.SWP_NOOWNERZORDER);

        var movedScale = Interop.Win32Windows.GetDpiScaleForWindow(handle);
        if (Math.Abs(movedScale - scale) > 0.01)
        {
            UpdateLayout();
            width = (int)Math.Ceiling(ActualWidth * movedScale);
            height = (int)Math.Ceiling(ActualHeight * movedScale);
            origin = PositionNearTray(cursor, width, height, RightSubmenuReserve(movedScale));
            Interop.NativeMethods.SetWindowPos(handle, IntPtr.Zero, origin.X, origin.Y, 0, 0,
                Interop.NativeConstants.SWP_NOSIZE | Interop.NativeConstants.SWP_NOZORDER
                | Interop.NativeConstants.SWP_NOACTIVATE | Interop.NativeConstants.SWP_NOOWNERZORDER);
        }

        _hasActivated = Activate();
        _commands.FirstOrDefault()?.Focus();
    }

    private int RightSubmenuReserve(double scale) => _appearanceButton is null
        ? 0
        : (int)Math.Ceiling(AppearanceShell.Width * scale) + 8;

    public static System.Drawing.Point PositionNearTray(System.Drawing.Point cursor, int width, int height,
        int rightSubmenuReserve = 0)
    {
        if (!Interop.Win32Screen.TryGetWorkArea(cursor.X, cursor.Y, out var work))
        {
            return cursor;
        }

        var x = cursor.X - width + 12;
        var y = cursor.Y - height - 6;
        if (y < work.Top)
        {
            y = cursor.Y + 6;
        }

        return new System.Drawing.Point(
            Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width - rightSubmenuReserve)),
            Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height)));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (AppearancePopup.IsOpen)
            {
                AppearancePopup.IsOpen = false;
                _appearanceButton?.Focus();
            }
            else Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left && AppearancePopup.IsOpen)
        {
            AppearancePopup.IsOpen = false;
            _appearanceButton?.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right && _appearanceButton?.IsKeyboardFocused == true)
        {
            OpenAppearance();
            _appearanceCommands.FirstOrDefault()?.Focus();
            e.Handled = true;
            return;
        }

        if (AppearancePopup.IsOpen && _appearanceCommands.Any(button => button.IsKeyboardFocused))
        {
            var currentChoice = _appearanceCommands.FindIndex(button => button.IsKeyboardFocused);
            var nextChoice = e.Key switch
            {
                Key.Down => (currentChoice + 1) % _appearanceCommands.Count,
                Key.Up => (currentChoice + _appearanceCommands.Count - 1) % _appearanceCommands.Count,
                _ => -1,
            };
            if (nextChoice >= 0)
            {
                _appearanceCommands[nextChoice].Focus();
                e.Handled = true;
            }
            return;
        }

        if (_commands.Count == 0)
        {
            return;
        }

        var current = _commands.FindIndex(button => button.IsKeyboardFocused);
        var next = e.Key switch
        {
            Key.Down => (current + 1) % _commands.Count,
            Key.Up => (current + _commands.Count - 1) % _commands.Count,
            Key.Home => 0,
            Key.End => _commands.Count - 1,
            _ => -1,
        };
        if (next >= 0)
        {
            _commands[next].Focus();
            e.Handled = true;
        }
    }

    private void Invoke(int commandId)
    {
        Close();
        CommandInvoked?.Invoke(commandId);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        AppearancePopup.IsOpen = false;
        base.OnClosing(e);
    }
}
