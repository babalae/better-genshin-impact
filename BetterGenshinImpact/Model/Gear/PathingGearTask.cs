using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.Model.Gear;

public class PathingGearTask : BaseGearTask
{
    public PathingGearTask(string path)
    {
        FilePath = path;
    }

    public override async Task Run(params object[] configs)
    {
        // 加载并执行
        var task = PathingTask.BuildFromFilePath(FilePath);
        var pathingTask = new PathExecutor(CancellationContext.Instance.Cts.Token);
        if (configs.Length > 0)
        {
            pathingTask.PartyConfig = (PathingPartyConfig)configs[0];
        }
        if (pathingTask.PartyConfig.AutoPickEnabled)
        {
            TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null);
        }
        await pathingTask.Pathing(task);
    }
}