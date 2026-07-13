namespace BetterGenshinImpact.Service.Tavern.Model;

public sealed class MarkerItemLinkVo
{
    /// <summary>
    /// 关联物品ID
    /// BetterGenshinImpact.Service.Tavern.Model.ItemTypeVo.Id
    /// </summary>
    public long? ItemId { get; set; }

    public int? Count { get; set; }

    public long? IconId { get; set; }
}