using BetterGenshinImpact.Core.Recorder;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class KeyMouseScript(string rootPath)
{
    public async Task Run(string json)
    {
        await KeyMouseMacroPlayer.PlayMacro(json, CancellationContext.Instance.Cts.Token);
    }

    public async Task RunFile(string path)
    {
        var json = await new LimitedFile(rootPath).ReadText(path);
        await Run(json);
    }
}
