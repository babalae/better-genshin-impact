using CsTrees.MEAI;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 兜底攻击行为树构建工具宿主
/// CsTrees.MEAI 源生成器会根据 AutoComboBuildFallbackBuilder 中的 Catalog 工厂方法
/// 自动生成对应的工具方法（含 End/BuildTree/ShowTreeStatus 等基类内置方法），
/// 聚合到 Tools 属性（Delegate[]）供 MEAI Agent 注册
/// </summary>
/// <param name="builder">兜底行为树构建器，节点工厂类工具经其操作树</param>
public partial class AutoComboBuildFallbackTools(AutoComboBuildFallbackBuilder builder)
    : BuildToolsBase<AutoComboBuildFallbackBuilder>(builder)
{
}
