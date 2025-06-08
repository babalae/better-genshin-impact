using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.Shell;

namespace BetterGenshinImpact.Model.Gear;

public class ShellGearTask : BaseGearTask
{
    public override async Task Run(params object[] configs)
    {
        ShellConfig? shellConfig = null;
        if (configs.Length > 0)
        {
            shellConfig = (ShellConfig)configs[0];
        }

        var task = new ShellTask(ShellTaskParam.BuildFromConfig(Name, shellConfig ?? new ShellConfig()));
        await task.Start(CancellationContext.Instance.Cts.Token);
    }
}