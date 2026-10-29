using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.View;
using Microsoft.Extensions.Logging;

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
    /// 等待全局任务锁，必要时启动游戏会话，等待订阅脚本自动更新结束，并进入现有任务模式。
    /// 租约记录本次启动返回的运行环境，节点内的任务只使用它。
    /// </summary>
    /// <exception cref="InvalidOperationException">当前异步流已经持有任务会话，或无法准备游戏运行环境。</exception>
    /// <exception cref="TimeoutException">等待订阅脚本自动更新超时。</exception>
    public async Task<PuloniaGameTaskLease> AcquireAsync(CancellationToken ct)
    {
        // 会话内已经持有任务锁（信号量不可重入），再次等待会永久挂起；与 TaskRunner 一致直接报错便于定位
        if (GameTaskSessionScope.IsActive)
            throw new InvalidOperationException("当前已在任务会话中，不能再次获取游戏任务租约，请直接使用已持有的运行环境。");

        // 先尝试立即取得任务锁；被独立任务或脚本占用时先提示再等待，避免日志窗口没有输出、看起来像卡住
        if (!await TaskControl.TaskSemaphore.WaitAsync(0, ct).ConfigureAwait(false))
        {
            TaskControl.Logger.LogInformation("一条龙：当前有其他任务正在运行，等待它结束后继续");
            await TaskControl.TaskSemaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        GameRuntime? runtime = null;
        try
        {
            runtime = await _runtimeService.StartAsync(ct).ConfigureAwait(false);
            if (runtime is null || runtime.IsStopping)
                throw new InvalidOperationException("无法准备游戏运行环境。");
            ct.ThrowIfCancellationRequested();

            // 自动更新会删除并重新检出用户脚本目录，与 TaskRunner 一样等它结束后再执行节点
            await ScriptRepoUpdater.Instance.WaitForAutoUpdateAsync(ct).ConfigureAwait(false);

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
