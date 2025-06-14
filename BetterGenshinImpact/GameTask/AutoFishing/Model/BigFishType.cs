using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

/// <summary>
/// 模仿Java实现的多属性枚举类
/// 按形态大类分类的原神鱼类枚举
/// </summary>
public class BigFishType
{
    public static readonly BigFishType Medaka = new("medaka", "fruit paste bait", "花鳉", 0);
    public static readonly BigFishType LargeMedaka = new("large medaka", "fruit paste bait", "大花鳉", 1);
    public static readonly BigFishType Stickleback = new("stickleback", "redrot bait", "棘鱼", 2);
    public static readonly BigFishType Koi = new("koi", "fake fly bait", "假龙", 3);
    public static readonly BigFishType KoiHead = new("koi head", "fake fly bait", "假龙头", 3);
    public static readonly BigFishType Butterflyfish = new("butterflyfish", "false worm bait", "蝶鱼", 4);
    public static readonly BigFishType Pufferfish = new("pufferfish", "fake fly bait", "炮鲀", 5);

    public static readonly BigFishType Ray = new("ray", "fake fly bait", "鳐", 6);

    // public static readonly BigFishType FormaloRay = new("formalo ray", "fake fly bait", "佛玛洛鳐");
    // public static readonly BigFishType DivdaRay = new("divda ray", "fake fly bait", "迪芙妲鳐");
    public static readonly BigFishType Angler = new("angler", "sugardew bait", "角鲀", 7);
    public static readonly BigFishType AxeMarlin = new("axe marlin", "sugardew bait", "斧枪鱼", 8);
    public static readonly BigFishType HeartfeatherBass = new("heartfeather bass", "sour bait", "心羽鲈", 9);
    public static readonly BigFishType MaintenanceMek = new("maintenance mek", "flashing maintenance mek bait", "维护机关", 10);
    public static readonly BigFishType Unihornfish = new("unihornfish", "spinelgrain bait", "独角鱼", 10);
    public static readonly BigFishType Sunfish = new("sunfish", "spinelgrain bait", "翻车鲀", 7);
    public static readonly BigFishType Rapidfish = new("rapidfish", "spinelgrain bait", "斗士急流鱼", 9);
    public static readonly BigFishType PhonyUnihornfish = new("phony unihornfish", "emberglow bait", "燃素独角鱼", 10);
    public static readonly BigFishType MagmaRapidfish = new("magma rapidfish", "emberglow bait", "炽岩斗士急流鱼", 9);


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
        }
    }

    public string Name { get; private set; }
    public string BaitName { get; private set; }
    public string ChineseName { get; private set; }

    public int NetIndex { get; private set; }

    private BigFishType(string name, string baitName, string chineseName, int netIndex)
    {
        Name = name;
        BaitName = baitName;
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