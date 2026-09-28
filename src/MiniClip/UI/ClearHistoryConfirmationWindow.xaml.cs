using System.Windows;

namespace MiniClip.UI;

/// <summary>Asks for an explicit choice before deleting the saved history.</summary>
public partial class ClearHistoryConfirmationWindow : Window
{
    public ClearHistoryConfirmationWindow(int historyCount, PopupTheme theme, bool unreadableHistory = false,
        bool hasStoredHistoryFile = false)
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(ChromeThemePalette.Create(theme));
        TitleText.Text = unreadableHistory
            ? "清空异常历史及临时记录？"
            : historyCount == 0 && hasStoredHistoryFile ? "删除剩余历史文件？"
            : $"清空 {Math.Max(0, historyCount)} 条历史？";
    }

    public bool Confirmed { get; private set; }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }
}
