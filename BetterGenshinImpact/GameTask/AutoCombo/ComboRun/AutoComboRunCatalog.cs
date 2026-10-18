using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Runtime;
using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboRun;

/// <summary>
/// 连招行为树测试专用目录
/// 仅挂载在 AutoComboRunBuilder 上，不进入 AutoComboBuildBuilder 的 LLM 建树词汇表
/// </summary>
/// <param name="runtime">本次运行所在的运行环境，传给需要截图的节点</param>
/// <param name="config">配置根</param>
/// <param name="avatars">队伍成员</param>
public class AutoComboRunCatalog(GameRuntime runtime, AllConfig config, Avatar[] avatars) : IBehaviourCatalog
{
    public CheckFightFinish CheckFightFinish(string name) => new(runtime, config, name, avatars);
}
