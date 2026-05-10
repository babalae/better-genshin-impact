using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.Model.Gear.Parameter;
using BetterGenshinImpact.Service.GearTask.Execution;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Model.Gear.Tasks;

public class PathingGearTask : BaseGearTask, IGearTaskResumable
{
    private readonly PathingGearTaskParams _params;
    private PathingGearTaskResumeState? _resumeState;

    public PathingGearTask(PathingGearTaskParams param)
    {
        FilePath = param.Path;
        _params = param;
    }

    public override async Task Run(CancellationToken ct)
    {
        var task = BetterGenshinImpact.GameTask.AutoPathing.Model.PathingTask.BuildFromFilePath(_params.Path);
        var pathExecutor = new PathExecutor(ct);

        if (_params.PathingPartyConfig != null)
        {
            pathExecutor.PartyConfig = _params.PathingPartyConfig;
        }

        if (pathExecutor.PartyConfig.AutoPickEnabled)
        {
            TaskTriggerDispatcher.Instance().AddTrigger("AutoPick", null);
        }

        pathExecutor.RuntimeNotifier = ExecutionContext != null
            ? new GearTaskPathingRuntimeNotifier(ExecutionContext)
            : NullPathingRuntimeNotifier.Instance;

        if (_resumeState != null)
        {
            pathExecutor.ApplyResumeState(_resumeState);
        }

        await pathExecutor.Pathing(task);
    }

    public Task ApplyResumeTokenAsync(string? resumeTokenJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(resumeTokenJson))
        {
            _resumeState = null;
            return Task.CompletedTask;
        }

        _resumeState = JsonConvert.DeserializeObject<PathingGearTaskResumeState>(resumeTokenJson);
        return Task.CompletedTask;
    }
}
