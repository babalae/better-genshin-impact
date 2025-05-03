using System;
using System.ComponentModel;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

public enum MapTypes
{
    [Description("提瓦特大陆")]
    Teyvat,

    [Description("层岩巨渊")]
    TheChasm,

    [Description("渊下宫")]
    Enkanomiya,

    [Description("旧日之海")]
    SeaOfBygoneEras,

    [Description("远古圣山")]
    AncientSacredMountain
}
public static class MapTypesExtensions
{
    public static MapTypes ParseFromDescription(string description)
    {
        foreach (var field in typeof(MapTypes).GetFields())
        {
            if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attribute)
            {
                if (attribute.Description == description)
                {
                    return (MapTypes)field.GetValue(null)!;
                }
            }
        }
        throw new ArgumentException($"无法找到描述为 '{description}' 的枚举值", nameof(description));
    }
    
    public static MapTypes ParseFromName(string name)
    {
        if (Enum.TryParse<MapTypes>(name, true, out var result))
        {
            return result;
        }
        throw new ArgumentException($"无法找到名称为 '{name}' 的枚举值", nameof(name));
    }
}