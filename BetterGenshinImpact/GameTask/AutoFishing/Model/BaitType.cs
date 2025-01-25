using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

public class BaitType
{

    public static readonly BaitType FruitPasteBait = new("fruit paste bait", "果酿饵");
    public static readonly BaitType RedrotBait = new("redrot bait", "赤糜饵");
    public static readonly BaitType FalseWormBait = new("false worm bait", "蠕虫假饵");
    public static readonly BaitType FakeFlyBait = new("fake fly bait", "飞蝇假饵");
    public static readonly BaitType SugardewBait = new("sugardew bait", "甘露饵");
    public static readonly BaitType SourBait = new("sour bait", "酸桔饵");
    public static readonly BaitType FlashingMaintenanceMekBait = new("flashing maintenance mek bait", "维护机关频闪诱饵");

    public static IEnumerable<BaitType> Values
    {
        get
        {
            yield return FruitPasteBait;
            yield return RedrotBait;
            yield return FalseWormBait;
            yield return FakeFlyBait;
            yield return SugardewBait;
            yield return SourBait;
            yield return FlashingMaintenanceMekBait;
        }
    }
    public string Name { get; private set; }
    public string ChineseName { get; private set; }

    private BaitType(string name, string chineseName)
    {
        Name = name;
        ChineseName = chineseName;
    }

    public static BaitType FromName(string name)
    {
        foreach (var type in Values)
        {
            if (type.Name == name)
            {
                return type;
            }
        }

        throw new KeyNotFoundException($"BaitType {name} not found");
    }
}