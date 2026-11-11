using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Plugins.Pluggables;

/// <summary>
/// 可插拔能力接口的代理，由 <see cref="DispatchProxy"/> 在运行时生成接口实现。
/// 每次调用向槽申请租约、在当前实例上执行，调用结束（返回 Task 时为任务完成）后归还租约。
/// </summary>
/// <remarks>
/// DispatchProxy 要求代理类不是 sealed，且有无参构造函数。
/// </remarks>
internal class PluggableProxy : DispatchProxy
{
    /// <summary>
    /// 所属注册表，用于报告调用失败。
    /// </summary>
    private PluggableRegistry _registry = null!;

    /// <summary>
    /// 可插拔能力的槽。
    /// </summary>
    private PluggableSlot _slot = null!;

    /// <summary>
    /// 固定使用的实现标识，null 表示按用户选择。
    /// </summary>
    private string? _pinnedId;

    /// <summary>
    /// 为可插拔能力接口创建代理。
    /// </summary>
    /// <param name="contract">可插拔能力接口。</param>
    /// <param name="registry">所属注册表。</param>
    /// <param name="slot">可插拔能力的槽。</param>
    /// <param name="pinnedId">固定使用的实现标识，null 表示按用户选择。</param>
    public static object Create(Type contract, PluggableRegistry registry, PluggableSlot slot, string? pinnedId)
    {
        var proxy = DispatchProxy.Create(contract, typeof(PluggableProxy));
        var dispatcher = (PluggableProxy)proxy;
        dispatcher._registry = registry;
        dispatcher._slot = slot;
        dispatcher._pinnedId = pinnedId;
        return proxy;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        var lease = _slot.Acquire(_pinnedId);
        object? result;
        try
        {
            result = targetMethod.Invoke(lease.Instance.Target, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            // 拆开反射包装，调用方看到的异常与直接调用实现时相同
            lease.Dispose();
            _registry.OnCallFailed(lease.Instance.Implementation, e.InnerException);
            ExceptionDispatchInfo.Throw(e.InnerException);
            throw;
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        if (result is Task task)
        {
            // 返回原任务对象以保持返回类型；任务完成后再归还租约，保证异步调用期间实例不被释放
            task.ContinueWith(static (completed, state) =>
                {
                    var (registry, completedLease) = ((PluggableRegistry, PluggableLease))state!;
                    completedLease.Dispose();
                    if (completed.Exception?.InnerException is { } inner)
                    {
                        registry.OnCallFailed(completedLease.Instance.Implementation, inner);
                    }
                }, (_registry, lease), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }

        lease.Dispose();
        return result;
    }
}
