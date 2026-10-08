using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 「Worker 日志」独立窗口的 ViewModel：维护日志行缓冲与行数上限，
/// 实际渲染由窗口增量追加（每次整段重排会明显卡顿）。
/// </summary>
public partial class WorkerLogWindowViewModel : ObservableObject
{
    /// <summary>窗口最多保留的日志行数，超出后丢弃最早的行</summary>
    private const int MaxLines = 2000;

    private readonly List<string> _lines = new(MaxLines + 1);

    public WorkerLogWindowViewModel(string title)
    {
        Title = title;
    }

    /// <summary>窗口标题（Worker 侧与控制侧文案不同）</summary>
    public string Title { get; }

    /// <summary>新增日志行：由窗口增量追加到日志框</summary>
    public event EventHandler<IReadOnlyList<string>>? LinesAppended;

    /// <summary>日志已清空：由窗口清空日志框</summary>
    public event EventHandler? LinesCleared;

    /// <summary>自动滚动到最新一行</summary>
    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private string _statusText = "0 行";

    /// <summary>
    /// 追加日志行。必须在 UI 线程调用（窗口直接操作日志框控件）
    /// </summary>
    public void Append(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        _lines.AddRange(lines);
        var dropped = _lines.Count - MaxLines;
        if (dropped > 0)
        {
            _lines.RemoveRange(0, dropped);
        }

        StatusText = $"{_lines.Count} 行";
        LinesAppended?.Invoke(this, lines);
    }

    [RelayCommand]
    private void Clear()
    {
        _lines.Clear();
        StatusText = "0 行";
        LinesCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 窗口重新打开时把已有日志整段回填（日志框是新建的）
    /// </summary>
    public IReadOnlyList<string> Snapshot()
    {
        return _lines.ToArray();
    }
}
