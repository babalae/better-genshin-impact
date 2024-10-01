using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Core.Script.Dependence.Model;

/// <summary>
/// 实时任务触发器
/// </summary>
public class RealtimeTimer
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
    public AutoPickExternalConfig? Config;

    public RealtimeTimer()
    {
    }

    public RealtimeTimer(string name)
    {
        Name = name;
    }

    public RealtimeTimer(string name, dynamic config)
    {
        Name = name;
        Config = ScriptObjectConverter.ConvertTo<AutoPickExternalConfig>(config);
    }
}
