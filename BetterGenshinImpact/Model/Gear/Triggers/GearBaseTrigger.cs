using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Tasks;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Model.Gear.Triggers;

public abstract class GearBaseTrigger
{
    /// <summary>
    /// 触发器设置名字
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 任务定义名称
    /// </summary>
    public string TaskDefinitionName { get; set; } = string.Empty;

    /// <summary>
    /// 执行任务
    /// </summary>
    public async Task Trigger()
    {
        var gearTaskExecutor = App.GetRequiredService<GearTaskExecutor>();
        await gearTaskExecutor.ExecuteTaskDefinitionAsync(TaskDefinitionName);
    }

}