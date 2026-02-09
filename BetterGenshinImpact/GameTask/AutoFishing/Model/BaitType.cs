using System.ComponentModel;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

public enum BaitType
{
    [Description(Lang.S["GameTask_10792_426fae"])]
    FruitPasteBait,
    [Description(Lang.S["GameTask_10791_b90580"])]
    RedrotBait,
    [Description(Lang.S["GameTask_10790_68f322"])]
    FalseWormBait,
    [Description(Lang.S["GameTask_10789_096261"])]
    FakeFlyBait,
    [Description(Lang.S["GameTask_10788_9ef271"])]
    SugardewBait,
    [Description(Lang.S["GameTask_10787_f831db"])]
    SourBait,
    [Description(Lang.S["GameTask_10786_03c246"])]
    FlashingMaintenanceMekBait,
    [Description(Lang.S["GameTask_10785_69d669"])]
    SpinelgrainBait,
    [Description(Lang.S["GameTask_10784_bf3a09"])]
    EmberglowBait,
    [Description(Lang.S["GameTask_10783_1f602c"])]
    BerryBait,
    [Description(Lang.S["GameTask_10782_2959f2"])]
    RefreshingLakkaBait
}