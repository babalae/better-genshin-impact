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
            if (source[property.Name] is not Undefined && source[property.Name] != null)
            {
                object value = source.GetProperty(property.Name);

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

        return result;
    }
}
