using System.IO;

namespace MyTodo;

/// <summary>全局共享数据会话：主面板与桌面小组件操作同一份待办数据。</summary>
public static class Session
{
    public static DataStore Store { get; } = new();
    public static AppData Data { get; } = Store.Load();

    public static event Action? DataChanged;

    public static void Save()
    {
        try { Store.Save(Data); }
        catch { }
        DataChanged?.Invoke();
    }

    /// <summary>软删除：移入回收站（保留 7 天）。</summary>
    public static void SoftDelete(TodoItem item)
    {
        item.IsDeleted = true;
        item.DeletedAt = DateTime.Now;
        LogEvent("task -> recycle: " + item.Title);
    }

    /// <summary>从回收站恢复。</summary>
    public static void Restore(TodoItem item)
    {
        item.IsDeleted = false;
        item.DeletedAt = null;
        LogEvent("task restored: " + item.Title);
    }

    public static void LogEvent(string msg)
    {
        try
        {
            Directory.CreateDirectory(DataStore.DataDir);
            File.AppendAllText(DataStore.LogTracePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}" + Environment.NewLine);
        }
        catch { }
    }

    public static void LogError(string msg, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DataStore.DataDir);
            File.AppendAllText(DataStore.LogErrorPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}: {ex}\r\n\r\n");
        }
        catch { }
    }
}
