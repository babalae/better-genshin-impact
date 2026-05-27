using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// Pathing 运行期通知接口。
/// PathExecutor 从业务步骤出发发出通知，外部可以选择记录、展示或转发。
/// </summary>
public interface IPathingRuntimeNotifier
{
    ValueTask NotifyWaypointEnteredAsync(int waypointGroupIndex, int waypointIndex, string? action, double positionX, double positionY, CancellationToken ct = default);

    ValueTask NotifyWaypointCompletedAsync(int waypointGroupIndex, int waypointIndex, string? action, double positionX, double positionY, CancellationToken ct = default);

    ValueTask NotifyTeleportCompletedAsync(int waypointGroupIndex, int waypointIndex, double positionX, double positionY, CancellationToken ct = default);

    ValueTask NotifyActionCompletedAsync(int waypointGroupIndex, int waypointIndex, string action, double positionX, double positionY, CancellationToken ct = default);

    ValueTask NotifyResumePointUpdatedAsync(PathingGearTaskResumeState state, string? message = null, CancellationToken ct = default);
}

/// <summary>
/// 空通知器，供不关心 Pathing 运行态的场景使用。
/// </summary>
public sealed class NullPathingRuntimeNotifier : IPathingRuntimeNotifier
{
    public static readonly NullPathingRuntimeNotifier Instance = new();

    public ValueTask NotifyActionCompletedAsync(int waypointGroupIndex, int waypointIndex, string action, double positionX, double positionY, CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask NotifyResumePointUpdatedAsync(PathingGearTaskResumeState state, string? message = null, CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask NotifyTeleportCompletedAsync(int waypointGroupIndex, int waypointIndex, double positionX, double positionY, CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask NotifyWaypointCompletedAsync(int waypointGroupIndex, int waypointIndex, string? action, double positionX, double positionY, CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask NotifyWaypointEnteredAsync(int waypointGroupIndex, int waypointIndex, string? action, double positionX, double positionY, CancellationToken ct = default) => ValueTask.CompletedTask;
}

/// <summary>
/// 把 Pathing 运行步骤转换成 GearTask 执行事件。
/// </summary>
public sealed class GearTaskPathingRuntimeNotifier : IPathingRuntimeNotifier
{
    private readonly GearTaskNodeExecutionContext _context;

    public GearTaskPathingRuntimeNotifier(GearTaskNodeExecutionContext context)
    {
        _context = context;
    }

    public ValueTask NotifyWaypointEnteredAsync(int waypointGroupIndex, int waypointIndex, string? action, double positionX, double positionY, CancellationToken ct = default)
    {
        var evt = _context.CreateEvent<PathingWaypointEnteredEvent>();
        evt.WaypointGroupIndex = waypointGroupIndex;
        evt.WaypointIndex = waypointIndex;
        evt.Action = action;
        evt.PositionX = positionX;
        evt.PositionY = positionY;
        evt.Extra["positionX"] = positionX;
        evt.Extra["positionY"] = positionY;
        return _context.PublishAsync(evt, ct);
    }

    public ValueTask NotifyWaypointCompletedAsync(int waypointGroupIndex, int waypointIndex, string? action, double positionX, double positionY, CancellationToken ct = default)
    {
        var evt = _context.CreateEvent<PathingWaypointCompletedEvent>();
        evt.WaypointGroupIndex = waypointGroupIndex;
        evt.WaypointIndex = waypointIndex;
        evt.Action = action;
        evt.PositionX = positionX;
        evt.PositionY = positionY;
        evt.Extra["positionX"] = positionX;
        evt.Extra["positionY"] = positionY;
        return _context.PublishAsync(evt, ct);
    }

    public ValueTask NotifyTeleportCompletedAsync(int waypointGroupIndex, int waypointIndex, double positionX, double positionY, CancellationToken ct = default)
    {
        var evt = _context.CreateEvent<PathingTeleportCompletedEvent>();
        evt.WaypointGroupIndex = waypointGroupIndex;
        evt.WaypointIndex = waypointIndex;
        evt.PositionX = positionX;
        evt.PositionY = positionY;
        evt.Extra["positionX"] = positionX;
        evt.Extra["positionY"] = positionY;
        return _context.PublishAsync(evt, ct);
    }

    public ValueTask NotifyActionCompletedAsync(int waypointGroupIndex, int waypointIndex, string action, double positionX, double positionY, CancellationToken ct = default)
    {
        var evt = _context.CreateEvent<PathingActionCompletedEvent>();
        evt.WaypointGroupIndex = waypointGroupIndex;
        evt.WaypointIndex = waypointIndex;
        evt.Action = action;
        evt.PositionX = positionX;
        evt.PositionY = positionY;
        evt.Extra["positionX"] = positionX;
        evt.Extra["positionY"] = positionY;
        return _context.PublishAsync(evt, ct);
    }

    public ValueTask NotifyResumePointUpdatedAsync(PathingGearTaskResumeState state, string? message = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(state.PathFile))
        {
            state.PathFile = _context.TaskPath;
        }

        var evt = _context.CreateEvent<ResumePointUpdatedEvent>();
        evt.CanResumeInsideTask = true;
        evt.ResumeTokenJson = JsonConvert.SerializeObject(state);
        evt.Message = message;
        return _context.PublishAsync(evt, ct);
    }
}
