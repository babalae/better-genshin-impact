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
            switch (type)
            {
                case "omni":
                    return ElementalType.Omni;
                case "cryo":
                    return ElementalType.Cryo;
                case "hydro":
                    return ElementalType.Hydro;
                case "pyro":
                    return ElementalType.Pyro;
                case "electro":
                    return ElementalType.Electro;
                case "dendro":
                    return ElementalType.Dendro;
                case "anemo":
                    return ElementalType.Anemo;
                case "geo":
                    return ElementalType.Geo;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public static ElementalType ChineseToElementalType(this string type)
        {
            type = type.ToLower();
            switch (type)
            {
                case "全":
                    return ElementalType.Omni;
                case "冰":
                    return ElementalType.Cryo;
                case "水":
                    return ElementalType.Hydro;
                case "火":
                    return ElementalType.Pyro;
                case "雷":
                    return ElementalType.Electro;
                case "草":
                    return ElementalType.Dendro;
                case "风":
                    return ElementalType.Anemo;
                case "岩":
                    return ElementalType.Geo;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }   
        }

        public static string ToChinese(this ElementalType type)
        {
            switch (type)
            {
                case ElementalType.Omni:
                    return "全";
                case ElementalType.Cryo:
                    return "冰";
                case ElementalType.Hydro:
                    return "水";
                case ElementalType.Pyro:
                    return "火";
                case ElementalType.Electro:
                    return "雷";
                case ElementalType.Dendro:
                    return "草";
                case ElementalType.Anemo:
                    return "风";
                case ElementalType.Geo:
                    return "岩";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public static string ToLowerString(this ElementalType type)
        {
            return type.ToString().ToLower();
        }
    }   
}