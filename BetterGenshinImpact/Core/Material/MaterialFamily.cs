using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Material;

/// <summary>独立于秘境与库存业务的单向材料升级链。</summary>
/// <param name="Id">以最低阶物品标识建立的稳定家族标识。</param>
/// <param name="Members">按低阶到高阶排列的标准物品名称。</param>
/// <param name="AllowsCraftingBonus">是否属于已核实的角色合成收益适用类别。</param>
public sealed record MaterialFamily(string Id, IReadOnlyList<string> Members, bool AllowsCraftingBonus)
{
    /// <summary>与成员名称同序的模型物品标识。</summary>
    public IReadOnlyList<string> MemberIds { get; init; } = [];
    /// <summary>模型字典中的材料分类。</summary>
    public string Category { get; init; } = "";
    /// <summary>每条相邻升级配方消耗的低阶材料数量。</summary>
    public int InputCount { get; init; } = 3;
    /// <summary>每条相邻升级配方产出的高阶材料数量。</summary>
    public int OutputCount { get; init; } = 1;
    /// <summary>按低到高相邻升级顺序记录的单次摩拉费用，不由数量判断自动抵扣。</summary>
    public IReadOnlyList<int> MoraCosts { get; init; } = [];
}
