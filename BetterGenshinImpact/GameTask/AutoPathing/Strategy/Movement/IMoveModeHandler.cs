using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;

/// <summary>
/// 策略模式：寻路移位动作接口 
/// Defines the specific behavior to execute for a generic or special locomotion pattern.
/// </summary>
public interface IMoveModeHandler
{
    /// <summary>
    /// 判断是否支持当前传入的移动模式代码
    /// Determines whether the handler can process the given mode code.
    /// </summary>
    bool CanHandle(string moveModeCode);
    
    /// <summary>
    /// 处理移动相关逻辑的异步操作
    /// Executes the move operation and handles keypress simulations.
    /// </summary>
    /// <returns>返回状态标识：是否跳过本轮（Continue）、通过（Pass）还是直接终止（ReturnFalse）</returns>
    Task<MoveModeResult> ExecuteAsync(WaypointForTrack waypoint, PathingMovementContext context);
}
