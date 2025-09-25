using Microsoft.ClearScript;
using System;
using System.Collections.Generic;
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
            object v8Value = source.GetProperty(propertyName);

            if (TryMap(v8Value, out T value))
            {
                return value;
            }
        }
        return defaultValue;
    }

    /// <summary>
    /// <para>适用集合的重载</para>
    /// 如果<paramref name="propertyName"/>解析失败，默认返回一个空集合；
    /// 如果集合元素解析失败，将跳过该元素
    /// <para>仅支持一层集合，<typeparamref name="T"/>不能再是集合</para>
    /// <para>避开反射，享受健康生活</para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="propertyName"></param>
    /// <returns></returns>
    public static IEnumerable<T> GetValue<T>(ScriptObject source, string propertyName)
    {
        if (source[propertyName] is not Undefined && source[propertyName] != null)
        {
            object v8Value = source.GetProperty(propertyName);

            return TryMap<T>(v8Value);
        }
        return new T[0];
    }

    /// <summary>
    /// 尝试将ClearScript的类型转换成常用类型
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="v8Value"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    private static bool TryMap<T>(object v8Value, out T value)
    {
        Type type = typeof(T);
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
        {
            // 处理数字
            if (v8Value is int intValue)
            {
                if (Enum.IsDefined(type, intValue))
                {
                    value = (T)Enum.ToObject(type, intValue);
                    return true;
                }
                else
                {
                    value = default!;
                    return false;
                }
            }
            // 处理字符串
            else if (v8Value is string strValue)
            {
                if (Enum.TryParse(type, strValue, ignoreCase: true, out object? parsedEnum))
                {
                    value = (T)parsedEnum;
                    return true;
                }
                else
                {
                    value = default!;
                    return false;
                }
            }
        }

        value = (T)v8Value;
        return true;
    }

    /// <summary>
    /// 尝试将ClearScript的V8Array类型转换成常用泛型集合类型
    /// 如果集合元素解析失败，将跳过该元素，如此始终能返回一个集合
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="v8Value"></param>
    /// <returns></returns>
    private static IEnumerable<T> TryMap<T>(object v8Value)
    {
        Type elementType = typeof(T);
        var iList = (System.Collections.IList)v8Value;  // V8Array虽然是私有类型无法获取，但其实现IList，可根据此接口操作

        Type listType = typeof(List<>).MakeGenericType(elementType);
        System.Collections.IList list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (var elementV8Value in iList)
        {
            if (TryMap(elementV8Value, out T elementValue))
            {
                list.Add(elementValue);
            }
        }

        return (IEnumerable<T>)list;
    }
}
