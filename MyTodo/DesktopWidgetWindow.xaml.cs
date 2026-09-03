using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace MyTodo;

/// <summary>
/// 固定在桌面的小组件：不依赖 SetParent 桌面子层，
/// 而是作为无边框顶层窗口稳定贴在桌面层（普通窗口之下）。
/// </summary>
public partial class DesktopWidgetWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_BOTTOM = new(1);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int SnapThresholdPixels = 18;

    private IntPtr _layerHwnd;
    private IntPtr _hookedHwnd;
    private HwndSource? _hwndSource;
    private readonly System.Windows.Threading.DispatcherTimer _positionSaveTimer =
        new() { Interval = TimeSpan.FromMilliseconds(400) };

    public DesktopWidgetWindow()
    {
        InitializeComponent();
        Opacity = Math.Clamp(Session.Data.Settings.DesktopWidgetOpacity, 0.25, 1.0);
        ApplyCompactMode(Session.Data.Settings.DesktopWidgetCompact);
        PositionOnScreen();
        LocationChanged += (_, _) => QueueSavePosition();
        _positionSaveTimer.Tick += (_, _) => SavePositionNow();
        SourceInitialized += (_, _) => MakeDesktopLayer();
        // WPF 在显示/隐藏过程中可能重建 HWND；这里确保新 HWND 仍旧贴桌面层。
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) MakeDesktopLayer(); };
        Deactivated += (_, _) =>
        {
            WidgetHost.CommitPendingEdit();
            ApplyDesktopLayer();
        };
        QuickTab.IsChecked = true;
        Session.LogEvent("desktop widget created");
    }

    private void PositionOnScreen()
    {
        var s = Session.Data.Settings;
        if (s.DesktopWidgetLeft is double left &&
            s.DesktopWidgetTop is double top &&
            double.IsFinite(left) && double.IsFinite(top))
        {
            Left = left;
            Top = top;
            return;
        }

        // 未记录位置时才使用默认右侧摆放；分辨率变化暂不做自动修正。
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 24;
        Top = Math.Max(area.Top, (area.Bottom - Height) / 2);
    }

    private void QueueSavePosition()
    {
        if (!IsLoaded || !IsVisible) return;
        if (!double.IsFinite(Left) || !double.IsFinite(Top)) return;

        // 拖动过程中只重新计时，停止后统一写盘。
        _positionSaveTimer.Stop();
        _positionSaveTimer.Start();
    }

    private void SavePositionNow()
    {
        if (!IsLoaded || !double.IsFinite(Left) || !double.IsFinite(Top)) return;

        var s = Session.Data.Settings;
        if (s.DesktopWidgetLeft == Left && s.DesktopWidgetTop == Top) return;

        s.DesktopWidgetLeft = Left;
        s.DesktopWidgetTop = Top;
        Session.Save();
        Session.LogEvent("widget position -> " +
            $"{Left:F0},{Top:F0}");
    }

    public void ApplyCompactMode(bool compact)
    {
        Width = compact ? 250 : 340;
        Height = compact ? 330 : 500;
        RootBorder.CornerRadius = new CornerRadius(compact ? 9 : 12);
        RootGrid.Margin = compact ? new Thickness(6) : new Thickness(10);
        RootGrid.RowDefinitions[0].Height = new GridLength(compact ? 26 : 34);
        QuickTab.FontSize = compact ? 12 : 13;
        DatedTab.FontSize = compact ? 12 : 13;
        WidgetHost.IsCompact = compact;

        // 尺寸变化后重新检查一次贴边；不吸附时位置保持不变。
        if (IsVisible && _layerHwnd != IntPtr.Zero)
            SnapToNearbyWorkArea(_layerHwnd);
    }

    // ================= 桌面钉扎 =================

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    private void MakeDesktopLayer()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (_layerHwnd == hwnd)
            {
                ApplyDesktopLayer();
                return;
            }
            _layerHwnd = hwnd;

            int oldExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, oldExStyle | WS_EX_TOOLWINDOW);
            HookWindowMessages(hwnd);
            ApplyDesktopLayer();
            Session.LogEvent("widget desktop layer ready, hwnd=0x" +
                hwnd.ToInt64().ToString("X"));
        }
        catch (Exception ex)
        {
            Session.LogEvent("widget desktop layer exception: " + ex.Message);
        }
    }

    private void HookWindowMessages(IntPtr hwnd)
    {
        if (_hookedHwnd == hwnd) return;

        _hwndSource = HwndSource.FromHwnd(hwnd) as HwndSource;
        if (_hwndSource is null) return;

        _hwndSource.AddHook(WndProc);
        _hookedHwnd = hwnd;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCLBUTTONDOWN)
        {
            // 标题栏是非客户区，WPF 收不到鼠标事件；按标题栏同样视为失焦。
            WidgetHost.CommitPendingEdit();
            return IntPtr.Zero;
        }
        if (msg == WM_EXITSIZEMOVE)
        {
            // 拖动过程保持系统原生行为；松手后一次性贴边，
            // 避免 WM_MOVING 反复改矩形造成“只能贴边移动”的磁性锁。
            SnapToNearbyWorkArea(hwnd);
        }
        return IntPtr.Zero;
    }

    private void SnapToNearbyWorkArea(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetWindowRect(hwnd, out var rect) ||
            !GetMonitorInfo(monitor, ref info)) return;

        var original = rect;
        var work = info.rcWork;

        // 水平吸附
        if (Math.Abs(rect.Left - work.Left) <= SnapThresholdPixels)
        {
            int dx = work.Left - rect.Left;
            rect.Left += dx;
            rect.Right += dx;
        }
        else if (Math.Abs(rect.Right - work.Right) <= SnapThresholdPixels)
        {
            int dx = work.Right - rect.Right;
            rect.Left += dx;
            rect.Right += dx;
        }

        // 垂直吸附；底部贴工作区底边，通常避开任务栏。
        if (Math.Abs(rect.Top - work.Top) <= SnapThresholdPixels)
        {
            int dy = work.Top - rect.Top;
            rect.Top += dy;
            rect.Bottom += dy;
        }
        else if (Math.Abs(rect.Bottom - work.Bottom) <= SnapThresholdPixels)
        {
            int dy = work.Bottom - rect.Bottom;
            rect.Top += dy;
            rect.Bottom += dy;
        }

        if (rect.Left == original.Left && rect.Top == original.Top) return;

        SetWindowPos(hwnd, HWND_BOTTOM, rect.Left, rect.Top, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void ApplyDesktopLayer()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
        catch (Exception ex)
        {
            Session.LogEvent("widget desktop layer failed: " + ex.Message);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _positionSaveTimer.Stop();
        SavePositionNow();
        base.OnClosing(e);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);

    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private struct MonitorInfo
    {
        public int cbSize;
        public Rect32 rcMonitor;
        public Rect32 rcWork;
        public uint dwFlags;
    }

    // ================= 模式页签 =================

    // 点击编辑框以外的任何位置（含面板空白处）都提交编辑，
    // 与主面板的失焦保存行为保持一致。
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => WidgetHost.CommitPendingEdit(e.OriginalSource as DependencyObject);

    private void QuickTab_Checked(object sender, RoutedEventArgs e)
    {
        if (WidgetHost is not null) WidgetHost.SetMode("quick");
    }

    private void DatedTab_Checked(object sender, RoutedEventArgs e)
    {
        if (WidgetHost is not null) WidgetHost.SetMode("dated");
    }
}
