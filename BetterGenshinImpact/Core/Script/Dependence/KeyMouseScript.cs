using BetterGenshinImpact.Core.Recorder;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// 向 JS 暴露键鼠宏执行能力，并固定使用本次脚本的取消令牌。
/// </summary>
public class KeyMouseScript(string rootPath, CancellationToken ct)
{
    public async Task Run(string json)
    {
        await KeyMouseMacroPlayer.PlayMacro(json, ct, false);
        ct.ThrowIfCancellationRequested();
    }

    public async Task RunFile(string path)
    {
        var json = await new LimitedFile(rootPath).ReadText(path);
        await Run(json);
    }
}
