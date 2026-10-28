using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Infrastructure.Network.Download;

/// <summary>
/// 下载监视器：本次运行中显示在下载中心的任务
/// </summary>
public interface IDownloadMonitor
{
    /// <summary>
    /// 任务快照，按开始时间排列
    /// </summary>
    IReadOnlyList<DownloadTask> Tasks { get; }

    /// <summary>
    /// 任务增减或状态变化，在后台线程触发。进度变化不触发，需要时读取 <see cref="DownloadTask.Progress"/>
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// 用原请求重新下载，已下载部分会续传。新任务替换列表中的原任务
    /// </summary>
    /// <exception cref="InvalidOperationException">任务不存在、未结束，或由发起方负责重试</exception>
    DownloadTask Retry(Guid id);

    /// <summary>
    /// 移除已完成、失败和已取消的任务
    /// </summary>
    void ClearFinished();
}
