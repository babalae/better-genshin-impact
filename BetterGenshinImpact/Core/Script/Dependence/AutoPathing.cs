using System.Threading.Tasks;
using System.Text.Json;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.Core.Script.Dependence;

internal class AutoPathing(string rootPath)
{
    public async Task Run(string json)
    {
        var task = JsonSerializer.Deserialize<PathingTask>(json);
        if (task == null)
        {
            return;
        }
        await new PathExecutor(CancellationContext.Instance.Cts).Pathing(task);
    }

    public async Task RunFile(string path)
    {
        var json = await new LimitedFile(rootPath).ReadText(path);
        await Run(json);
    }
}
