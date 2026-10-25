namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>合成浓缩树脂的结果数据。</summary>
public sealed class CraftCondensedResinData
{
    /// <summary>合成份数；按默认数量合成且无法识别数量时为 null。</summary>
    public int? CraftedCount { get; set; }
}
