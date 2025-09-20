using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Model.Gear.Triggers;

/// <summary>
/// 基于 Quartz.NET 的定时触发器
/// </summary>
public class QuartzCronGearTrigger : GearBaseTrigger
{
    private readonly ILogger<QuartzCronGearTrigger> _logger = App.GetLogger<QuartzCronGearTrigger>();

    /// <summary>
    /// 是否在执行时中断其他同类型定时任务
    /// </summary>
    public bool ShouldInterruptOthers { get; set; } = true;

    /// <summary>
    /// 使用 Cron 表达式（如果设置，将覆盖 IntervalMs 设置）
    /// </summary>
    public string? CronExpression { get; set; }

    public override async Task Trigger()
    {
        
    }
}