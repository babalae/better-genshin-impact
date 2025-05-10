using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Helpers.Extensions;

public static class EnumExtensions
{
    public static string GetDescription(this Enum value)
    {
        return value.GetType()
            .GetField(value.ToString())
            ?.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .Cast<DescriptionAttribute>()
            .FirstOrDefault()
            ?.Description ?? value.ToString();
    }

    public static int GetOrder(this Enum value)
    {
        return value.GetType()
            .GetField(value.ToString())
            ?.GetCustomAttributes(typeof(DefaultValueAttribute), false)
            .Cast<DefaultValueAttribute>()
            .FirstOrDefault()
            ?.Value as int? ?? 0;
    }

    public static IEnumerable<EnumItem<T>> ToEnumItems<T>() where T : Enum
    {
        return Enum.GetValues(typeof(T))
            .Cast<T>()
            .Select(EnumItem<T>.Create)
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Value);
    }
}
