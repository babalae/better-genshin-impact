namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 配置组地图追踪的拾取策略。
/// </summary>
public enum PathingPickupMode
{
    /// <summary>原版 AutoPick（OCR + 全局黑白名单）</summary>
    AutoPick = 0,

    /// <summary>只拾取圣遗物（物品名模板匹配）</summary>
    ArtifactOnly = 1,

    /// <summary>不自动拾取</summary>
    Disabled = 2
}
