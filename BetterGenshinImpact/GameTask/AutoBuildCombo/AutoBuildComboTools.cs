using CsTrees.MEAI;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// 行为树构建工具宿主
/// CsTrees.MEAI 源生成器会根据 AutoBuildComboBuilder 中的 Catalog 工厂方法
/// 自动生成对应的工具方法（含 End/BuildTree/ShowTreeStatus 等基类内置方法），
/// 聚合到 Tools 属性（Delegate[]）供 MEAI Agent 注册
/// </summary>
public partial class AutoBuildComboTools : BuildToolsBase<AutoBuildComboBuilder>
{
    public AutoBuildComboTools(AutoBuildComboBuilder builder) : base(builder)
    {
    }
}
