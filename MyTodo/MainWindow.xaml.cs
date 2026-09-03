using System.ComponentModel;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using WF = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace MyTodo;

public partial class MainWindow : Window
{
    private const int HOTKEY_ID = 0xB007;
    private const int HOTKEY_ID_COLLECT = 0xB008;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_NCLBUTTONDOWN = 0x00A1;

    private const int MOD_ALT = 0x1;
    private const int MOD_CONTROL = 0x2;
    private const int MOD_SHIFT = 0x4;
    private const int MOD_WIN = 0x8;

    private bool _exiting;
    private ToastWindow? _toast;

    private enum RecordingTarget { None, Main, Collect }
    private RecordingTarget _recordingTarget;
    private bool _hooked;
    private bool _hotkeyWarned;
    private bool _suppressAutoHide;
    private bool _suppressOpacityUi;
    private bool _readyForOpacityInput;

    private HwndSource? _hwndSource;
    private WF.NotifyIcon? _tray;
    private WF.ContextMenuStrip? _trayMenu;
    private IntPtr _menuHook;
    private readonly HookProc _menuHookProc;
    private readonly WF.ToolStripMenuItem _statSummaryItem = new("统计加载中…");
    private readonly WF.ToolStripMenuItem _statTrendItem = new("趋势 …");
    private readonly System.Windows.Threading.DispatcherTimer _opacitySaveTimer =
        new() { Interval = TimeSpan.FromMilliseconds(300) };

    public MainWindow()
    {
        InitializeComponent();
        _menuHookProc = MenuMouseHookProc;

        _opacitySaveTimer.Tick += (_, _) => SaveWidgetOpacity();

        Loaded += (_, _) => MainHost.FocusAddBox();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) EnsureWindowHooked();
        };

        Session.LogEvent("app startup");
        SetupTray();
        ApplySettingsToUi();
        _readyForOpacityInput = true;
        ApplyTopmostUi();
        QuickTab.IsChecked = true;
    }

    // ================= 全局快捷键 =================

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>确保消息钩子挂在最终窗口句柄上，并注册全局热键。</summary>
    private void EnsureWindowHooked()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!_hooked || _hwndSource is null || _hwndSource.Handle != handle)
        {
            _hwndSource = (HwndSource)HwndSource.FromHwnd(handle);
            _hwndSource.AddHook(WndProc);
            _hooked = true;
            Session.LogEvent("hooked hwnd=0x" + handle.ToInt64().ToString("X"));
        }

        // 每次显示都重新注册，保证热键始终有效
        bool regOk = RegisterHotkey();
        Session.LogEvent("RegisterHotKey collect enabled=" + Session.Data.Settings.QuickCollectEnabled +
            " mods=" + Session.Data.Settings.QuickCollectModifiers +
            " vk=0x" + Session.Data.Settings.QuickCollectVirtualKey.ToString("X"));
        Session.LogEvent("RegisterHotKey mods=" + Session.Data.Settings.Modifiers +
                 " vk=0x" + Session.Data.Settings.VirtualKey.ToString("X") + " ok=" + regOk);
        if (!regOk && !_hotkeyWarned)
        {
            _hotkeyWarned = true;
            ShowInfo("快捷键注册失败",
                $"快捷键 {Session.Data.Settings.HotkeyText} 被其他程序占用。\n请在设置中更换快捷键。");
        }
    }

    private bool RegisterHotkey()
    {
        if (_hwndSource is null) return false;
        var handle = _hwndSource.Handle;
        UnregisterHotKey(handle, HOTKEY_ID);
        var s = Session.Data.Settings;
        bool ok = RegisterHotKey(handle, HOTKEY_ID, (uint)s.Modifiers, (uint)s.VirtualKey);

        // 快速收集热键（可在设置中关闭）
        UnregisterHotKey(handle, HOTKEY_ID_COLLECT);
        if (s.QuickCollectEnabled)
            RegisterHotKey(handle, HOTKEY_ID_COLLECT,
                (uint)s.QuickCollectModifiers, (uint)s.QuickCollectVirtualKey);
        return ok;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCLBUTTONDOWN)
        {
            // 标题栏是非客户区，WPF 收不到鼠标事件；按标题栏同样视为失焦。
            MainHost.CommitPendingEdit();
        }
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID || id == HOTKEY_ID_COLLECT)
            {
                // 正在本面板输入文字时，只有单键且会产生字符的键（字母/数字/符号）不触发；
                // F1 这类功能键始终允许呼出/隐藏，避免“按了没反应”。
                int mods = ((int)lParam) & 0xFFFF;
                int vk = (((int)lParam) >> 16) & 0xFFFF;
                bool typingHere = IsActive
                    && Keyboard.FocusedElement is System.Windows.Controls.TextBox
                    && mods == 0
                    && IsTextKey(vk);
                Session.LogEvent("WM_HOTKEY id=0x" + id.ToString("X") + " typingHere=" + typingHere);
                if (!typingHere)
                {
                    if (id == HOTKEY_ID)
                        TogglePanel();
                    else
                        CollectFromClipboard();
                }
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>该虚拟键是否会产生文本字符（字母、数字、空格、标点）。</summary>
    private static bool IsTextKey(int vk)
    {
        return vk is >= 0x30 and <= 0x39      // 0-9
            or >= 0x41 and <= 0x5A            // A-Z
            or >= 0xBA and <= 0xC0            // ;=,-./`
            or >= 0xDB and <= 0xDE            // [\]'
            or 0x20;                          // 空格
    }

    private void TogglePanel()
    {
        Session.LogEvent("hotkey toggle, visible=" + IsVisible);
        if (IsVisible)
        {
            MainHost.CommitPendingEdit();
            Hide();
        }
        else ShowPanel();
    }

    private void ShowPanel()
    {
        Session.LogEvent("show panel");
        Show();
        Activate();
        MainHost.FocusAddBox();
    }

    private void HidePanel_Click(object sender, RoutedEventArgs e)
    {
        MainHost.CommitPendingEdit();
        Hide();
    }

    // 面板空白处不可聚焦，点击不会触发编辑框 LostFocus；
    // 在窗口层捕获鼠标按下，只要没点在编辑框内就提交编辑。
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => MainHost.CommitPendingEdit(e.OriginalSource as DependencyObject);

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // 切到其他程序时键盘焦点丢失但逻辑焦点仍在，LostFocus 不触发，
        // 这里兜底提交后再按设置决定是否自动隐藏。
        MainHost.CommitPendingEdit();
        if (_suppressAutoHide) return;
        Session.LogEvent("deactivated, autohide=" + Session.Data.Settings.AutoHideOnFocusLost);
        if (Session.Data.Settings.AutoHideOnFocusLost) Hide();
    }

    // ================= 托盘 =================

    private void SetupTray()
    {
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("显示面板", null, (_, _) => ShowPanel());

        var widgetItem = new WF.ToolStripMenuItem("桌面小组件")
        {
            CheckOnClick = true,
            Checked = Session.Data.Settings.ShowDesktopWidget,
        };
        widgetItem.CheckedChanged += (_, _) =>
            App.SetDesktopWidget(widgetItem.Checked);
        menu.Items.Add(widgetItem);

        var compactItem = new WF.ToolStripMenuItem("紧凑模式")
        {
            CheckOnClick = true,
            Checked = Session.Data.Settings.DesktopWidgetCompact,
        };
        compactItem.CheckedChanged += (_, _) =>
            App.SetCompactMode(compactItem.Checked);
        menu.Items.Add(compactItem);

        // 统计信息直接显示在菜单里（不可点击的信息行）
        _statSummaryItem.Enabled = false;
        _statTrendItem.Enabled = false;
        _statTrendItem.Font = new Font("Segoe UI Symbol", 9f);   // 块字符趋势图
        _statTrendItem.ToolTipText = "近 7 日完成趋势，最后一个字符是今天";
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(_statSummaryItem);
        menu.Items.Add(_statTrendItem);
        menu.ShowItemToolTips = true;

        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("设置", null, (_, _) => { ShowPanel(); OpenSettings(); });
        menu.Items.Add("退出", null, (_, _) => ExitApp());

        // 每次展开菜单时刷新统计，保证数字始终是当前值
        menu.Opening += (_, _) => UpdateTrayStats();
        UpdateTrayStats();

        // 菜单打开期间挂鼠标钩子，实现“点击菜单外任意位置自动关闭”
        _trayMenu = menu;
        menu.Opened += (_, _) => InstallMenuHook();
        menu.Closed += (_, _) => RemoveMenuHook();

        _tray = new WF.NotifyIcon
        {
            Text = "MyTodo",
            Visible = true,
            Icon = MakeIcon(),
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowPanel();
    }

    private Icon MakeIcon()
    {
        var asm = typeof(MainWindow).Assembly;
        using var stream = asm.GetManifestResourceStream("MyTodo.Assets.tray.ico")
            ?? throw new InvalidOperationException("Embedded resource MyTodo.Assets.tray.ico not found.");
        return new Icon(stream);
    }

    private void ExitApp()
    {
        Session.LogEvent("exit from tray");
        _exiting = true;
        _tray?.Dispose();
        RemoveMenuHook();
        _trayMenu = null;
        if (_hwndSource is not null)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID_COLLECT);
        }
        Application.Current.Shutdown();
    }

    // ================= 托盘菜单自动关闭 =================

    // WinForms 上下文菜单的“点击外部自动关闭”依赖 WinForms 自己的消息泵里的
    // 消息过滤器；本应用跑的是 WPF 消息循环，那套机制收不到消息，菜单会一直挂着。
    // 这里在菜单打开期间挂低级鼠标钩子：菜单窗口以外的任何鼠标按下都关闭菜单，
    // 且不吞掉这次点击，让它继续传给目标窗口。

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0205;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_NCRBUTTONDOWN = 0x00A4;
    private const int WM_NCMBUTTONDOWN = 0x00A7;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private void InstallMenuHook()
    {
        if (_menuHook != IntPtr.Zero) return;
        _menuHook = SetWindowsHookEx(WH_MOUSE_LL, _menuHookProc, GetModuleHandle(null), 0);
        Session.LogEvent("tray menu hook installed ok=" + (_menuHook != IntPtr.Zero));
    }

    private void RemoveMenuHook()
    {
        if (_menuHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_menuHook);
        _menuHook = IntPtr.Zero;
    }

    private IntPtr MenuMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0
                && _trayMenu is { Visible: true } menu
                && wParam.ToInt32() is WM_LBUTTONDOWN or WM_RBUTTONDOWN
                    or WM_MBUTTONDOWN or WM_XBUTTONDOWN
                    or WM_NCLBUTTONDOWN or WM_NCRBUTTONDOWN or WM_NCMBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                IntPtr hwndUnder = WindowFromPoint(info.pt);
                IntPtr menuHwnd = menu.IsHandleCreated ? menu.Handle : IntPtr.Zero;
                if (hwndUnder != menuHwnd)
                {
                    // 点击落在菜单外：关闭菜单（不吞点击，让它继续传给目标窗口）
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (_trayMenu is { Visible: true } m) m.Close();
                    });
                }
            }
        }
        catch
        {
            // 钩子回调里绝不能抛异常
        }
        return CallNextHookEx(_menuHook, nCode, wParam, lParam);
    }

    // ================= 托盘统计 =================

    private void UpdateTrayStats()
    {
        var items = Session.Data.Items;
        var today = DateTime.Today;
        var weekStart = today.AddDays(-6);

        int pending = items.Count(t => !t.IsCompleted && !t.IsDeleted);
        int doneToday = items.Count(t => t.IsCompleted && !t.IsDeleted && t.CompletedAt?.Date == today);
        int doneWeek = items.Count(t => t.IsCompleted && !t.IsDeleted && t.CompletedAt?.Date >= weekStart);
        _statSummaryItem.Text = $"待办 {pending} · 今日 {doneToday} · 近7日 {doneWeek}";
        _statTrendItem.Text = "趋势   " + BuildSparkline(items, today);
    }

    /// <summary>近 7 日（6 天前 → 今天）完成数的迷你柱状图，最后一个字符是今天。</summary>
    private static string BuildSparkline(List<TodoItem> items, DateTime today)
    {
        const string levels = "▁▂▃▄▅▆▇█";
        var counts = new int[7];
        for (int i = 0; i < 7; i++)
        {
            var day = today.AddDays(i - 6);
            counts[i] = items.Count(t => t.IsCompleted && !t.IsDeleted && t.CompletedAt?.Date == day);
        }

        int max = Math.Max(counts.Max(), 1);
        var sb = new System.Text.StringBuilder();
        foreach (var c in counts)
        {
            int idx = c == 0 ? 0 : Math.Min(1 + (c - 1) * (levels.Length - 1) / max, levels.Length - 1);
            sb.Append(levels[idx]);
        }
        return sb.ToString();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            Session.LogEvent("close attempt, cancel+hide");
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    // ================= 置顶 =================

    private void Topmost_Click(object sender, RoutedEventArgs e)
    {
        var on = !Session.Data.Settings.MainPanelTopmost;
        Session.Data.Settings.MainPanelTopmost = on;
        Session.Save();
        ApplyTopmostUi();
        Session.LogEvent("main topmost -> " + on);
    }

    private void ApplyTopmostUi()
    {
        bool on = Session.Data.Settings.MainPanelTopmost;
        Topmost = on;
        TopmostBtn.Tag = on ? "On" : null;
        TopmostBtn.Content = on ? "\uE77A" : "\uE718";   // 取消置顶 / 置顶
        TopmostBtn.ToolTip = on ? "取消置顶" : "置于顶层";
    }

    // ================= 模式页签 =================

    private void QuickTab_Checked(object sender, RoutedEventArgs e)
    {
        if (MainHost is not null) MainHost.SetMode("quick");
    }

    private void DatedTab_Checked(object sender, RoutedEventArgs e)
    {
        if (MainHost is not null) MainHost.SetMode("dated");
    }

    // ================= 设置 =================

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        SettingsView.Visibility = Visibility.Visible;
        ApplySettingsToUi();
        RefreshStorageInfo();
        RefreshRecycleBin();
    }

    private void BackToMain_Click(object sender, RoutedEventArgs e)
    {
        SettingsView.Visibility = Visibility.Collapsed;
    }

    private void ApplySettingsToUi()
    {
        var s = Session.Data.Settings;
        HotkeyText.Text = s.HotkeyText;
        CollectHotkeyText.Text = s.QuickCollectHotkeyText;
        CollectEnabledCheck.IsChecked = s.QuickCollectEnabled;
        CollectHotkeyBox.IsEnabled = s.QuickCollectEnabled;
        CollectHotkeyBox.Opacity = s.QuickCollectEnabled ? 1.0 : 0.5;
        AutoHideCheck.IsChecked = s.AutoHideOnFocusLost;
        StartupCheck.IsChecked = s.StartWithWindows;

        _suppressOpacityUi = true;
        OpacitySlider.Value = s.DesktopWidgetOpacity;
        OpacityValueText.Text = $"{Math.Round(s.DesktopWidgetOpacity * 100)}%";
        _suppressOpacityUi = false;
    }

    // ---- 桌面小组件透明度 ----

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_readyForOpacityInput || _suppressOpacityUi || OpacityValueText is null) return;

        var value = Math.Clamp(e.NewValue, 0.25, 1.0);
        Session.Data.Settings.DesktopWidgetOpacity = value;
        OpacityValueText.Text = $"{Math.Round(value * 100)}%";
        App.ApplyDesktopWidgetOpacity();

        // 拖动时实时预览；停止后统一落盘，避免拖一次写几十次文件。
        _opacitySaveTimer.Stop();
        _opacitySaveTimer.Start();
    }

    private void OpacitySlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        SaveWidgetOpacity();
    }

    private void SaveWidgetOpacity()
    {
        _opacitySaveTimer.Stop();
        Session.Save();
        Session.LogEvent("widget opacity -> " +
            Session.Data.Settings.DesktopWidgetOpacity.ToString("P0"));
    }

    // ---- 数据与日志 ----

    private static string GetSizeText(string path)
    {
        try
        {
            var f = new FileInfo(path);
            if (!f.Exists || f.Length == 0) return "0 B";
            if (f.Length < 1024) return $"{f.Length} B";
            if (f.Length < 1024L * 1024) return $"{f.Length / 1024.0:F1} KB";
            return $"{f.Length / 1024.0 / 1024.0:F1} MB";
        }
        catch { return "—"; }
    }

    private void RefreshStorageInfo()
    {
        DataSizeText.Text = GetSizeText(DataStore.DataFilePath);
        TraceSizeText.Text = GetSizeText(DataStore.LogTracePath);
        ErrorSizeText.Text = GetSizeText(DataStore.LogErrorPath);
    }

    private void CleanHistory_Click(object sender, RoutedEventArgs e)
    {
        int n = Session.Data.Items.Count(t => t.IsCompleted && !t.IsDeleted);
        if (n == 0)
        {
            ShowInfo("没有历史待办", "当前没有已完成的历史待办。");
            return;
        }

        bool confirmed = ShowConfirm("清理历史待办",
            $"将把 {n} 条已完成的历史待办移入回收站，\n可在回收站中恢复。", "清理");
        if (!confirmed) return;

        foreach (var t in Session.Data.Items.Where(t => t.IsCompleted && !t.IsDeleted).ToList())
            Session.SoftDelete(t);
        Session.LogEvent("clean history, moved=" + n);
        Session.Save();
        RefreshStorageInfo();
        RefreshRecycleBin();
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = ShowConfirm("清空日志",
            "将清空 trace.log 与 error.log 的全部内容，\n此操作不可恢复。", "清空", danger: true);
        if (!confirmed) return;

        foreach (var f in new[] { DataStore.LogTracePath, DataStore.LogErrorPath })
        {
            try
            {
                if (File.Exists(f)) File.WriteAllText(f, string.Empty);
            }
            catch { }
        }
        Session.LogEvent("logs cleared");
        RefreshStorageInfo();
    }

    // ---- 导入 / 导出 ----

    private void ExportData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出 MyTodo 数据",
            FileName = $"MyTodo-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Filter = "MyTodo 数据备份 (*.json)|*.json|所有文件 (*.*)|*.*",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var backup = new TodoBackupFile
            {
                ExportedAt = DateTime.Now,
                Items = Session.Data.Items,
            };
            File.WriteAllText(dialog.FileName,
                System.Text.Json.JsonSerializer.Serialize(backup, DataStore.BackupJsonOptions));
            Session.LogEvent("data exported, count=" + backup.Items.Count);
            ShowInfo("导出成功",
                $"已导出 {backup.Items.Count} 条待办记录。\n文件位置：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            Session.LogError("data export failed", ex);
            ShowInfo("导出失败", "写入备份文件失败，请检查文件是否被占用后重试。");
        }
    }

    private void ImportData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入 MyTodo 数据",
            Filter = "MyTodo 数据备份 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        TodoBackupFile? backup;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            bool hasItems = document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("Items", out _);
            if (!hasItems)
            {
                ShowInfo("导入失败", "这不是有效的 MyTodo 数据备份文件。");
                return;
            }

            backup = System.Text.Json.JsonSerializer.Deserialize<TodoBackupFile>(
                json, DataStore.BackupJsonOptions);
            if (backup is null)
            {
                ShowInfo("导入失败", "备份文件内容无法解析。");
                return;
            }

            DataStore.NormalizeItems(backup.Items);
        }
        catch (Exception ex)
        {
            Session.LogError("data import parse failed", ex);
            ShowInfo("导入失败", "读取或解析备份文件失败，请确认文件格式。");
            return;
        }

        int count = backup.Items.Count;
        bool confirmed = ShowConfirm("导入数据",
            $"将用备份中的 {count} 条待办记录覆盖当前数据（含回收站）。\n快捷键等设置保留不变，当前数据会自动备份。",
            "导入", danger: true);
        if (!confirmed) return;

        try
        {
            Directory.CreateDirectory(DataStore.DataDir);
            var safetyPath = Path.Combine(DataStore.DataDir,
                $"data-before-import-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(safetyPath,
                System.Text.Json.JsonSerializer.Serialize(Session.Data, DataStore.BackupJsonOptions));

            Session.Data.Items = backup.Items;
            Session.Save();
            Session.LogEvent("data imported, count=" + count +
                " safety=" + safetyPath);

            RefreshStorageInfo();
            RefreshRecycleBin();
            ShowInfo("导入成功",
                $"已导入 {count} 条待办记录。\n导入前数据已备份到：{safetyPath}");
        }
        catch (Exception ex)
        {
            Session.LogError("data import failed", ex);
            ShowInfo("导入失败", "导入过程发生错误，当前数据未变更为导入内容。");
        }
    }

    // ---- 回收站 ----

    private void RefreshRecycleBin()
    {
        var rows = Session.Data.Items
            .Where(t => t.IsDeleted)
            .OrderByDescending(t => t.DeletedAt ?? t.CreatedAt)
            .Select(t => new RecycleRow(t))
            .ToList();

        RecycleList.ItemsSource = rows;
        RecycleInfoText.Text = rows.Count == 0
            ? "回收站是空的"
            : $"回收站有 {rows.Count} 项";
        bool has = rows.Count > 0;
        RecycleListScroll.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        RecycleActions.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RecycleRestore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecycleRow row)
        {
            Session.Restore(row.Item);
            Session.Save();
            RefreshRecycleBin();
            RefreshStorageInfo();
        }
    }

    private void RecycleRestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var rows = Session.Data.Items.Where(t => t.IsDeleted).ToList();
        foreach (var t in rows)
            Session.Restore(t);
        if (rows.Count > 0)
        {
            Session.Save();
            RefreshRecycleBin();
            RefreshStorageInfo();
        }
    }

    private void RecycleClear_Click(object sender, RoutedEventArgs e)
    {
        int n = Session.Data.Items.Count(t => t.IsDeleted);
        if (n == 0) return;

        bool confirmed = ShowConfirm("清空回收站",
            $"将永久删除回收站中的 {n} 项，\n此操作不可恢复。", "清空", danger: true);
        if (!confirmed) return;

        Session.Data.Items.RemoveAll(t => t.IsDeleted);
        Session.Save();
        RefreshRecycleBin();
        RefreshStorageInfo();
    }

    // ---- 快速收集 ----

    private void CollectEnabled_Changed(object sender, RoutedEventArgs e)
    {
        Session.Data.Settings.QuickCollectEnabled = CollectEnabledCheck.IsChecked == true;
        Session.Save();
        RegisterHotkey();
    }

    private void CollectFromClipboard()
    {
        string text;
        try { text = Clipboard.GetText(); }
        catch { ShowToast("剪贴板读取失败"); return; }

        text = text.Trim().ReplaceLineEndings(" ");
        if (text.Length == 0)
        {
            ShowToast("剪贴板里没有文字");
            return;
        }
        if (text.Length > 500) text = text[..500];

        var item = new TodoItem { Title = text, Mode = "quick", Day = null };
        item.Order = Session.Data.Items
            .Where(t => t.Mode == "quick" && !t.IsDeleted && !t.IsCompleted)
            .Select(t => t.Order).DefaultIfEmpty(-1).Max() + 1;
        Session.Data.Items.Add(item);
        Session.LogEvent("quick collect: " + text);
        Session.Save();

        var preview = text.Length > 22 ? text[..22] + "…" : text;
        ShowToast("已收集： " + preview);
    }

    private void ShowToast(string text)
    {
        _toast ??= new ToastWindow();
        _toast.ShowMessage(text);
    }

    // ---- 快捷键录制 ----

    private void HotkeyBox_Click(object sender, RoutedEventArgs e)
        => StartRecording(RecordingTarget.Main);

    private void CollectHotkeyBox_Click(object sender, RoutedEventArgs e)
        => StartRecording(RecordingTarget.Collect);

    private void StartRecording(RecordingTarget target)
    {
        if (_recordingTarget != RecordingTarget.None) return;
        _recordingTarget = target;
        var box = target == RecordingTarget.Main ? HotkeyBox : CollectHotkeyBox;
        var label = target == RecordingTarget.Main ? HotkeyText : CollectHotkeyText;
        label.Text = "按下新的组合键…";
        box.BorderBrush = (System.Windows.Media.Brush)FindResource("Brush.Accent");
        box.Focus();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingTarget == RecordingTarget.None) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 只按了修饰键，等待主键
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            (_recordingTarget == RecordingTarget.Main ? HotkeyText : CollectHotkeyText).Text
                = "按下新的组合键…";
            return;
        }

        if (key == Key.Escape)
        {
            CancelRecording();
            return;
        }

        int mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= MOD_WIN;

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            CancelRecording();
            return;
        }

        var s = Session.Data.Settings;
        if (_recordingTarget == RecordingTarget.Main)
        {
            s.Modifiers = mods;
            s.VirtualKey = vk;
        }
        else
        {
            s.QuickCollectModifiers = mods;
            s.QuickCollectVirtualKey = vk;
        }
        Session.Save();
        bool ok = RegisterHotkey();
        Session.LogEvent("hotkey changed target=" + _recordingTarget +
            " mods=" + mods + " vk=0x" + vk.ToString("X") + " ok=" + ok);
        _recordingTarget = RecordingTarget.None;
        ApplySettingsToUi();
        var stroke = (System.Windows.Media.Brush)FindResource("Brush.Stroke");
        HotkeyBox.BorderBrush = stroke;
        CollectHotkeyBox.BorderBrush = stroke;
        MainHost.FocusAddBox();
    }

    private void CancelRecording()
    {
        _recordingTarget = RecordingTarget.None;
        ApplySettingsToUi();
        var stroke = (System.Windows.Media.Brush)FindResource("Brush.Stroke");
        HotkeyBox.BorderBrush = stroke;
        CollectHotkeyBox.BorderBrush = stroke;
    }

    // ---- 开关 ----

    private void AutoHide_Changed(object sender, RoutedEventArgs e)
    {
        Session.Data.Settings.AutoHideOnFocusLost = AutoHideCheck.IsChecked == true;
        Session.Save();
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        bool enable = StartupCheck.IsChecked == true;
        Session.Data.Settings.StartWithWindows = enable;
        Session.Save();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;
            if (enable)
                key.SetValue("MyTodo", $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue("MyTodo", throwOnMissingValue: false);
        }
        catch
        {
            ShowInfo("开机启动设置失败", "写入系统启动项失败，请检查权限后重试。");
        }
    }

    // ================= 统一弹窗 =================

    private bool ShowConfirm(string title, string message, string confirmText, bool danger = false)
    {
        _suppressAutoHide = true;
        try
        {
            return ConfirmDialog.Show(this, title, message, confirmText, danger);
        }
        finally
        {
            _suppressAutoHide = false;
        }
    }

    private void ShowInfo(string title, string message)
    {
        _suppressAutoHide = true;
        try
        {
            ConfirmDialog.ShowInfo(this, title, message);
        }
        finally
        {
            _suppressAutoHide = false;
        }
    }
}
