using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// GearTask 执行事件总线。
/// 负责在执行器与记录器、UI 投影等消费者之间解耦。
/// </summary>
public interface IGearTaskEventBus
{
    /// <summary>
    /// 发布一条执行事件。
    /// </summary>
    ValueTask PublishAsync(GearTaskExecutionEvent evt, CancellationToken ct = default);

    /// <summary>
    /// 注册一个事件消费者。
    /// </summary>
    IDisposable Subscribe(IGearTaskEventConsumer consumer);
}

/// <summary>
/// GearTask 执行事件消费者。
/// </summary>
public interface IGearTaskEventConsumer
{
    ValueTask ConsumeAsync(GearTaskExecutionEvent evt, CancellationToken ct = default);
}
