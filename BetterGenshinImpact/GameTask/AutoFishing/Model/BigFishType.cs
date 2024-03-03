using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace BetterGenshinImpact.GameTask.AutoFishing.Model;

/// <summary>
/// 模仿Java实现的多属性枚举类
/// 按形态大类分类的原神鱼类枚举
/// </summary>
public class BigFishType
{
    public static readonly BigFishType Medaka = new("medaka", "fruit paste bait", "花鳉");
    public static readonly BigFishType LargeMedaka = new("large medaka", "fruit paste bait", "大花鳉");
    public static readonly BigFishType Stickleback = new("stickleback", "redrot bait", "棘鱼");
    public static readonly BigFishType Koi = new("koi", "fake fly bait", "假龙");
    public static readonly BigFishType Butterflyfish = new("butterflyfish", "false worm bait", "蝶鱼");
    public static readonly BigFishType Pufferfish = new("pufferfish", "fake fly bait", "炮鲀");
    public static readonly BigFishType FormaloRay = new("formalo ray", "fake fly bait", "佛玛洛鳐");
    public static readonly BigFishType DivdaRay = new("divda ray", "fake fly bait", "迪芙妲鳐");
    public static readonly BigFishType Angler = new("angler", "sugardew bait", "角鲀");
    public static readonly BigFishType AxeMarlin = new("axe marlin", "sugardew bait", "斧枪鱼");
    public static readonly BigFishType HeartfeatherBass = new("heartfeather bass", "sour bait", "心羽鲈");
    public static readonly BigFishType MaintenanceMek = new("maintenance mek", "flashing maintenance mek bait", "维护机关");


    public static IEnumerable<BigFishType> Values
    {
        get
        {
            yield return Medaka;
            yield return LargeMedaka;
            yield return Stickleback;
            yield return Koi;
            yield return Butterflyfish;
            yield return Pufferfish;
            yield return FormaloRay;
            yield return DivdaRay;
            yield return Angler;
            yield return AxeMarlin;
            yield return HeartfeatherBass;
            yield return MaintenanceMek;
        }
    }

    public string Name { get; private set; }
    public string BaitName { get; private set; }
    public string ChineseName { get; private set; }

    private BigFishType(string name, string baitName, string chineseName)
    {
        Name = name;
        BaitName = baitName;
        ChineseName = chineseName;
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
        for (int i = 0; i < Values.Count(); i++)
        {
            if (Values.ElementAt(i).Name == e.Name)
            {
                return i;
            }
        }
        throw new KeyNotFoundException($"BigFishType {e.Name} not found index");
    }
}