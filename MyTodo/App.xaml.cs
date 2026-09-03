using System.IO;
using Application = System.Windows.Application;
using System.Windows;

namespace MyTodo;

/// <summary>Interaction logic for App.xaml</summary>
public partial class App : Application
{
    private static DesktopWidgetWindow? _widget;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log(args.ExceptionObject as Exception ?? new Exception("unknown"));

        var win = new MainWindow();
        win.Show();
        // 恢复上次的小组件开关状态（通过托盘控制）
        if (Session.Data.Settings.ShowDesktopWidget)
            ShowDesktopWidget();
    }

    /// <summary>托盘开关入口。</summary>
    public static void SetDesktopWidget(bool enable)
    {
        if (Session.Data.Settings.ShowDesktopWidget == enable) return;
        Session.Data.Settings.ShowDesktopWidget = enable;
        Session.Save();
        Session.LogEvent("desktop widget -> " + enable);

        if (enable) ShowDesktopWidget();
        else HideDesktopWidget();
    }

    private static void ShowDesktopWidget()
    {
        if (_widget is null)
            _widget = new DesktopWidgetWindow();
        _widget.Show();
        ApplyDesktopWidgetOpacity();
    }

    /// <summary>设置页滑动条修改后实时同步到已显示的桌面小组件。</summary>
    public static void ApplyDesktopWidgetOpacity()
    {
        if (_widget is not null)
        {
            _widget.Opacity = Math.Clamp(Session.Data.Settings.DesktopWidgetOpacity, 0.25, 1.0);
        }
    }

    /// <summary>托盘“紧凑模式”入口；独立于桌面小组件显示开关。</summary>
    public static void SetCompactMode(bool enable)
    {
        if (Session.Data.Settings.DesktopWidgetCompact == enable) return;

        Session.Data.Settings.DesktopWidgetCompact = enable;
        Session.Save();
        Session.LogEvent("desktop widget compact -> " + enable);
        _widget?.ApplyCompactMode(enable);
    }

    private static void HideDesktopWidget()
    {
        _widget?.Close();
        _widget = null;
    }

    private static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataStore.DataDir);
            File.AppendAllText(DataStore.LogErrorPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {ex}\r\n\r\n");
        }
        catch { }
    }
}
