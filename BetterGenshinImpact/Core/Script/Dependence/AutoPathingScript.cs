using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;

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
    
    /// <summary>
    /// 从已订阅的内容中获取文件
    /// </summary>
    /// <param name="path">在 `\User\AutoPathing` 目录下获取文件</param>
    public async Task RunFileFromUser(string path)
    {
        var json = await new LimitedFile(Global.Absolute(@"User\AutoPathing")).ReadText(path);
        await Run(json);
    }
}
