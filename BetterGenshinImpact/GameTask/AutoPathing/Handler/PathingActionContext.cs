using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

/// <summary>
/// 路径动作处理器的强类型执行上下文。
/// </summary>
public sealed record PathingActionContext(
    PathExecutor? Executor = null,
    PathingPartyConfig? PartyConfig = null);
