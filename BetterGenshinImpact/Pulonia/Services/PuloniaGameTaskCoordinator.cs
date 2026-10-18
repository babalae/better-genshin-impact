using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.View;

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
    /// 租约记录本次启动返回的运行环境，节点内的任务只使用它。
    /// </summary>
    public async Task<PuloniaGameTaskLease> AcquireAsync(CancellationToken ct)
    {
        await TaskControl.TaskSemaphore.WaitAsync(ct).ConfigureAwait(false);
        GameRuntime? runtime = null;
        try
        {
            runtime = await _runtimeService.StartAsync(ct).ConfigureAwait(false);
            if (runtime is null || runtime.IsStopping)
                throw new InvalidOperationException("无法准备游戏运行环境。");
            ct.ThrowIfCancellationRequested();

            // Pulonia 节点始终沿调用链使用本次运行令牌，不创建独立任务执行作用域。
            BeginTaskState(runtime);
            RunnerContext.Instance.Clear();
            return new PuloniaGameTaskLease(runtime);
        }
        catch
        {
            try
            {
                CleanupTaskState(runtime);
            }
            finally
            {
                TaskControl.TaskSemaphore.Release();
            }
            throw;
        }
    }

    /// <summary>
    /// 为 Pulonia 游戏节点进入现有任务模式，但不创建另一个任务执行入口。
    /// </summary>
    /// <param name="runtime">本次租约记录的运行环境</param>
    private static void BeginTaskState(GameRuntime runtime)
    {
        TaskTriggerDispatcher.Instance().BeginTask();
        runtime.MaskWindowMapState.Reset();
        runtime.MaskWindowDrawingBoard.ClearAll();
        runtime.Window.Activate();
    }

    /// <summary>
    /// 即使任务模式清理失败，也继续尝试释放输入并清空临时运行上下文。
    /// </summary>
    /// <param name="runtime">租约记录的运行环境；启动失败时为 null，此时跳过清理遮罩画板</param>
    internal static void CleanupTaskState(GameRuntime? runtime)
    {
        try
        {
            TaskTriggerDispatcher.InstanceNullable()?.EndTask();
        }
        finally
        {
            try
            {
                InputHub.ReleaseAll();
            }
            finally
            {
                try
                {
                    runtime?.MaskWindowDrawingBoard.ClearAll();
                }
                finally
                {
                    try
                    {
                        HtmlMaskWindow.CloseAll();
                    }
                    finally
                    {
                        RunnerContext.Instance.Clear();
                    }
                }
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
    /// 防止重复释放任务锁。
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 获取租约时绑定的运行环境。节点内的任务只使用它；它停止后本租约下的节点失败，不切换到新的运行环境。
    /// </summary>
    public GameRuntime Runtime { get; }

    /// <summary>
    /// 创建已经进入任务模式的游戏节点租约。
    /// </summary>
    /// <param name="runtime">本次启动返回的运行环境</param>
    internal PuloniaGameTaskLease(GameRuntime runtime)
    {
        Runtime = runtime;
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
            PuloniaGameTaskCoordinator.CleanupTaskState(Runtime);
        }
        finally
        {
            TaskControl.TaskSemaphore.Release();
        }
    }
}
