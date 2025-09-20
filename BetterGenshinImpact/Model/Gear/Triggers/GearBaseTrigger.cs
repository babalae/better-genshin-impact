using System.Collections.ObjectModel;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Tasks;

namespace BetterGenshinImpact.Model.Gear.Triggers;

public abstract class GearBaseTrigger
{
    /// <summary>
    /// 触发器设置名字
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    public ObservableCollection<GearTaskRefence> GearTaskRefenceList { get; set; } = [];
    
    /// <summary>
    /// 执行任务
    /// </summary>
    public abstract Task Trigger();
}