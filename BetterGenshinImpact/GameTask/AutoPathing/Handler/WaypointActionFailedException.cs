using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 表示路点动作没有完成其承诺的游戏状态。PathExecutor 会将其转换为明确的路线失败。
/// </summary>
public sealed class WaypointActionFailedException(string message) : HandledException(message);
