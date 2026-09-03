using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTodo;

public class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>quick = 随手记；dated = 按日期。</summary>
    public string Mode { get; set; } = "quick";

    /// <summary>dated 模式所属日期（一天一份）。</summary>
    public DateTime? Day { get; set; }

    /// <summary>手动排序序号（未完成区内有效）。</summary>
    public int Order { get; set; }

    /// <summary>回收站：软删除标记；保留 7 天后自动清除。</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class AppSettings
{
    public int Modifiers { get; set; } = 3;       // Ctrl | Alt
    public int VirtualKey { get; set; } = 0x54;   // T

    /// <summary>快速收集热键：把剪贴板文字一键存入随手记。</summary>
    public bool QuickCollectEnabled { get; set; } = true;
    public int QuickCollectModifiers { get; set; } = 3;       // Ctrl | Alt
    public int QuickCollectVirtualKey { get; set; } = 0x56;   // V

    public bool AutoHideOnFocusLost { get; set; } = true;
    public bool StartWithWindows { get; set; }

    /// <summary>主面板是否置顶。</summary>
    public bool MainPanelTopmost { get; set; } = true;

    /// <summary>是否在桌面显示小组件（通过托盘开关）。</summary>
    public bool ShowDesktopWidget { get; set; }

    /// <summary>桌面小组件整体透明度（0.25~1.00）。</summary>
    public double DesktopWidgetOpacity { get; set; } = 1.0;

    /// <summary>桌面小组件上次位置；空值表示使用默认位置。</summary>
    public double? DesktopWidgetLeft { get; set; }
    public double? DesktopWidgetTop { get; set; }

    /// <summary>桌面小组件紧凑模式；独立于“桌面小组件”显示开关。</summary>
    public bool DesktopWidgetCompact { get; set; }

    [JsonIgnore]
    public string HotkeyText => FormatHotkey(Modifiers, VirtualKey);

    [JsonIgnore]
    public string QuickCollectHotkeyText => FormatHotkey(QuickCollectModifiers, QuickCollectVirtualKey);

    public static string FormatHotkey(int modifiers, int virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 8) != 0) parts.Add("Win");
        var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(virtualKey);
        var name = key.ToString();
        if (name.Length == 1) name = name.ToUpperInvariant();
        parts.Add(name);
        return string.Join(" + ", parts);
    }
}

public class AppData
{
    public List<TodoItem> Items { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

public class DataStore
{
    /// <summary>数据目录（data.json 与日志文件同目录）。</summary>
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyTodo");

    public static readonly string DataFilePath = Path.Combine(DataDir, "data.json");
    public static readonly string LogTracePath = Path.Combine(DataDir, "trace.log");
    public static readonly string LogErrorPath = Path.Combine(DataDir, "error.log");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>导出文件使用的 JSON 选项；导入时也用它，保持字段命名一致。</summary>
    public static JsonSerializerOptions BackupJsonOptions => JsonOptions;

    public AppData Load()
    {
        try
        {
            if (File.Exists(DataFilePath))
            {
                var json = File.ReadAllText(DataFilePath);
                var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions);
                if (data != null)
                {
                    data.Settings ??= new AppSettings();
                    data.Settings.DesktopWidgetOpacity =
                        Math.Clamp(data.Settings.DesktopWidgetOpacity, 0.25, 1.0);
                    data.Items ??= new List<TodoItem>();
                    // 兼容旧数据：无模式字段的一律视为随手记
                    foreach (var item in data.Items)
                    {
                        if (string.IsNullOrEmpty(item.Mode)) item.Mode = "quick";
                        if (item.Mode != "dated") item.Day = null;
                        if (item.IsCompleted && item.CompletedAt is null)
                            item.CompletedAt = item.CreatedAt;
                    }
                    // 旧数据没有手动排序：按创建时间生成初始顺序
                    if (data.Items.All(t => t.Order == 0))
                    {
                        int i = 0;
                        foreach (var item in data.Items.OrderBy(t => t.CreatedAt))
                            item.Order = i++;
                    }
                    // 回收站过期清理：软删除超过 7 天的条目在启动时永久移除
                    data.Items.RemoveAll(t => t.IsDeleted
                        && t.DeletedAt is DateTime d
                        && DateTime.Now - d > TimeSpan.FromDays(7));
                    return data;
                }
            }
        }
        catch { /* 损坏的数据不阻塞启动 */ }
        return new AppData();
    }

    public void Save(AppData data)
    {
        Directory.CreateDirectory(DataDir);
        var tmp = DataFilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(tmp, DataFilePath, overwrite: true);
    }

    /// <summary>
    /// 清理导入数据中的异常值。导出的备份本身应当合法，
    /// 这里主要兜底处理外部编辑过的 JSON，避免导入后列表缺日期或无排序。
    /// </summary>
    public static void NormalizeItems(List<TodoItem> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is null) items.RemoveAt(i);
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
            item.Title ??= "";
            if (item.CreatedAt == default) item.CreatedAt = DateTime.Now;

            if (string.IsNullOrWhiteSpace(item.Mode) || item.Mode != "dated")
            {
                item.Mode = "quick";
                item.Day = null;
            }
            else
            {
                item.Day ??= item.CreatedAt.Date;
            }

            if (item.IsCompleted && item.CompletedAt is null)
                item.CompletedAt = item.CreatedAt;
        }
    }
}

/// <summary>导出到 JSON 的待办数据快照。</summary>
public class TodoBackupFile
{
    public int SchemaVersion { get; set; } = 1;
    public string App { get; set; } = "MyTodo";
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public List<TodoItem> Items { get; set; } = new();
}
