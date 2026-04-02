using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;

/// <summary>
/// 攀爬模式处理器 
/// Wall traversal handler (currently bypasses generic action simulation).
/// </summary>
public class ClimbMoveModeHandler : IMoveModeHandler
{
    public bool CanHandle(string moveModeCode) => moveModeCode == MoveModeEnum.Climb.Code;

    public Task<MoveModeResult> ExecuteAsync(WaypointForTrack waypoint, PathingMovementContext context)
    {
        // 攀爬状态下没有额外的模拟按键，直接往下放行 / Pass immediately for climbing
        return Task.FromResult(MoveModeResult.Pass);
    }
}
