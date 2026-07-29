using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Shell;

namespace BetterGenshinImpact.Model.Gear.Tasks;

public class ShellGearTask(ShellConfig? shellConfig) : BaseGearTask
{
    public override async Task Run(CancellationToken ct)
    {
        var task = new ShellTask(ShellTaskParam.BuildFromConfig(Name, shellConfig ?? new ShellConfig()));
        await task.Start(ct);
    }
}