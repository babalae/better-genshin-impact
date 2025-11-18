using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

public enum BaitType
{
    [Description("果酿饵")]
    FruitPasteBait,
    [Description("赤糜饵")]
    RedrotBait,
    [Description("蠕虫假饵")]
    FalseWormBait,
    [Description("飞蝇假饵")]
    FakeFlyBait,
    [Description("甘露饵")]
    SugardewBait,
    [Description("酸桔饵")]
    SourBait,
    [Description("维护机关频闪诱饵")]
    FlashingMaintenanceMekBait,
    [Description("澄晶果粒饵")]
    SpinelgrainBait,
    [Description("温火饵")]
    EmberglowBait,
    [Description("槲梭饵")]
    BerryBait,
    [Description("清白饵")]
    RefreshingLakkaBait
}