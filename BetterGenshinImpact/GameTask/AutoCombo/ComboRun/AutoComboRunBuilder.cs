using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Runtime;
using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboRun;

/// <summary>
/// 连招行为树测试用流式构建器
/// 仅用于手工扩展树，不参与 LLM 建树工具生成
/// </summary>
public partial class AutoComboRunBuilder : TreeBuilder<AutoComboRunBuilder>
{
    private static readonly CompositesCatalog compositesCatalog = new();

    private readonly AutoComboRunCatalog autoComboRunCatalog;

    /// <summary>
    /// 创建构建器
    /// </summary>
    /// <param name="runtime">本次运行所在的运行环境</param>
    /// <param name="config">配置根</param>
    /// <param name="avatars">队伍成员</param>
    public AutoComboRunBuilder(GameRuntime runtime, AllConfig config, Avatar[] avatars)
    {
        autoComboRunCatalog = new AutoComboRunCatalog(runtime, config, avatars);
    }
}
