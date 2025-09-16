using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

/// <summary>
/// 模仿Java实现的多属性枚举类
/// 按形态大类分类的原神鱼类枚举
/// </summary>
public class BigFishType
{
    public static readonly BigFishType Medaka = new("medaka", BaitType.FruitPasteBait, "花鳉", 0);
    public static readonly BigFishType LargeMedaka = new("large medaka", BaitType.FruitPasteBait, "大花鳉", 1);
    public static readonly BigFishType Stickleback = new("stickleback", BaitType.RedrotBait, "棘鱼", 2);
    public static readonly BigFishType Koi = new("koi", BaitType.FakeFlyBait, "假龙", 3);
    public static readonly BigFishType KoiHead = new("koi head", BaitType.FakeFlyBait, "假龙头", 3);
    public static readonly BigFishType Butterflyfish = new("butterflyfish", BaitType.FalseWormBait, "蝶鱼", 4);
    public static readonly BigFishType Pufferfish = new("pufferfish", BaitType.FakeFlyBait, "炮鲀", 5);

    public static readonly BigFishType Ray = new("ray", BaitType.FakeFlyBait, "鳐", 6);

    // public static readonly BigFishType FormaloRay = new("formalo ray", "飞蝇假饵", "佛玛洛鳐");
    // public static readonly BigFishType DivdaRay = new("divda ray", "飞蝇假饵", "迪芙妲鳐");
    public static readonly BigFishType Angler = new("angler", BaitType.SugardewBait, "角鲀", 7);
    public static readonly BigFishType AxeMarlin = new("axe marlin", BaitType.SugardewBait, "斧枪鱼", 8);
    public static readonly BigFishType HeartfeatherBass = new("heartfeather bass", BaitType.SourBait, "心羽鲈", 9);
    public static readonly BigFishType MaintenanceMek = new("maintenance mek", BaitType.FlashingMaintenanceMekBait, "维护机关", 10);
    public static readonly BigFishType Unihornfish = new("unihornfish", BaitType.SpinelgrainBait, "独角鱼", 10);
    public static readonly BigFishType Sunfish = new("sunfish", BaitType.SpinelgrainBait, "翻车鲀", 7);
    public static readonly BigFishType Rapidfish = new("rapidfish", BaitType.SpinelgrainBait, "斗士急流鱼", 9);
    public static readonly BigFishType PhonyUnihornfish = new("phony unihornfish", BaitType.EmberglowBait, "燃素独角鱼", 10);
    public static readonly BigFishType MagmaRapidfish = new("magma rapidfish", BaitType.EmberglowBait, "炽岩斗士急流鱼", 9);
    public static readonly BigFishType SecretSourceScoutSweeper = new ("secret source scout sweeper", BaitType.EmberglowBait, "秘源机关・巡戒使", 9);

    public static readonly BigFishType MaulerShark = new ("mauler shark", BaitType.RefreshingLakkaBait, "凶凶鲨", 9);
    public static readonly BigFishType CrystalEye = new("crystal eye", BaitType.RefreshingLakkaBait, "明眼鱼", 9);
    public static readonly BigFishType AxeheadFish = new ("axehead fish", BaitType.BerryBait, "巨斧鱼", 9);

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