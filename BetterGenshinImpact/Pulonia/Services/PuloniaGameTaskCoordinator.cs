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
    /// </summary>
    public async Task<PuloniaGameTaskLease> AcquireAsync(CancellationToken ct, Action? inputCheck = null)
    {
        await TaskControl.TaskSemaphore.WaitAsync(ct).ConfigureAwait(false);
        IDisposable? safety = null;
        try
        {
            // 取得游戏输入所有权后才安装安全门，排队或纯任务不能干扰已有独立任务输入。
            safety = inputCheck is null ? null : InputSafetyGate.Enter(inputCheck);
            inputCheck?.Invoke();
            var started = await _runtimeService.StartAsync(ct).ConfigureAwait(false);
            if (!started || !_runtimeService.IsRunning)
                throw new InvalidOperationException("无法准备游戏运行环境。");
            ct.ThrowIfCancellationRequested();

            // Pulonia 节点始终沿调用链使用本次运行令牌，不创建独立任务执行作用域。
            BeginTaskState();
            RunnerContext.Instance.Clear();
            return new PuloniaGameTaskLease(safety);
        }
        catch
        {
            try
            {
                CleanupTaskState();
            }
            finally
            {
                safety?.Dispose();
                TaskControl.TaskSemaphore.Release();
            }
            throw;
        }
    }

    /// <summary>
    /// 为 Pulonia 游戏节点进入现有任务模式，但不创建另一个任务执行入口。
    /// </summary>
    private static void BeginTaskState()
    {
        TaskTriggerDispatcher.Instance().BeginTask();
        var runtime = TaskContext.Instance().Runtime;
        runtime?.MaskWindowMapState.Reset();
        runtime?.MaskWindowDrawingBoard.ClearAll();
        SystemControl.ActivateWindow();
    }

    /// <summary>
    /// 即使任务模式清理失败，也继续尝试释放输入并清空临时运行上下文。
    /// </summary>
    internal static void CleanupTaskState()
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
                    TaskContext.Instance().Runtime?.MaskWindowDrawingBoard.ClearAll();
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
    /// <summary>与本次游戏输入所有权绑定的安全门，抬键结束后释放。</summary>
    private readonly IDisposable? _inputSafety;

    /// <summary>
    /// 创建已经进入任务模式的游戏节点租约。
    /// </summary>
    internal PuloniaGameTaskLease(IDisposable? inputSafety = null)
    {
        _inputSafety = inputSafety;
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
            PuloniaGameTaskCoordinator.CleanupTaskState();
        }
        finally
        {
            _inputSafety?.Dispose();
            TaskControl.TaskSemaphore.Release();
        }
    }
}
