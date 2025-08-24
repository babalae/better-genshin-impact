using Microsoft.ClearScript;
using System;
using System.Reflection;

namespace BetterGenshinImpact.Helpers;

public class ScriptObjectConverter
{
    public static T ConvertTo<T>(ScriptObject source)
    {
        T result = Activator.CreateInstance<T>();
        Type type = typeof(T);

        foreach (PropertyInfo property in type.GetProperties())
        {
            // 支持 PascalCase 和 camelCase
            DealWithProperty(source, property, char.ToLower(property.Name[0]) + property.Name[1..], result);
            DealWithProperty(source, property, char.ToUpper(property.Name[0]) + property.Name[1..], result);
        }

        return result;
    }

    private static void DealWithProperty<T>(ScriptObject source, PropertyInfo property, string propertyName, T result)
    {
        if (source[propertyName] is not Undefined && source[propertyName] != null)
        {
            object value = source.GetProperty(propertyName);

            if (property.PropertyType.IsPrimitive || property.PropertyType == typeof(string))
            {
                property.SetValue(result, Convert.ChangeType(value, property.PropertyType));
            }
            else
            {
                MethodInfo method = typeof(ScriptObjectConverter).GetMethod("ConvertTo")!.MakeGenericMethod(property.PropertyType);
                object? nestedValue = method.Invoke(null, [value]);
                property.SetValue(result, nestedValue);
            }
        }
    }
    
    public static T GetValue<T>(ScriptObject source, string propertyName, T defaultValue)
    {
        if (source[propertyName] is not Undefined && source[propertyName] != null)
        {
            object value = source.GetProperty(propertyName);
            return (T)value;
        }
        return defaultValue;
    }
}
