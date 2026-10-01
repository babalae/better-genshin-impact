namespace BetterGenshinImpact.Service.Model.Hutao;

// 与 Snap.Hutao.Remastered 的 AutomationInventoryItem 保持 JSON 形状一致。
internal sealed class AutomationInventoryItem
{
    public uint ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public uint Count { get; set; }
}
