using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using DragDropEffects = System.Windows.DragDropEffects;
using DataObject = System.Windows.DataObject;

namespace MyTodo;

/// <summary>任务列表面板：主面板与桌面小组件共用，数据来自 Session。</summary>
public partial class TaskListHost : UserControl, INotifyPropertyChanged
{
    public ObservableCollection<TaskItem> VisibleTasks { get; } = new();
    public ICommand StartEditCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool _isCompact;
    public bool IsCompact
    {
        get => _isCompact;
        set
        {
            if (_isCompact == value) return;
            _isCompact = value;
            ApplyCompactLayout();
            OnPropertyChanged(nameof(IsCompact));
        }
    }

    private string _mode = "quick";          // quick | dated
    private DateTime _selectedDay = DateTime.Today;
    private DateTime _lastKnownToday = DateTime.Today;
    private System.Windows.Threading.DispatcherTimer? _dayWatch;
    private bool _suppressMode;

    private TaskItem? _dragItem;
    private Point _dragStart;

    public TaskListHost()
    {
        InitializeComponent();
        StartEditCommand = new RelayCommand(p => StartEdit(p as TaskItem));
        DataContext = this;
        Session.DataChanged += OnSessionDataChanged;
        Unloaded += (_, _) =>
        {
            Session.DataChanged -= OnSessionDataChanged;
            _dayWatch?.Stop();
        };

        // 程序常驻托盘时会跨天；轻量轮询负责把“当前日期”滚到新的一天。
        _dayWatch = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        _dayWatch.Tick += (_, _) => CheckDayRollover();
        _dayWatch.Start();

        SetMode("quick", force: true);
    }

    private void OnSessionDataChanged() => RefreshAll();

    public void FocusAddBox() => AddBox.Focus();

    private void ApplyCompactLayout()
    {
        var compact = IsCompact;

        AddBox.FontSize = compact ? 12 : 13;
        AddBox.Padding = compact ? new Thickness(8, 5, 8, 5) : new Thickness(10, 7, 10, 7);
        AddHint.FontSize = compact ? 12 : 13;
        AddHint.Margin = compact ? new Thickness(9, 0, 0, 0) : new Thickness(12, 0, 0, 0);

        DateNav.Margin = compact ? new Thickness(0, 3, 0, 0) : new Thickness(0, 6, 0, 0);
        DayLabel.MinWidth = compact ? 68 : 96;
        DayLabel.FontSize = compact ? 12 : 13;

        EmptyState.FontSize = compact ? 11.5 : 12.5;
        CountBadge.FontSize = compact ? 10 : 11;
        CompleteAllBtn.FontSize = compact ? 11 : 12;
        ClearCompletedBtn.FontSize = compact ? 11 : 12;
    }

    // ================= 模式切换 =================

    /// <summary>切换模式；各窗口自己的 ToggleButton 已处理选中态。</summary>
    public void SetMode(string mode, bool force = false)
    {
        if (!force && (_suppressMode || _mode == mode)) return;
        _suppressMode = true;
        _mode = mode;
        _suppressMode = false;
        DateNav.Visibility = mode == "dated" ? Visibility.Visible : Visibility.Collapsed;
        Session.LogEvent("mode -> " + mode);
        RefreshAll();
    }

    private static string DayText(DateTime d)
    {
        if (d == DateTime.Today) return "今天";
        if (d == DateTime.Today.AddDays(-1)) return "昨天";
        if (d == DateTime.Today.AddDays(1)) return "明天";
        var fmt = d.Year == DateTime.Today.Year ? "M月d日 ddd" : "yyyy年M月d日 ddd";
        return d.ToString(fmt);
    }

    private void CheckDayRollover()
    {
        var today = DateTime.Today;
        if (today == _lastKnownToday) return;

        var previousToday = _lastKnownToday;
        _lastKnownToday = today;

        // 只滚动“原本正在看的今天”；用户主动停留的历史日期不抢走。
        if (_selectedDay == previousToday)
        {
            _selectedDay = today;
            Session.LogEvent("date rollover -> " + today.ToString("yyyy-MM-dd"));
        }

        RefreshAll();
    }

    private void DayPrev_Click(object sender, RoutedEventArgs e) => ShiftDay(-1);
    private void DayNext_Click(object sender, RoutedEventArgs e) => ShiftDay(1);

    private void ShiftDay(int days)
    {
        _selectedDay = _selectedDay.AddDays(days);
        RefreshAll();
    }

    private void DayLabel_Click(object sender, RoutedEventArgs e)
    {
        _selectedDay = DateTime.Today;
        RefreshAll();
    }

    private void PickDay_Click(object sender, RoutedEventArgs e) => DayCalendarPopup.IsOpen = true;

    private void DayCalendar_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        _selectedDay = (DateTime)e.AddedItems[0]!;
        DayCalendarPopup.IsOpen = false;
        RefreshAll();
    }

    // ================= 任务列表 =================

    private void RefreshAll()
    {
        DayLabel.Content = DayText(_selectedDay);
        RefreshTasks();
        UpdateBadges();
    }

    private void RefreshTasks()
    {
        var list = _mode == "quick"
            ? Session.Data.Items.Where(t => t.Mode == "quick" && !t.IsDeleted).ToList()
            : Session.Data.Items.Where(t => t.Mode == "dated" && t.Day?.Date == _selectedDay.Date && !t.IsDeleted).ToList();

        // 未完成按手动顺序排，已完成（历史）按完成时间倒序沉底
        list = list
            .OrderBy(t => t.IsCompleted ? 1 : 0)
            .ThenBy(t => t.IsCompleted ? -(t.CompletedAt?.Ticks ?? 0) : t.Order)
            .ToList();

        VisibleTasks.Clear();
        foreach (var t in list)
            VisibleTasks.Add(new TaskItem(t));
    }

    private void UpdateBadges()
    {
        int active = VisibleTasks.Count(t => !t.IsCompleted);
        CountBadge.Text = active == 0 ? "全部完成" : $"{active} 项未完成";
        EmptyState.Text = _mode == "dated" ? "这一天还没有任务" : "暂无任务，添加第一个吧";
        EmptyState.Visibility = VisibleTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompleteAllBtn.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearCompletedBtn.Visibility = VisibleTasks.Any(t => t.IsCompleted)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- 拖动排序 ----

    private void TasksListBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var container = ItemsControl.ContainerFromElement(TasksListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
        _dragItem = container?.DataContext as TaskItem;
        if (_dragItem is { IsCompleted: true }) _dragItem = null; // 已完成的（历史）不参与排序
        _dragStart = e.GetPosition(TasksListBox);
    }

    private void TasksListBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(TasksListBox);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        try
        {
            DragDrop.DoDragDrop(TasksListBox, new DataObject(typeof(TaskItem), _dragItem), DragDropEffects.Move);
        }
        finally
        {
            _dragItem = null;
        }
    }

    private void TasksListBox_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void TasksListBox_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(TaskItem))) return;
        if (e.Data.GetData(typeof(TaskItem)) is not TaskItem src || src.IsCompleted) return;

        // 计算落点在“未完成区”中的插入位置
        int insertAt = VisibleTasks.Count(t => !t.IsCompleted);
        var container = ItemsControl.ContainerFromElement(TasksListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container is not null)
        {
            int index = TasksListBox.ItemContainerGenerator.IndexFromContainer(container);
            if (index >= 0)
            {
                int incompleteBefore = 0;
                for (int i = 0; i < index && i < VisibleTasks.Count; i++)
                    if (!VisibleTasks[i].IsCompleted) incompleteBefore++;

                bool below = e.GetPosition(container).Y > container.ActualHeight / 2;
                insertAt = below ? incompleteBefore + 1 : incompleteBefore;
            }
        }
        MoveTask(src, insertAt);
    }

    private void MoveTask(TaskItem src, int insertAt)
    {
        // FLIP 动画第一步：记录移动前每行的纵向位置
        var before = CaptureRowPositions();

        var ctx = Session.Data.Items
            .Where(t => _mode == "quick" ? t.Mode == "quick" : t.Mode == "dated" && t.Day?.Date == _selectedDay.Date)
            .Where(t => !t.IsDeleted)
            .Where(t => !t.IsCompleted)
            .OrderBy(t => t.Order)
            .ToList();

        ctx.Remove(src.Item);
        insertAt = Math.Max(0, Math.Min(insertAt, ctx.Count));
        ctx.Insert(insertAt, src.Item);
        for (int i = 0; i < ctx.Count; i++) ctx[i].Order = i;
        Session.Save();
        RefreshAll();

        // FLIP 动画第二步：布局完成后，把每行从旧位置平滑滑到新位置
        AnimateRowReflow(before);
    }

    /// <summary>记录当前已实现容器的纵向位置（用于 FLIP 位移动画）。</summary>
    private Dictionary<string, double> CaptureRowPositions()
    {
        var map = new Dictionary<string, double>();
        foreach (var t in VisibleTasks)
        {
            if (TasksListBox.ItemContainerGenerator.ContainerFromItem(t) is not ListBoxItem c) continue;

            // 清掉上一次动画的残影，保证记录的是最终落位
            if (c.RenderTransform is TranslateTransform tr)
            {
                tr.BeginAnimation(TranslateTransform.YProperty, null);
                tr.BeginAnimation(TranslateTransform.XProperty, null);
            }
            map[t.Id] = c.TransformToAncestor(TasksListBox).Transform(new Point(0, 0)).Y;
        }
        return map;
    }

    /// <summary>
    /// FLIP 位移动画：以新旧位置差作为初始偏移，动画归零。
    /// 不缓存视觉树引用（旧方案翻车点），每次布局完成后实时查询容器。
    /// </summary>
    private void AnimateRowReflow(Dictionary<string, double> before)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            foreach (var t in VisibleTasks)
            {
                if (!before.TryGetValue(t.Id, out var oldY)) continue;
                if (TasksListBox.ItemContainerGenerator.ContainerFromItem(t) is not ListBoxItem c) continue;

                var newY = c.TransformToAncestor(TasksListBox).Transform(new Point(0, 0)).Y;
                var delta = oldY - newY;
                if (Math.Abs(delta) < 0.5) continue;

                if (c.RenderTransform is not TranslateTransform translate)
                {
                    translate = new TranslateTransform();
                    c.RenderTransform = translate;
                }
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = delta;
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
            }
        }));
    }

    // ---- 添加 ----

    private void AddBox_TextChanged(object sender, TextChangedEventArgs e)
        => AddHint.Visibility = AddBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void AddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddTask();
    }

    private void AddTask()
    {
        var title = AddBox.Text.Trim();
        if (title.Length == 0) return;
        var item = new TodoItem
        {
            Title = title,
            Mode = _mode,
            Day = _mode == "dated" ? _selectedDay.Date : null,
        };
        // 新任务排在未完成区末尾
        item.Order = Session.Data.Items
            .Where(t => t.Mode == item.Mode && (item.Mode == "quick" || t.Day?.Date == item.Day) && !t.IsCompleted && !t.IsDeleted)
            .Select(t => t.Order).DefaultIfEmpty(-1).Max() + 1;
        Session.Data.Items.Add(item);
        AddBox.Clear();
        Session.Save();
        RefreshAll();
    }

    // ---- 完成 / 删除 / 编辑 ----

    private void TaskCheck_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskItem ti)
            Session.LogEvent("task completed: " + ti.Title + " completed=" + ti.Item.IsCompleted);
        Session.Save();
        UpdateBadges();
        RefreshTasks();
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TaskItem ti) return;
        if (ti.Item.IsDeleted) return;

        var container = TasksListBox.ItemContainerGenerator.ContainerFromItem(ti) as ListBoxItem;
        if (container is null)
        {
            SoftDelete(ti.Item);
            return;
        }
        if (Equals(container.Tag, "deleting")) return;
        container.Tag = "deleting";

        // 入回收站动效：向左滑出 + 淡出，随后高度收拢让下方列表自然合拢
        var translate = new TranslateTransform();
        container.RenderTransform = translate;

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase() };
        var slide = new DoubleAnimation(0, -56, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase() };
        var shrink = new DoubleAnimation(container.ActualHeight, 0, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(140),
            EasingFunction = new QuadraticEase(),
        };
        var margin = new ThicknessAnimation(container.Margin, new Thickness(0), TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(140),
        };

        shrink.Completed += (_, _) => SoftDelete(ti.Item);

        container.BeginAnimation(OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.XProperty, slide);
        container.BeginAnimation(HeightProperty, shrink);
        container.BeginAnimation(MarginProperty, margin);
    }

    private void SoftDelete(TodoItem item)
    {
        if (item.IsDeleted) return;
        Session.SoftDelete(item);
        Session.Save();
        RefreshAll();
    }

    private void StartEdit(TaskItem? ti)
    {
        if (ti is null) return;
        ti.IsEditing = true;
    }

    private void EditBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is TaskItem { IsEditing: true })
        {
            tb.Focus();
            tb.CaretIndex = tb.Text.Length;
        }
    }

    private void CommitEdit(TextBox tb)
    {
        if (tb.DataContext is not TaskItem { IsEditing: true } ti) return;
        var text = tb.Text.Trim();
        if (text.Length == 0)
        {
            SoftDelete(ti.Item);
            return;
        }

        // 文本已随输入实时同步（UpdateSourceTrigger=PropertyChanged），
        // 这里只需退出编辑态并落盘；不重建列表，保持视觉树稳定，
        // 失焦提交才不会打断紧随其后的拖拽或点击。
        ti.Title = text;
        ti.IsEditing = false;
        Session.Save();
    }

    /// <summary>
    /// 提交仍在进行的编辑（失焦保存）。clickSource 为本次点击的原始元素，
    /// 点击发生在编辑框内部时不打断输入。
    /// 不缓存编辑框引用：非空提交只折叠编辑框、不重建列表，
    /// 同一 TextBox 再次进入编辑态时 Loaded 不会重新触发，缓存会过期。
    /// 因此每次都实时在视觉树中查找处于编辑态的编辑框。
    /// </summary>
    public void CommitPendingEdit(DependencyObject? clickSource = null)
    {
        var tb = FindEditingTextBox(TasksListBox);
        if (tb is null) return;

        if (clickSource is not null && tb.IsAncestorOf(clickSource)) return;

        CommitEdit(tb);
    }

    /// <summary>在已实现的容器中查找处于编辑态的 TextBox（编辑中必然已实现）。</summary>
    private static TextBox? FindEditingTextBox(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { DataContext: TaskItem { IsEditing: true } })
                return (TextBox)child;
            var found = FindEditingTextBox(child);
            if (found is not null) return found;
        }
        return null;
    }

    private void EditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            CommitEdit(tb);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && sender is TextBox tb2)
        {
            tb2.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            if (tb2.DataContext is TaskItem ti) ti.IsEditing = false;
            e.Handled = true;
        }
    }

    private void EditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is TaskItem { IsEditing: true })
            CommitEdit(tb);
    }

    private void CompleteAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in Session.Data.Items)
        {
            if (t.IsCompleted) continue;
            if (_mode == "quick" && t.Mode == "quick")
            {
                t.IsCompleted = true;
                t.CompletedAt = DateTime.Now;
            }
            if (_mode == "dated" && t.Mode == "dated" && t.Day?.Date == _selectedDay.Date)
            {
                t.IsCompleted = true;
                t.CompletedAt = DateTime.Now;
            }
        }
        Session.LogEvent("complete all, mode=" + _mode);
        Session.Save();
        RefreshAll();
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        var targets = Session.Data.Items
            .Where(t => t.IsCompleted && !t.IsDeleted &&
                ((_mode == "quick" && t.Mode == "quick") ||
                 (_mode == "dated" && t.Mode == "dated" && t.Day?.Date == _selectedDay.Date)))
            .ToList();
        foreach (var t in targets)
            Session.SoftDelete(t);
        if (targets.Count > 0)
            Session.Save();
        RefreshAll();
    }
}
