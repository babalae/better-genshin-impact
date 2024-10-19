using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class AutoPathingScript(string rootPath)
{
    public async Task Run(string json)
    {
        var task = PathingTask.BuildFromJson(json);
        await new PathExecutor(CancellationContext.Instance.Cts.Token).Pathing(task);
    }

    public async Task RunFile(string path)
    {
        var json = await new LimitedFile(rootPath).ReadText(path);
        await Run(json);
    }
}
