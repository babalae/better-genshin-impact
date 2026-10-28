using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Infrastructure.Network.Download;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 标题栏下载中心：本次运行的下载任务列表、总进度和失败提示（见设计文档 9.4 节）
/// </summary>
public partial class DownloadCenterViewModel : ObservableObject
{
    /// <summary>
    /// 有下载进行时刷新进度的间隔
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 下载任务列表
    /// </summary>
    private readonly IDownloadMonitor _monitor;

    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger<DownloadCenterViewModel> _logger;

    /// <summary>
    /// 界面线程
    /// </summary>
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// 有下载进行时定时刷新进度；任务列表和状态变化由监视器事件触发
    /// </summary>
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// 是否已安排一次刷新，合并后台线程的连续事件
    /// </summary>
    private int _syncQueued;

    /// <summary>
    /// 用户已确认退出。托盘“退出”确认后调用 Shutdown，主窗口的 Closing 还会再触发一次，不再重复询问
    /// </summary>
    private bool _exitConfirmed;

    public DownloadCenterViewModel(IDownloadMonitor monitor, ILogger<DownloadCenterViewModel> logger)
    {
        _monitor = monitor;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = RefreshInterval };
        _timer.Tick += (_, _) => Sync();
        _monitor.Changed += OnMonitorChanged;
        _dispatcher.InvokeAsync(Sync, DispatcherPriority.Background);
    }

    /// <summary>
    /// 下载任务行，按开始时间排列
    /// </summary>
    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    /// <summary>
    /// 弹出层是否打开
    /// </summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>
    /// 本次运行是否有下载任务；没有时不显示标题栏按钮
    /// </summary>
    [ObservableProperty]
    private bool _hasItems;

    /// <summary>
    /// 是否有排队、下载或校验中的任务
    /// </summary>
    [ObservableProperty]
    private bool _hasActive;

    /// <summary>
    /// 是否有失败的任务，标题栏按钮显示红点
    /// </summary>
    [ObservableProperty]
    private bool _hasFailed;

    /// <summary>
    /// 进行中任务的总进度百分比
    /// </summary>
    [ObservableProperty]
    private double _overallProgress;

    /// <summary>
    /// 进行中的任务都不知道总大小时，总进度显示为不确定
    /// </summary>
    [ObservableProperty]
    private bool _isOverallIndeterminate;

    /// <summary>
    /// 标题栏按钮的提示
    /// </summary>
    [ObservableProperty]
    private string _buttonToolTip = "下载";

    /// <summary>
    /// 退出前确认：有下载正在进行时询问，返回 true 表示继续退出。需在界面线程调用
    /// </summary>
    public bool ConfirmExit()
    {
        var count = _monitor.Tasks.Count(task => !task.Progress.IsFinished);
        if (count == 0 || _exitConfirmed)
        {
            return true;
        }

        _exitConfirmed = ThemedMessageBox.Question($"有 {count} 个下载正在进行，退出后下次可以继续下载。\n确定要退出吗？",
            "下载未完成", MessageBoxButton.OKCancel, MessageBoxResult.Cancel) == MessageBoxResult.OK;
        return _exitConfirmed;
    }

    /// <summary>
    /// 打开弹出层
    /// </summary>
    [RelayCommand]
    private void Open() => IsOpen = true;

    /// <summary>
    /// 取消下载，已下载的部分保留
    /// </summary>
    [RelayCommand]
    private void Cancel(DownloadItemViewModel? item) => item?.Source.Cancel();

    /// <summary>
    /// 用原请求重新下载，已下载的部分会续传
    /// </summary>
    [RelayCommand]
    private void Retry(DownloadItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            item.Replace(_monitor.Retry(item.Source.Id));
            Sync();
        }
        catch (InvalidOperationException e)
        {
            Toast.Warning(e.Message);
        }
    }

    /// <summary>
    /// 在资源管理器中选中下载完成的文件
    /// </summary>
    [RelayCommand]
    private void OpenFolder(DownloadItemViewModel? item)
    {
        if (item?.Source.ResultPath is not { } path || !File.Exists(path))
        {
            Toast.Warning("文件已被移动或删除");
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "打开下载文件夹失败");
            Toast.Error($"无法打开文件夹：{e.Message}");
        }
    }

    /// <summary>
    /// 移除已完成、失败和已取消的任务
    /// </summary>
    [RelayCommand]
    private void ClearFinished() => _monitor.ClearFinished();

    /// <summary>
    /// 监视器事件在后台线程触发，合并后切到界面线程刷新
    /// </summary>
    private void OnMonitorChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _syncQueued, 1) == 0 && !_dispatcher.HasShutdownStarted)
        {
            _dispatcher.InvokeAsync(Sync, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// 按监视器的任务列表增删行、刷新每一行和总览，任务失败时弹出提示
    /// </summary>
    private void Sync()
    {
        Interlocked.Exchange(ref _syncQueued, 0);
        var tasks = _monitor.Tasks;

        // 1. 删除已从列表移除的任务
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!tasks.Contains(Items[i].Source))
            {
                Items.RemoveAt(i);
            }
        }

        // 2. 按监视器中的顺序加入新任务
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            if (!Items.Any(item => ReferenceEquals(item.Source, task)))
            {
                Items.Insert(Math.Min(i, Items.Count), new DownloadItemViewModel(task));
            }
        }

        // 3. 刷新每一行，新出现的失败弹出提示；下载完成只在列表中标记，不打扰用户
        foreach (var item in Items)
        {
            var previous = item.State;
            item.Update();
            if (item.State == DownloadState.Failed && previous != DownloadState.Failed)
            {
                Toast.Error($"「{item.DisplayName}」下载失败：{item.Source.Progress.Error}");
            }
        }

        UpdateSummary();
    }

    /// <summary>
    /// 刷新标题栏按钮：是否显示、总进度、红点和提示；有下载进行时保持定时刷新
    /// </summary>
    private void UpdateSummary()
    {
        var active = Items.Where(item => item.IsActive).Select(item => item.Source.Progress).ToList();
        var known = active.Where(progress => progress.TotalBytes > 0).ToList();
        HasItems = Items.Count > 0;
        HasActive = active.Count > 0;
        HasFailed = Items.Any(item => item.IsFailed);
        IsOverallIndeterminate = HasActive && known.Count == 0;
        OverallProgress = known.Count > 0
            ? Math.Clamp(known.Sum(progress => progress.ReceivedBytes) * 100d / known.Sum(progress => progress.TotalBytes!.Value), 0, 100)
            : 0;
        ButtonToolTip = HasActive ? $"下载：{active.Count} 个正在进行"
            : HasFailed ? "下载：有下载失败"
            : "下载：已全部完成";

        _timer.IsEnabled = HasActive;
        if (!HasItems)
        {
            IsOpen = false;
        }
    }
}
