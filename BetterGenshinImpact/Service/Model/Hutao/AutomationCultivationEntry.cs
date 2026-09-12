using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Model.Hutao;

// 与 Snap.Hutao.Remastered 的 AutomationCultivationEntry 保持 JSON 形状一致。
internal sealed class AutomationCultivationEntry
{
    public uint ItemId { get; set; }

    public List<AutomationCultivationItem> Items { get; set; } = [];
}
