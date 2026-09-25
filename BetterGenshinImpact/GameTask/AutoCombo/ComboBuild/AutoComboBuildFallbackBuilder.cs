using BetterGenshinImpact.GameTask.AutoFight.Model;
using CsTrees.FluentBuilder;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 兜底攻击行为树构建器
/// 与 AutoComboBuildBuilder 分离：词汇表只含基础动作与 Sequence，供 LLM 构建生成技能全不可用时的兜底输出序列
/// </summary>
public partial class AutoComboBuildFallbackBuilder : TreeBuilder<AutoComboBuildFallbackBuilder>
{
    private readonly AutoComboBuildFallbackCatalog fallbackCatalog;

    public AutoComboBuildFallbackBuilder(Avatar[] avatars)
    {
        fallbackCatalog = new AutoComboBuildFallbackCatalog(avatars);
    }
}