using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Model.Hutao;

// 与 Snap.Hutao.Remastered 的 AutomationCultivationProject 保持 JSON 形状一致。
internal sealed class AutomationCultivationProject
{
    public List<AutomationCultivationEntry> Entries { get; set; } = [];

    public List<AutomationInventoryItem> InventoryItems { get; set; } = [];
}
