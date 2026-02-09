using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

/// <summary>
/// 模仿Java实现的多属性枚举类
/// 按形态大类分类的原神鱼类枚举
/// </summary>
public class BigFishType
{
    public static readonly BigFishType Medaka = new("medaka", BaitType.FruitPasteBait, Lang.S["GameTask_10812_547c2c"], 0);
    public static readonly BigFishType LargeMedaka = new("large medaka", BaitType.FruitPasteBait, Lang.S["GameTask_10811_c23bf7"], 1);
    public static readonly BigFishType Stickleback = new("stickleback", BaitType.RedrotBait, Lang.S["GameTask_10810_09f554"], 2);
    public static readonly BigFishType Koi = new("koi", BaitType.FakeFlyBait, Lang.S["GameTask_10809_22c6e6"], 3);
    public static readonly BigFishType KoiHead = new("koi head", BaitType.FakeFlyBait, Lang.S["GameTask_10808_fedd8e"], 3);
    public static readonly BigFishType Butterflyfish = new("butterflyfish", BaitType.FalseWormBait, Lang.S["GameTask_10807_8009bd"], 4);
    public static readonly BigFishType Pufferfish = new("pufferfish", BaitType.FakeFlyBait, Lang.S["GameTask_10806_9b3a18"], 5);

    public static readonly BigFishType Ray = new("ray", BaitType.FakeFlyBait, "鳐", 6);

    // public static readonly BigFishType FormaloRay = new("formalo ray", "飞蝇假饵", "佛玛洛鳐");
    // public static readonly BigFishType DivdaRay = new("divda ray", "飞蝇假饵", "迪芙妲鳐");
    public static readonly BigFishType Angler = new("angler", BaitType.SugardewBait, Lang.S["GameTask_10805_6d2a69"], 7);
    public static readonly BigFishType AxeMarlin = new("axe marlin", BaitType.SugardewBait, Lang.S["GameTask_10804_ea602a"], 8);
    public static readonly BigFishType HeartfeatherBass = new("heartfeather bass", BaitType.SourBait, Lang.S["GameTask_10803_ef8456"], 9);
    public static readonly BigFishType MaintenanceMek = new("maintenance mek", BaitType.FlashingMaintenanceMekBait, Lang.S["GameTask_10802_e361a9"], 10);
    public static readonly BigFishType Unihornfish = new("unihornfish", BaitType.SpinelgrainBait, Lang.S["GameTask_10801_38a0e6"], 10);
    public static readonly BigFishType Sunfish = new("sunfish", BaitType.SpinelgrainBait, Lang.S["GameTask_10800_02ba06"], 7);
    public static readonly BigFishType Rapidfish = new("rapidfish", BaitType.SpinelgrainBait, Lang.S["GameTask_10799_5009f5"], 9);
    public static readonly BigFishType PhonyUnihornfish = new("phony unihornfish", BaitType.EmberglowBait, Lang.S["GameTask_10798_5eefba"], 10);
    public static readonly BigFishType MagmaRapidfish = new("magma rapidfish", BaitType.EmberglowBait, Lang.S["GameTask_10797_bc8cef"], 9);
    public static readonly BigFishType SecretSourceScoutSweeper = new ("secret source", BaitType.EmberglowBait, Lang.S["GameTask_10796_b8ce9a"], 9);

    public static readonly BigFishType MaulerShark = new ("mauler shark", BaitType.RefreshingLakkaBait, Lang.S["GameTask_10795_7c1b88"], 9);
    public static readonly BigFishType CrystalEye = new("crystal eye", BaitType.RefreshingLakkaBait, Lang.S["GameTask_10794_bc1a69"], 9);
    public static readonly BigFishType AxeheadFish = new ("axehead", BaitType.BerryBait, Lang.S["GameTask_10793_f4fe27"], 9);

    public static IEnumerable<BigFishType> Values
    {
        get
        {
            yield return Medaka;
            yield return LargeMedaka;
            yield return Stickleback;
            yield return Koi;
            yield return KoiHead;
            yield return Butterflyfish;
            yield return Pufferfish;
            yield return Ray;
            // yield return FormaloRay;
            // yield return DivdaRay;
            yield return Angler;
            yield return AxeMarlin;
            yield return HeartfeatherBass;
            yield return MaintenanceMek;
            yield return Unihornfish;
            yield return Sunfish;
            yield return Rapidfish;
            yield return PhonyUnihornfish;
            yield return MagmaRapidfish;
            yield return SecretSourceScoutSweeper;
            yield return MaulerShark;
            yield return CrystalEye;
            yield return AxeheadFish;
        }
    }

    public string Name { get; private set; }
    public BaitType BaitType { get; private set; }
    public string ChineseName { get; private set; }

    public int NetIndex { get; private set; }

    private BigFishType(string name, BaitType baitType, string chineseName, int netIndex)
    {
        Name = name;
        BaitType = baitType;
        ChineseName = chineseName;
        NetIndex = netIndex;
    }

    public static BigFishType FromName(string name)
    {
        foreach (var fishType in Values)
        {
            if (fishType.Name == name)
            {
                return fishType;
            }
        }

        throw new KeyNotFoundException($"BigFishType {name} not found");
    }

    public static int GetIndex(BigFishType e)
    {
        return e.NetIndex;
    }
}
