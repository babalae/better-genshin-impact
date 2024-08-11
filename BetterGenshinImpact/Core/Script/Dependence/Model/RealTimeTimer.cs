namespace BetterGenshinImpact.Core.Script.Dependence.Model;

/// <summary>
/// 实时任务计时器
/// </summary>
public class RealTimeTimer
{
    /// <summary>
    /// 实时任务触发器名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 实时任务触发器时间间隔
    /// 默认50ms
    /// </summary>
    public int Interval { get; set; } = 50;

    /// <summary>
    /// 实时任务配置
    /// </summary>
    public object? Config;
}
