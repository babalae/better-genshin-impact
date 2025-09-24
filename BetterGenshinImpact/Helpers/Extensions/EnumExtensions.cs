using BetterGenshinImpact.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

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

    public static bool TryGetEnumValueFromDescription<T>(this string description, [NotNullWhen(true)] out T? result) where T : struct, Enum
    {
        result = null;
        if (string.IsNullOrEmpty(description))
            return false;

        var type = typeof(T);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute))
                is DescriptionAttribute attribute)
            {
                if (attribute.Description.Equals(description, StringComparison.OrdinalIgnoreCase))
                {
                    var value = field.GetValue(null);
                    if (value is T enumValue)
                    {
                        result = enumValue;
                        return true;
                    }
                    return false;
                }
            }
        }
        return false;
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
