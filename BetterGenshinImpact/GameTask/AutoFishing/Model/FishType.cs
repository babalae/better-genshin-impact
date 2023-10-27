using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

/// <summary>
/// 模仿Java实现的多属性枚举类
/// </summary>
public class FishType
{
    public static readonly FishType AizenMedaka = new("aizen medaka", "fruit paste bait", "蓝染花鳉");
    public static readonly FishType Crystalfish = new("crystalfish", "fruit paste bait", "水晶宴");
    public static readonly FishType Dawncatcher = new("dawncatcher", "fruit paste bait", "擒霞客");
    public static readonly FishType GlazeMedaka = new("glaze medaka", "fruit paste bait", "琉璃花鳉");
    public static readonly FishType Medaka = new("medaka", "fruit paste bait", "花鳉");
    public static readonly FishType SweetFlowerMedaka = new("sweet-flower medaka", "fruit paste bait", "甜甜花鳉");
    public static readonly FishType AkaiMaou = new("akai maou", "redrot bait", "赤魔王");
    public static readonly FishType Betta = new("betta", "redrot bait", "斗棘鱼");
    public static readonly FishType LungedStickleback = new("lunged stickleback", "redrot bait", "肺棘鱼");
    public static readonly FishType Snowstrider = new("snowstrider", "redrot bait", "雪中君");
    public static readonly FishType VenomspineFish = new("venomspine fish", "redrot bait", "鸩棘鱼");
    public static readonly FishType AbidingAngelfish = new("abiding angelfish", "false worm bait", "长生仙");
    public static readonly FishType BrownShirakodai = new("brown shirakodai", "false worm bait", "流纹褐蝶鱼");
    public static readonly FishType PurpleShirakodai = new("purple shirakodai", "false worm bait", "流纹京紫蝶鱼");
    public static readonly FishType RaimeiAngelfish = new("raimei angelfish", "false worm bait", "雷鸣仙");
    public static readonly FishType TeaColoredShirakodai = new("tea-colored shirakodai", "false worm bait", "流纹茶蝶鱼");
    public static readonly FishType BitterPufferfish = new("bitter pufferfish", "fake fly bait", "苦炮鲀");
    public static readonly FishType DivdaRay = new("divda ray", "fake fly bait", "迪芙妲鳐");
    public static readonly FishType FormaloRay = new("formalo ray", "fake fly bait", "佛玛洛鳐");
    public static readonly FishType GoldenKoi = new("golden koi", "fake fly bait", "金赤假龙");
    public static readonly FishType Pufferfish = new("pufferfish", "fake fly bait", "苦炮鲀2");
    public static readonly FishType RustyKoi = new("rusty koi", "fake fly bait", "锖假龙");
    public static readonly FishType HalcyonJadeAxeMarlin = new("halcyon jade axe marlin", "sugardew bait", "翡玉斧枪鱼");
    public static readonly FishType LazuriteAxeMarlin = new("lazurite axe marlin", "sugardew bait", "青金斧枪鱼");
    public static readonly FishType PeachOfTheDeepWaves = new("peach of the deep waves", "sugardew bait", "沉波蜜桃");
    public static readonly FishType SandstormAngler = new("sandstorm angler", "sugardew bait", "吹沙角鲀");
    public static readonly FishType StreamingAxeMarlin = new("streaming axe marlin", "sugardew bait", "海涛斧枪鱼");
    public static readonly FishType SunsetCloudAngler = new("sunset cloud angler", "sugardew bait", "暮云角鲀");
    public static readonly FishType TrueFruitAngler = new("true fruit angler", "sugardew bait", "真果角鲀");
    public static readonly FishType BlazingHeartfeatherBass = new("blazing heartfeather bass", "sour bait", "烘烘心羽鲈");
    public static readonly FishType RipplingHeartfeatherBass = new("rippling heartfeather bass", "sour bait", "波波心羽鲈");
    public static readonly FishType MaintenanceMekInitialConfiguration = new("maintenance mek: initial configuration", "flashing maintenance mek bait", "维护机关·初始能力型");
    public static readonly FishType MaintenanceMekPlatinumCollection = new("maintenance mek: platinum collection", "flashing maintenance mek bait", "维护机关·白金典藏型");
    public static readonly FishType MaintenanceMekSituationController = new("maintenance mek: situation controller", "flashing maintenance mek bait", "维护机关·态势控制者");
    public static readonly FishType MaintenanceMekWaterBodyCleaner = new("maintenance mek: water body cleaner", "flashing maintenance mek bait", "维护机关·水域清理者");

    public static IEnumerable<FishType> Values
    {
        get
        {
            yield return AizenMedaka;
            yield return Crystalfish;
            yield return Dawncatcher;
            yield return GlazeMedaka;
            yield return Medaka;
            yield return SweetFlowerMedaka;
            yield return AkaiMaou;
            yield return Betta;
            yield return LungedStickleback;
            yield return Snowstrider;
            yield return VenomspineFish;
            yield return AbidingAngelfish;
            yield return BrownShirakodai;
            yield return PurpleShirakodai;
            yield return RaimeiAngelfish;
            yield return TeaColoredShirakodai;
            yield return BitterPufferfish;
            yield return DivdaRay;
            yield return FormaloRay;
            yield return GoldenKoi;
            yield return Pufferfish;
            yield return RustyKoi;
            yield return HalcyonJadeAxeMarlin;
            yield return LazuriteAxeMarlin;
            yield return PeachOfTheDeepWaves;
            yield return SandstormAngler;
            yield return StreamingAxeMarlin;
            yield return SunsetCloudAngler;
            yield return TrueFruitAngler;
            yield return BlazingHeartfeatherBass;
            yield return RipplingHeartfeatherBass;
            yield return MaintenanceMekInitialConfiguration;
            yield return MaintenanceMekPlatinumCollection;
            yield return MaintenanceMekSituationController;
            yield return MaintenanceMekWaterBodyCleaner;
        }
    }

    public string Name { get; private set; }
    public string BaitName { get; private set; }
    public string ChineseName { get; private set; }

    private FishType(string name, string baitName, string chineseName)
    {
        Name = name;
        BaitName = baitName;
        ChineseName = chineseName;
    }

    public static FishType FromName(string name)
    {
        foreach (var fishType in Values)
        {
            if (fishType.Name == name)
            {
                return fishType;
            }
        }

        throw new KeyNotFoundException($"FishType {name} not found");
    }
}