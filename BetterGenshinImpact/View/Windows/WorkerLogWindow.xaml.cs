using System;
using System.Collections.Generic;
using System.Windows;
using BetterGenshinImpact.ViewModel.Windows;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 「Worker 日志」独立窗口。Worker 侧（远程独立窗口）与控制侧（本地独立窗口）复用同一个窗口，
/// 只由 <see cref="WorkerLogWindowService"/> 决定标题与数据来源。
/// </summary>
public partial class WorkerLogWindow
{
    private readonly WorkerLogWindowViewModel _viewModel;

    public WorkerLogWindow(WorkerLogWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.LinesAppended += OnLinesAppended;
        _viewModel.LinesCleared += OnLinesCleared;
        Closed += OnWindowClosed;
    }

    /// <summary>
    /// 用户主动关闭（区别于应用退出时的关闭）
    /// </summary>
    public bool ClosedByUser { get; private set; }

    public void AppendSnapshot(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            LogBox.AppendText(line);
            LogBox.AppendText(Environment.NewLine);
        }

        if (_viewModel.AutoScroll)
        {
            LogBox.ScrollToEnd();
        }
    }

    private void OnLinesAppended(object? sender, IReadOnlyList<string> lines)
    {
        AppendSnapshot(lines);
    }

    private void OnLinesCleared(object? sender, EventArgs e)
    {
        LogBox.Clear();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _viewModel.LinesAppended -= OnLinesAppended;
        _viewModel.LinesCleared -= OnLinesCleared;
        ClosedByUser = true;
    }
}
