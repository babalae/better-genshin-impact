using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Tasks;

namespace BetterGenshinImpact.Model.Gear.Triggers;

/// <summary>
/// 直接顺序执行的触发器
/// </summary>
public class SequentialGearTrigger : GearBaseTrigger
{
    public override async Task Run()
    {
        List<BaseGearTask> list = GearTaskRefenceList.Select(gearTask => gearTask.ToGearTask()).ToList();
        foreach (var gearTask in list)
        {
            await gearTask.Run();
        }
    }
}