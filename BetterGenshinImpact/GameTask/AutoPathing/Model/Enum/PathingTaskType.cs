using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

/// <summary>
/// Defines overarching task categories orchestrating complex waypoint navigation clusters.
/// 定义编排复杂路点导航集群的总体任务大类，用于上层任务调度与资源分配。
/// </summary>
/// <param name="code">The underlying type identification code. 底层任务类型标识代码。</param>
/// <param name="msg">The localized human-readable descriptor. 本地化任务描述符。</param>
public class PathingTaskType(string code, string msg)
{
    /// <summary>Resource gathering navigation profile. 资源采集导航作业。</summary>
    public static readonly PathingTaskType Collect = new("collect", "采集");
    
    /// <summary>Ore excavation navigation profile. 矿石挖掘导航作业。</summary>
    public static readonly PathingTaskType Mining = new("mining", "挖矿");
    
    /// <summary>Systematic enemy elimination navigation profile. 系统化巡图接敌作业（锄地）。</summary>
    public static readonly PathingTaskType Farming = new("farming", "锄地");

    /// <summary>
    /// Enumerates all officially registered comprehensive task categories.
    /// 枚举所有已正式注册的综合任务大类。
    /// </summary>
    public static IEnumerable<PathingTaskType> Values
    {
        get
        {
            yield return Collect;
            yield return Mining;
            yield return Farming;
        }
    }

    /// <summary>
    /// The structural code representing the task profile.
    /// 代表任务配置项的结构代码。
    /// </summary>
    public string Code { get; private set; } = code;
    
    /// <summary>
    /// The logging message identifying this task type.
    /// 标识该任务类型的描述消息。
    /// </summary>
    public string Msg { get; private set; } = msg;

    /// <summary>
    /// Safely resolves the localized display message corresponding to the task type.
    /// 安全解析该任务类型对应的本地化显示消息。
    /// </summary>
    /// <param name="code">The target task execution code. 目标任务执行代码。</param>
    /// <returns>The specified localized message or raw code if omitted. 指定的本地化消息，漏匹配则返回原始代码。</returns>
    public static string GetMsgByCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;

        foreach (var item in Values)
        {
            if (string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return item.Msg;
            }
        }
        return code;
    }
}
