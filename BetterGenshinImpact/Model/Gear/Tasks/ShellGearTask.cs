using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.Shell;

namespace BetterGenshinImpact.Model.Gear.Tasks;

public class ShellGearTask(ShellConfig? shellConfig) : BaseGearTask
{
    public override async Task Run()
    {
        var task = new ShellTask(ShellTaskParam.BuildFromConfig(Name, shellConfig ?? new ShellConfig()));
        await task.Start(CancellationContext.Instance.Cts.Token);
    }
}