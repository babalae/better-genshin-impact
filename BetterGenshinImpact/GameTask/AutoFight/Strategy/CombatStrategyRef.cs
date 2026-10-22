namespace BetterGenshinImpact.GameTask.AutoFight.Strategy;

/// <summary>解析后的战斗策略引用。</summary>
/// <param name="Kind">单一策略类型。</param>
/// <param name="Path">执行用绝对路径；自动连招为固定名称；越界或类型错误的引用保留原输入。</param>
/// <param name="Value">能唯一定位策略的规范引用值。</param>
/// <param name="DisplayName">供界面显示的名称。</param>
/// <param name="Exists">文件或目录是否存在；连招恒为 true。</param>
public sealed record CombatStrategyRef(
    CombatStrategyKind Kind, string Path, string Value, string DisplayName, bool Exists);
