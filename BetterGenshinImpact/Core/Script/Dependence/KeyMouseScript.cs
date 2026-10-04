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
        // 登记本轮 JS 宿主操作；脚本停止后必须等待业务方法真正退出再释放游戏所有权。
        using var scriptOperation = ScriptHostOperations.Enter();
        await KeyMouseMacroPlayer.PlayMacro(json, ct, false);
        ct.ThrowIfCancellationRequested();
    }

    public async Task RunFile(string path)
    {
        using var scriptOperation = ScriptHostOperations.Enter();
        var json = await new LimitedFile(rootPath).ReadText(path);
        await Run(json);
    }
}
