using System;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Infrastructure.Network.Download;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 下载中心的一行：把 <see cref="DownloadTask"/> 的进度快照转换为界面文字和按钮状态
/// </summary>
public partial class DownloadItemViewModel : ObservableObject
{
    /// <summary>
    /// 创建一行。状态先视为排队，由下载中心随后调用 <see cref="Update"/>，以便发现刚创建就已失败的任务
    /// </summary>
    /// <param name="task">下载任务</param>
    public DownloadItemViewModel(DownloadTask task)
    {
        Source = task;
    }

    /// <summary>
    /// 当前对应的任务；重试后替换为新任务
    /// </summary>
    public DownloadTask Source { get; private set; }

    /// <summary>
    /// 下载名称
    /// </summary>
    public string DisplayName => Source.Request.DisplayName;

    /// <summary>
    /// 上次刷新时的状态，用于判断状态变化
    /// </summary>
    public DownloadState State { get; private set; } = DownloadState.Queued;

    /// <summary>
    /// 进度文字，例如“612 MB / 1.8 GB · 5.2 MB/s · 剩余 4 分钟”，失败时为原因
    /// </summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>
    /// 进度百分比
    /// </summary>
    [ObservableProperty]
    private double _progressValue;

    /// <summary>
    /// 总大小未知或正在校验，进度条显示为不确定
    /// </summary>
    [ObservableProperty]
    private bool _isIndeterminate;

    /// <summary>
    /// 排队、下载或校验中
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// 是否失败
    /// </summary>
    [ObservableProperty]
    private bool _isFailed;

    /// <summary>
    /// 是否可以在下载中心重试：失败或取消，且不需要回到发起页面重试
    /// </summary>
    [ObservableProperty]
    private bool _canRetry;

    /// <summary>
    /// 是否可以打开所在文件夹：已完成，且文件由用户直接使用
    /// </summary>
    [ObservableProperty]
    private bool _canOpenFolder;

    /// <summary>
    /// 换成重试后的新任务
    /// </summary>
    public void Replace(DownloadTask task)
    {
        Source = task;
        OnPropertyChanged(nameof(DisplayName));
        Update();
    }

    /// <summary>
    /// 按任务的最新进度刷新，需在界面线程调用
    /// </summary>
    public void Update()
    {
        var progress = Source.Progress;
        var origin = Source.Request.Origin;
        State = progress.State;
        IsActive = !progress.IsFinished;
        IsFailed = progress.State == DownloadState.Failed;
        IsIndeterminate = progress.State == DownloadState.Verifying || progress.Percentage is null;
        ProgressValue = progress.Percentage ?? 0;
        CanRetry = (progress.State is DownloadState.Failed or DownloadState.Canceled) && origin is null;
        CanOpenFolder = progress.State == DownloadState.Completed && origin is null;
        StatusText = progress.State switch
        {
            DownloadState.Queued => "排队中",
            DownloadState.Downloading => FormatDownloading(progress),
            DownloadState.Verifying => "正在校验文件…",
            DownloadState.Completed => "已完成",
            DownloadState.Failed => origin is null ? progress.Error ?? "下载失败" : $"{progress.Error ?? "下载失败"}。请在{origin}中重试",
            _ => origin is null ? "已取消，已下载的部分会保留" : $"已取消，已下载的部分会保留。请在{origin}中继续",
        };
    }

    /// <summary>
    /// 下载中的进度文字
    /// </summary>
    private static string FormatDownloading(DownloadProgress progress)
    {
        var size = progress.TotalBytes is { } total
            ? $"{ByteSizeFormatter.Format(progress.ReceivedBytes)} / {ByteSizeFormatter.Format(total)}"
            : ByteSizeFormatter.Format(progress.ReceivedBytes);
        if (progress.BytesPerSecond <= 0)
        {
            return size;
        }

        var text = $"{size} · {ByteSizeFormatter.Format((long)progress.BytesPerSecond)}/s";
        return progress.Remaining is { } remaining ? $"{text} · 剩余 {FormatRemaining(remaining)}" : text;
    }

    /// <summary>
    /// 剩余时间文字：不到 1 分钟按秒，不到 1 小时按分钟，否则按小时和分钟
    /// </summary>
    private static string FormatRemaining(TimeSpan remaining) => remaining.TotalSeconds switch
    {
        < 60 => $"{Math.Max(1, (int)remaining.TotalSeconds)} 秒",
        < 3600 => $"{(int)Math.Ceiling(remaining.TotalMinutes)} 分钟",
        _ => $"{(int)remaining.TotalHours} 小时 {remaining.Minutes} 分钟",
    };
}
