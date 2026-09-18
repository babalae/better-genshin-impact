using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Model.Hutao;

// 与 Snap.Hutao.Remastered 的 AutomationCultivationItem 保持 JSON 形状一致。
internal sealed class AutomationCultivationItem
{
    public uint ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public uint Count { get; set; }

    public uint RankLevel { get; set; }

    public List<string> Monsters { get; set; } = [];
}
