using System;
using System.Diagnostics;
using System.Threading.Tasks;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Model.Gear.Triggers;

/// <summary>
/// 基于 Quartz.NET 的定时触发器
/// </summary>
public class QuartzCronGearTrigger : GearBaseTrigger
{
    /// <summary>
    /// 使用 Cron 表达式（如果设置，将覆盖 IntervalMs 设置）
    /// </summary>
    public string? CronExpression { get; set; }
}