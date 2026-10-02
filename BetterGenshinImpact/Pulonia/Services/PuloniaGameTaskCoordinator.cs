using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 按需准备游戏运行环境，并让 Pulonia 与现有独立任务共用同一输入所有权。
/// </summary>
public sealed class PuloniaGameTaskCoordinator
{
    /// <summary>
    /// 游戏截图、窗口和输入后端的统一运行环境入口。
    /// </summary>
    private readonly GameRuntimeService _runtimeService;

    /// <summary>
    /// 使用游戏运行环境服务建立协调器。
    /// </summary>
    public PuloniaGameTaskCoordinator(GameRuntimeService runtimeService)
    {
        _runtimeService = runtimeService;
    }

    /// <summary>
    /// 等待全局任务锁，必要时启动游戏会话，并进入现有任务模式。
    /// </summary>
    public async Task<PuloniaGameTaskLease> AcquireAsync(CancellationToken ct)
    {
        await TaskControl.TaskSemaphore.WaitAsync(ct).ConfigureAwait(false);
        var runner = new TaskRunner();
        try
        {
            var started = await _runtimeService.StartAsync(ct).ConfigureAwait(false);
            if (!started || !_runtimeService.IsRunning)
                throw new InvalidOperationException("无法准备游戏运行环境。");
            ct.ThrowIfCancellationRequested();

            // 不重置全局 CancellationContext；Pulonia 节点始终使用自己的运行令牌。
            runner.Init();
            RunnerContext.Instance.Clear();
            return new PuloniaGameTaskLease(runner);
        }
        catch
        {
            try
            {
                CleanupTaskState(runner);
            }
            finally
            {
                TaskControl.TaskSemaphore.Release();
            }
            throw;
        }
    }

    /// <summary>
    /// 即使任务模式清理失败，也继续尝试释放输入并清空临时运行上下文。
    /// </summary>
    internal static void CleanupTaskState(TaskRunner runner)
    {
        try
        {
            runner.End();
        }
        finally
        {
            try
            {
                InputHub.ReleaseAll();
            }
            finally
            {
                RunnerContext.Instance.Clear();
            }
        }
    }
}

/// <summary>
/// 一次游戏型节点持有的任务模式与输入所有权。
/// </summary>
public sealed class PuloniaGameTaskLease : IDisposable
{
    /// <summary>
    /// 复用现有任务模式的清理入口。
    /// </summary>
    private readonly TaskRunner _runner;

    /// <summary>
    /// 防止重复释放任务锁。
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 保存已经进入任务模式的运行器。
    /// </summary>
    internal PuloniaGameTaskLease(TaskRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// 释放所有按键、退出任务模式并归还全局任务锁；游戏会话保留以供后续节点复用。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            PuloniaGameTaskCoordinator.CleanupTaskState(_runner);
        }
        finally
        {
            TaskControl.TaskSemaphore.Release();
        }
    }
}
