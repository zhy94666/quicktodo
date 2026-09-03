using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace MyTodo;

/// <summary>右下角轻提示：不抢焦点，显示片刻后自动淡出。</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(1800) };

    public ToastWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DisableActivation();
        _closeTimer.Tick += (_, _) => FadeOutAndHide();
    }

    public void ShowMessage(string text)
    {
        ToastText.Text = text;
        Opacity = 0;
        Show();
        PositionNearTray();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140));
        BeginAnimation(OpacityProperty, fadeIn);

        _closeTimer.Stop();
        _closeTimer.Start();
    }

    private void PositionNearTray()
    {
        var wa = WF.SystemInformation.WorkingArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - ActualHeight - 12;
    }

    private void FadeOutAndHide()
    {
        _closeTimer.Stop();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180));
        fadeOut.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>不抢当前应用焦点的关键：WS_EX_NOACTIVATE。</summary>
    private void DisableActivation()
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
