using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MyTodo;

/// <summary>统一风格的确认/提示弹窗。</summary>
public partial class ConfirmDialog : Window
{
    private bool _confirmed;

    private ConfirmDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => CancelBtn.Focus();
    }

    /// <summary>确认弹窗，返回是否点击了确认。</summary>
    public static bool Show(Window? owner, string title, string message,
        string confirmText = "确认", bool danger = false)
    {
        var dlg = new ConfirmDialog
        {
            Owner = owner,
        };
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.ConfirmBtn.Content = confirmText;
        dlg.CancelBtn.Visibility = Visibility.Visible;
        dlg.ConfirmBtn.Style = (Style)dlg.FindResource(danger ? "DangerButton" : "AccentButton");
        dlg.ShowDialog();
        return dlg._confirmed;
    }

    /// <summary>信息提示弹窗（只有一个“知道了”按钮）。</summary>
    public static void ShowInfo(Window? owner, string title, string message)
    {
        var dlg = new ConfirmDialog
        {
            Owner = owner,
        };
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.ConfirmBtn.Content = "知道了";
        dlg.CancelBtn.Visibility = Visibility.Collapsed;
        dlg.ConfirmBtn.Style = (Style)dlg.FindResource("AccentButton");
        dlg.ShowDialog();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc 永远是安全的取消
        if (e.Key == Key.Escape) Close();
    }
}
