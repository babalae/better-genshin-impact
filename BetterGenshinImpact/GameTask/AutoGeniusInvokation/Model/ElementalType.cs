using System;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model
{
    public enum ElementalType
    {
        Omni,
        Cryo,
        Hydro,
        Pyro,
        Electro,
        Dendro,
        Anemo,
        Geo
    }

    public static class ElementalTypeExtension
    {
        public static ElementalType ToElementalType(this string type)
        {
            type = type.ToLower();
            return type switch
            {
                "omni" => ElementalType.Omni,
                "cryo" => ElementalType.Cryo,
                "hydro" => ElementalType.Hydro,
                "pyro" => ElementalType.Pyro,
                "electro" => ElementalType.Electro,
                "dendro" => ElementalType.Dendro,
                "anemo" => ElementalType.Anemo,
                "geo" => ElementalType.Geo,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }

        public static ElementalType ChineseToElementalType(this string type)
        {
            type = type.ToLower();
            return type switch
            {
                "全" => ElementalType.Omni,
                "冰" => ElementalType.Cryo,
                "水" => ElementalType.Hydro,
                "火" => ElementalType.Pyro,
                "雷" => ElementalType.Electro,
                "草" => ElementalType.Dendro,
                "风" => ElementalType.Anemo,
                "岩" => ElementalType.Geo,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }

        public static string ToChinese(this ElementalType type)
        {
            return type switch
            {
                ElementalType.Omni => "全",
                ElementalType.Cryo => "冰",
                ElementalType.Hydro => "水",
                ElementalType.Pyro => "火",
                ElementalType.Electro => "雷",
                ElementalType.Dendro => "草",
                ElementalType.Anemo => "风",
                ElementalType.Geo => "岩",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            };
        }

        public static string ToLowerString(this ElementalType type)
        {
            return type.ToString().ToLower();
        }
    }
}