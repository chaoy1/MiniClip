using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MiniClip.Input;

namespace MiniClip.UI;

/// <summary>
/// MiniClip's single settings surface for hotkey, startup and local history information.
/// </summary>
/// <remarks>
/// <para>The dialog records a combination by having the user press it, and reports the
/// outcome in place. The important behaviour is what happens on a refusal: Windows owns
/// shortcut registration, so a combination may be valid and still unavailable because
/// another program holds it. That is not an error the user caused and not one they can
/// guess at, so it is stated in the field's own line with the reason, and the save button
/// is disabled rather than allowed to fail silently. §15, §29 stage 2.</para>
/// <para>MiniClip never registers a hotkey that would swallow plain typing, so
/// <see cref="HotkeyGesture.IsValid"/> is checked here as well as at registration.</para>
/// </remarks>
public partial class HotkeySettingsWindow : Window
{
    private readonly Func<HotkeyGesture, Task<HotkeyRegistrationResult>> _register;
    private readonly HotkeyGesture _original;
    private readonly bool _originalStartWithWindows;
    private readonly Action? _openHistoryFolder;

    private HotkeyGesture _pending;
    private bool _isRecording;
    private bool _canSave;

    /// <summary>Creates the dialog. <paramref name="register"/> attempts a live registration.</summary>
    public HotkeySettingsWindow(
        HotkeyGesture current,
        bool startWithWindows,
        Func<HotkeyGesture, Task<HotkeyRegistrationResult>> register,
        PopupTheme theme = PopupTheme.Dark,
        int historyCount = 0,
        Action? openHistoryFolder = null)
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(ChromeThemePalette.Create(theme));

        _register = register ?? throw new ArgumentNullException(nameof(register));
        _original = current;
        _originalStartWithWindows = startWithWindows;
        _openHistoryFolder = openHistoryFolder;

        _pending = current;
        StartWithWindows.IsChecked = startWithWindows;
        HistoryCountText.Text = $"历史记录 · {Math.Max(0, historyCount)} 条";
        OpenHistoryFolderButton.IsEnabled = openHistoryFolder is not null;

        GestureText.Text = current.IsValid ? current.ToDisplayString() : "未设置";
        SetNote(null, isOkay: true);
        StartWithWindows.Checked += (_, _) => RefreshSaveState();
        StartWithWindows.Unchecked += (_, _) => RefreshSaveState();
        RefreshSaveState();

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => SettingsShell.Focus();
    }

    /// <summary>What the user settled on, or null when they closed without saving.</summary>
    public HotkeyGesture? Result { get; private set; }

    /// <summary>Whether the user's "start with Windows" choice changed.</summary>
    public bool StartWithWindowsResult => StartWithWindows.IsChecked == true;

    public bool StartWithWindowsChanged => StartWithWindowsResult != _originalStartWithWindows;

    // ------------------------------------------------------------------ recording

    private void OnRecordBoxClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BeginRecording();
        e.Handled = true;
    }

    private void OnRecordBoxGotFocus(object sender, KeyboardFocusChangedEventArgs e) => BeginRecording();

    private void BeginRecording()
    {
        if (_isRecording)
        {
            return;
        }

        _isRecording = true;
        RecordBox.BorderBrush = (System.Windows.Media.Brush)FindResource("ChromePrimaryBrush");
        GestureText.Text = "按下新的组合键…";
        StateText.Text = "录制中";
        StateText.Foreground = (System.Windows.Media.Brush)FindResource("ChromePrimaryBrush");
        RecordingCursor.Visibility = Visibility.Visible;
        SetNote("按下组合键即可更换；按 Esc 取消录制。", isOkay: true);
    }

    private void EndRecording()
    {
        _isRecording = false;
        RecordingCursor.Visibility = Visibility.Collapsed;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isRecording)
        {
            if (e.Key == Key.Escape)
            {
                // Not saving is the default outcome of closing, so Esc just closes.
                Close();
                e.Handled = true;
            }

            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            EndRecording();
            ShowGesture(_pending, "已生效");
            SetNote(null, isOkay: true);
            RefreshSaveState();
            return;
        }

        if (IsModifierOnly(e.Key))
        {
            // Show the modifiers as they go down so the field feels like it is listening.
            var live = HotkeyGesture.FromWpf(Keyboard.Modifiers, e.Key);
            GestureText.Text = live.Modifiers == HotkeyModifiers.None
                ? "按下新的组合键…"
                : DescribeModifiers(live.Modifiers) + " + …";
            return;
        }

        // With Alt held, WPF reports Key.System and puts the real key in SystemKey.
        // Reading e.Key here would record nothing at all.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var gesture = HotkeyGesture.FromWpf(Keyboard.Modifiers, key);

        EndRecording();

        if (!gesture.IsValid)
        {
            _pending = _original;
            ShowGesture(_pending, "已生效");
            SetNote(ExplainInvalid(gesture), isOkay: false);
            RefreshSaveState();
            return;
        }

        _pending = gesture;
        ShowGesture(gesture, "保存后生效");
        SetNote("按保存后生效；MiniClip 会立即尝试注册。", isOkay: true);
        RefreshSaveState();
    }

    private static bool IsModifierOnly(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.System;

    private static string DescribeModifiers(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Explains a refusal in terms of what the user can do about it. A message that only
    /// says "invalid" leaves them guessing at an invisible rule.
    /// </summary>
    private static string ExplainInvalid(HotkeyGesture gesture)
    {
        if (gesture.VirtualKey == 0)
        {
            return "这个按键不能用作全局快捷键，请换一个字母、数字或功能键。";
        }

        if (gesture.Modifiers == HotkeyModifiers.None)
        {
            return "全局快捷键至少需要 Ctrl、Alt 或 Shift 中的一个，否则会挡住正常输入。";
        }

        if (gesture.Modifiers == HotkeyModifiers.Shift)
        {
            return "只用 Shift 的组合会影响正常打字，请加上 Ctrl 或 Alt。";
        }

        if (gesture.Modifiers == HotkeyModifiers.Win)
        {
            return "Win 加单键会和系统快捷键冲突，请再加上 Ctrl 或 Alt。";
        }

        return "这个组合被 Windows 保留，请换一个。";
    }

    private void ShowGesture(HotkeyGesture gesture, string state)
    {
        GestureText.Text = gesture.ToDisplayString();
        StateText.Text = state;
        StateText.Foreground = (System.Windows.Media.Brush)FindResource("ChromeMutedBrush");
        RecordBox.BorderBrush = (System.Windows.Media.Brush)FindResource("ChromeBorderBrush");
    }

    private void SetNote(string? text, bool isOkay)
    {
        if (string.IsNullOrEmpty(text))
        {
            NoteBox.Visibility = Visibility.Collapsed;
            return;
        }

        NoteBox.Visibility = Visibility.Visible;
        NoteText.Text = text;
        NoteText.Foreground = (System.Windows.Media.Brush)FindResource(isOkay ? "ChromeMutedBrush" : "ChromeErrorBrush");
        NoteMark.Foreground = NoteText.Foreground;
    }

    private void RefreshSaveState()
    {
        _canSave = (_pending.IsValid && !_pending.Equals(_original)) || StartWithWindowsChanged;
        SaveButton.IsEnabled = _canSave;
    }

    // ------------------------------------------------------------------ commands

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!_canSave)
        {
            return;
        }

        if (_pending.Equals(_original))
        {
            Result = _original;
            DialogResult = true;
            Close();
            return;
        }

        SaveButton.IsEnabled = false;
        StateText.Text = "正在注册…";

        var result = await _register(_pending).ConfigureAwait(true);

        if (!result.IsRegistered)
        {
            // Stay open: the user needs to see why and try another combination.
            GestureText.Text = _pending.ToDisplayString();
            StateText.Text = result.Status == HotkeyRegistrationStatus.AlreadyInUse ? "注册失败" : "不可用";
            StateText.Foreground = (System.Windows.Media.Brush)FindResource("ChromeErrorBrush");
            RecordBox.BorderBrush = (System.Windows.Media.Brush)FindResource("ChromeErrorBrush");
            SetNote(result.Message, isOkay: false);
            _canSave = true;
            SaveButton.IsEnabled = true;
            return;
        }

        Result = _pending;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnOpenHistoryFolderClick(object sender, RoutedEventArgs e) => _openHistoryFolder?.Invoke();

    /// <summary>Plays the dialog's entrance, honouring the system animation setting.</summary>
    public void PlayEntrance()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            return;
        }

        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(120)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
}
