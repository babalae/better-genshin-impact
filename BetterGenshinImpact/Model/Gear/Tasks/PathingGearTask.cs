using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.Model.Gear.Parameter;

namespace BetterGenshinImpact.Model.Gear.Tasks;

public class PathingGearTask : BaseGearTask
{

    private PathingGearTaskParams _params;

    public PathingGearTask(PathingGearTaskParams param)
    {
        FilePath = param.Path;
        _params = param;
    }
    
    public override async Task Run(CancellationToken ct)
    {
        // 加载并执行
        var task = PathingTask.BuildFromFilePath(_params.Path);
        var pathingTask = new PathExecutor(CancellationContext.Instance.Cts.Token);

        if (_params.PathingPartyConfig != null)
        {
            pathingTask.PartyConfig = _params.PathingPartyConfig;
        }

        if (pathingTask.PartyConfig.AutoPickEnabled)
        {
            TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null);
        }

        await pathingTask.Pathing(task);
    }
}