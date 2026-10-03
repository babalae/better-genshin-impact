using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 标识外部请求停止当前任务的原因。
/// </summary>
public enum TaskStopReason
{
    /// <summary>
    /// 用户通过停止按钮或全局停止热键请求停止。
    /// </summary>
    UserRequested,

    /// <summary>
    /// 游戏运行环境停止，依赖该环境的任务必须退出。
    /// </summary>
    RuntimeStopped,

    /// <summary>
    /// 应用程序正在关闭，尚未结束的任务必须退出。
    /// </summary>
    ApplicationShutdown
}

/// <summary>
/// 在现有执行入口之间转发停止信号，不持有任务令牌或执行任务。
/// </summary>
public sealed class TaskStopService
{
    /// <summary>
    /// 保护活动停止回调集合。
    /// </summary>
    private readonly object _syncRoot = new();

    /// <summary>
    /// 当前仍处于运行生命周期内的停止回调。
    /// </summary>
    private readonly Dictionary<long, StopRegistration> _registrations = [];

    /// <summary>
    /// 记录停止回调异常，确保单个回调失败不会阻断其他任务停止。
    /// </summary>
    private readonly ILogger<TaskStopService> _logger;

    /// <summary>
    /// 为每个停止回调生成进程内唯一标识。
    /// </summary>
    private long _nextRegistrationId;

    /// <summary>
    /// 创建停止信号转发服务。
    /// </summary>
    public TaskStopService(ILogger<TaskStopService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 注册一个仅在当前运行生命周期内有效的停止回调。
    /// </summary>
    /// <param name="stopAction">收到停止请求时执行的回调。</param>
    /// <returns>释放后立即注销回调的注册句柄。</returns>
    public IDisposable Register(Action<TaskStopReason> stopAction)
    {
        ArgumentNullException.ThrowIfNull(stopAction);

        var id = Interlocked.Increment(ref _nextRegistrationId);
        var registration = new StopRegistration(this, id, stopAction);
        lock (_syncRoot)
        {
            _registrations.Add(id, registration);
        }

        return registration;
    }

    /// <summary>
    /// 向当前所有活动运行发送同一个停止信号。
    /// </summary>
    /// <param name="reason">本次停止请求的来源。</param>
    public void StopAll(TaskStopReason reason)
    {
        StopRegistration[] registrations;
        lock (_syncRoot)
        {
            registrations = [.. _registrations.Values];
        }

        foreach (var registration in registrations)
        {
            try
            {
                registration.Invoke(reason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "转发任务停止信号时发生异常：{Message}", ex.Message);
            }
        }
    }

    /// <summary>
    /// 从活动集合中移除已经结束的运行。
    /// </summary>
    /// <param name="id">注册时生成的唯一标识。</param>
    private void Unregister(long id)
    {
        lock (_syncRoot)
        {
            _registrations.Remove(id);
        }
    }

    /// <summary>
    /// 单个停止回调的可释放注册句柄。
    /// </summary>
    private sealed class StopRegistration : IDisposable
    {
        /// <summary>
        /// 保证注销完成后不会再进入停止回调。
        /// </summary>
        private readonly object _invokeGate = new();

        /// <summary>
        /// 注册所属的停止信号服务。
        /// </summary>
        private readonly TaskStopService _owner;

        /// <summary>
        /// 当前注册的唯一标识。
        /// </summary>
        private readonly long _id;

        /// <summary>
        /// 调用方提供的停止回调。
        /// </summary>
        private readonly Action<TaskStopReason> _stopAction;

        /// <summary>
        /// 防止重复释放或释放后再次调用回调。
        /// </summary>
        private int _disposed;

        /// <summary>
        /// 创建停止回调注册句柄。
        /// </summary>
        public StopRegistration(TaskStopService owner, long id, Action<TaskStopReason> stopAction)
        {
            _owner = owner;
            _id = id;
            _stopAction = stopAction;
        }

        /// <summary>
        /// 在注册仍然有效时转发停止请求。
        /// </summary>
        public void Invoke(TaskStopReason reason)
        {
            lock (_invokeGate)
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    _stopAction(reason);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_invokeGate)
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.Unregister(_id);
                }
            }
        }
    }
}
