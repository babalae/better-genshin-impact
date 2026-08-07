using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Suspend;

/// <summary>
/// 路径执行器暂停机制实现 / Path executor suspend logic implementation.
/// </summary>
public class PathExecutorSuspend : ISuspendable, IPathingSuspendState
{
    private readonly PathExecutor _pathExecutor;
    private bool _isSuspended;
    private DateTime _suspendTimeUtc = DateTime.MinValue;
    private int _resumeRecoveryPending;

    /// <inheritdoc/>
    public bool IsSuspended => _isSuspended;

    /// <summary>
    /// Gets whether the executor must explicitly recover UI and positioning after a resume.
    /// 获取是否需要在恢复后由执行器显式恢复界面和定位。
    /// </summary>
    public bool IsResumeRecoveryPending => Volatile.Read(ref _resumeRecoveryPending) != 0;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    public PathExecutorSuspend(PathExecutor pathExecutor)
    {
        _pathExecutor = pathExecutor ?? throw new ArgumentNullException(nameof(pathExecutor));
    }

    /// <inheritdoc/>
    public void Suspend()
    {
        _suspendTimeUtc = DateTime.UtcNow;
        _isSuspended = true;
    }

    /// <summary>
    /// Marks the explicit resume recovery as completed.
    /// 标记显式暂停恢复已经完成。
    /// </summary>
    public void CompleteResumeRecovery()
    {
        Interlocked.Exchange(ref _resumeRecoveryPending, 0);
    }

    /// <inheritdoc/>
    public void Resume()
    {
        if (!_isSuspended)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var suspendDuration = _suspendTimeUtc == DateTime.MinValue
            ? TimeSpan.Zero
            : now - _suspendTimeUtc;
        if (suspendDuration < TimeSpan.Zero)
        {
            suspendDuration = TimeSpan.Zero;
        }

        Logger.LogInformation("路径恢复：暂停时长={SuspendSec:F1}s，等待执行器恢复界面和定位", suspendDuration.TotalSeconds);

        // 恢复时重置移动超时，并通知 PathExecutor 中断当前点的后续动作后显式恢复。
        _pathExecutor.MovementController.ResetMoveToStartTime(now);
        _isSuspended = false;
        _suspendTimeUtc = DateTime.MinValue;
        Interlocked.Exchange(ref _resumeRecoveryPending, 1);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _isSuspended = false;
        _suspendTimeUtc = DateTime.MinValue;
        Interlocked.Exchange(ref _resumeRecoveryPending, 0);
    }
}
