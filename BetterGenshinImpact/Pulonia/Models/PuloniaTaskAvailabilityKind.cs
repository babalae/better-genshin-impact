namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 能力资源的可用性规则类型。
/// </summary>
public enum PuloniaTaskAvailabilityKind
{
    /// <summary>
    /// 不限制重复执行。
    /// </summary>
    Unrestricted,

    /// <summary>
    /// 从确认事件发生时刻开始计算滚动冷却。
    /// </summary>
    RollingCooldown,

    /// <summary>
    /// 按服务器日、周或月窗口累计额度。
    /// </summary>
    ResetQuota
}
