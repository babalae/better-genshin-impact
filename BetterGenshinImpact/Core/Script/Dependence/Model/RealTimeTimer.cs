using System;
using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.GameTask.AutoSkip;
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
    public object? Config;

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
        if (Name == "AutoPick")
        {
            Config = ScriptObjectConverter.ConvertTo<AutoPickExternalConfig>(config);
        }
        else if (Name == "AutoSkip")
        {
            if (config is AutoSkipConfig)
            {
                Config = config;
            }
            else 
            {
                throw new ArgumentException("AutoSkip的配置参数需要为AutoSkipConfig类型");
            }
        }
    }
}
