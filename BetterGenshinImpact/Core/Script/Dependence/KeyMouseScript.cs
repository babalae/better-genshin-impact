using BetterGenshinImpact.Core.Recorder;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class KeyMouseScript(string rootPath)
{
    public async Task Run(string json)
    {
        await KeyMouseMacroPlayer.PlayMacro(json, ScriptCancellationContext.Token, false);
        ScriptCancellationContext.Token.ThrowIfCancellationRequested();
    }

    public async Task RunFile(string path)
    {
        var json = await new LimitedFile(rootPath).ReadText(path);
        await Run(json);
    }
}
