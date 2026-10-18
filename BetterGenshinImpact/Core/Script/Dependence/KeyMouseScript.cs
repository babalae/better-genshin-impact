using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.GameTask.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// 向 JS 暴露键鼠宏执行能力，并固定使用本次脚本的运行环境与取消令牌。
/// </summary>
/// <param name="runtime">本次脚本运行使用的运行环境，键鼠宏在它上面重放</param>
/// <param name="rootPath">脚本工作目录</param>
/// <param name="ct">当前脚本执行的取消令牌</param>
public class KeyMouseScript(GameRuntime runtime, string rootPath, CancellationToken ct)
{
    public async Task Run(string json)
    {
        // 登记本轮 JS 宿主操作；脚本停止后必须等待业务方法真正退出再释放游戏所有权。
        using var scriptOperation = ScriptHostOperations.Enter();
        await KeyMouseMacroPlayer.PlayMacro(runtime, json, ct, false);
        ct.ThrowIfCancellationRequested();
    }

    public async Task RunFile(string path)
    {
        using var scriptOperation = ScriptHostOperations.Enter();
        var json = await new LimitedFile(rootPath).ReadText(path);
        await Run(json);
    }
}
