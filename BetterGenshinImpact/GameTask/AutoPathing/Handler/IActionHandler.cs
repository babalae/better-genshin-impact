using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 定义路径执行过程中的动作处理器 / Defines the action handler for path execution.
/// </summary>
public interface IActionHandler
{
    /// <summary>
    /// 是否覆盖（跳过）默认的 LocoMotion 移动行为 / Whether this action overrides the default locomotion behavior.
    /// 当返回 true 时，寻路引擎将不执行默认的 MoveTo 移动，交由 Handler 自行控制角色的空间位移。
    /// </summary>
    bool OverridesLocomotion => false;

    /// <summary>
    /// 异步执行具体的动作逻辑 / Asynchronously executes the specific action logic.
    /// </summary>
    /// <param name="ct">取消令牌 / Cancellation token.</param>
    /// <param name="waypointForTrack">触发该动作的路点信息 / Waypoint information triggering the action.</param>
    /// <param name="config">可选的附加配置对象 / Optional additional configuration object.</param>
    /// <returns>异步任务结果 / Asynchronous task result.</returns>
    Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null);
}
