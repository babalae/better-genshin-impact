using System;

namespace BetterGenshinImpact.Infrastructure.Network.Download;

/// <summary>
/// 下载状态
/// </summary>
public enum DownloadState
{
    /// <summary>排队等待空闲的下载位置</summary>
    Queued,

    /// <summary>正在下载</summary>
    Downloading,

    /// <summary>正在校验大小和哈希</summary>
    Verifying,

    /// <summary>已完成</summary>
    Completed,

    /// <summary>失败，已下载部分保留供续传（校验失败时除外）</summary>
    Failed,

    /// <summary>已取消，已下载部分保留供续传</summary>
    Canceled,
}

/// <summary>
/// 下载进度快照
/// </summary>
/// <param name="State">状态</param>
/// <param name="ReceivedBytes">已下载的字节数，含续传前已有的部分</param>
/// <param name="TotalBytes">总大小，未知时为 null</param>
/// <param name="BytesPerSecond">最近 3 秒的平均速度</param>
/// <param name="Error">失败原因，只在 Failed 时有值</param>
public sealed record DownloadProgress(DownloadState State, long ReceivedBytes, long? TotalBytes,
    double BytesPerSecond, string? Error = null)
{
    /// <summary>
    /// 排队中的初始进度
    /// </summary>
    public static DownloadProgress Queued { get; } = new(DownloadState.Queued, 0, null, 0);

    /// <summary>
    /// 是否已经结束（完成、失败或取消）
    /// </summary>
    public bool IsFinished => State is DownloadState.Completed or DownloadState.Failed or DownloadState.Canceled;

    /// <summary>
    /// 完成百分比，总大小未知时为 null
    /// </summary>
    public double? Percentage => TotalBytes > 0 ? Math.Clamp(ReceivedBytes * 100d / TotalBytes.Value, 0, 100) : null;

    /// <summary>
    /// 预计剩余时间，总大小或速度未知时为 null
    /// </summary>
    public TimeSpan? Remaining => TotalBytes > 0 && BytesPerSecond > 0
        ? TimeSpan.FromSeconds(Math.Max(0, TotalBytes.Value - ReceivedBytes) / BytesPerSecond)
        : null;
}
