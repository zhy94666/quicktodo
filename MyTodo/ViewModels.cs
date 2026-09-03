using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MyTodo;

/// <summary>任务条目的界面包装。</summary>
public class TaskItem : INotifyPropertyChanged
{
    public TodoItem Item { get; }
    public TaskItem(TodoItem item)
    {
        Item = item;
        _title = item.Title;
        _isCompleted = item.IsCompleted;
    }

    public string Id => Item.Id;

    private string _title;
    public string Title
    {
        get => _title;
        set { _title = value; Item.Title = value; OnProp(); }
    }

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            _isCompleted = value;
            Item.IsCompleted = value;
            Item.CompletedAt = value ? DateTime.Now : null;
            OnProp();
            OnProp(nameof(CompletedTimeText));
        }
    }

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set { _isEditing = value; OnProp(); OnProp(nameof(NotEditing)); }
    }

    public bool NotEditing => !_isEditing;

    /// <summary>完成时间：随手记=完整时间，按日期=仅时刻。</summary>
    public string CompletedTimeText
    {
        get
        {
            if (Item.CompletedAt is null) return "";
            return Item.Mode == "dated"
                ? "已完成 " + Item.CompletedAt.Value.ToString("HH:mm")
                : "已完成 " + Item.CompletedAt.Value.ToString("yyyy年M月d日 HH:mm");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnProp([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>回收站列表行。</summary>
public class RecycleRow
{
    public RecycleRow(TodoItem item)
    {
        Item = item;
        Title = item.Title;
        var d = item.DeletedAt ?? DateTime.Now;
        DeletedTimeText = "删除于 " + d.ToString("yyyy年M月d日 HH:mm");
    }

    public TodoItem Item { get; }
    public string Title { get; }
    public string DeletedTimeText { get; }
}
