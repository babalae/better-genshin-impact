using System;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using BetterGenshinImpact.GameTask.SoloTask;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 按 <see cref="SoloTaskJson"/> 的序列化合同为独立任务参数生成任务计划支持的 JSON Schema 子集。
/// 属性名、字段、枚举都与参数 JSON 一致，保证默认参数和节点参数能通过校验。
/// </summary>
public static class PuloniaSoloTaskSchema
{
    /// <summary>
    /// 嵌套对象的最大展开深度，更深的对象不再约束结构。
    /// </summary>
    private const int MaxDepth = 8;

    /// <summary>
    /// 为参数类型生成顶层 Schema。
    /// </summary>
    /// <param name="paramType">参数类型。</param>
    /// <returns>对象类型的 Schema。</returns>
    public static JObject Create(Type paramType)
    {
        var schema = CreateValueSchema(paramType, 0);
        // 顶层参数始终是对象，不允许为 null
        schema["type"] = "object";
        return schema;
    }

    /// <summary>
    /// 为一个值类型生成 Schema；引用类型统一允许 null，避免与运行时的空值不一致。
    /// </summary>
    /// <param name="type">值的声明类型。</param>
    /// <param name="depth">当前嵌套深度。</param>
    /// <returns>值的 Schema；无法确定结构时返回不带约束的空 Schema。</returns>
    private static JObject CreateValueSchema(Type type, int depth)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var nullable = underlying is not null || !type.IsValueType;
        var valueType = underlying ?? type;
        var schema = new JObject();
        string jsonType;

        // 1. 标量：布尔、字符串、枚举、数字
        if (valueType == typeof(bool))
        {
            jsonType = "boolean";
        }
        else if (valueType == typeof(string) || valueType == typeof(char) || valueType == typeof(Guid)
                 || valueType == typeof(DateTime) || valueType == typeof(DateTimeOffset) || valueType == typeof(TimeSpan))
        {
            jsonType = "string";
        }
        else if (valueType.IsEnum)
        {
            jsonType = "string";
            // 位标志枚举会序列化成组合字符串，不能用固定选项约束
            if (!valueType.IsDefined(typeof(FlagsAttribute), false))
            {
                var choices = new JArray(Enum.GetNames(valueType).Cast<object>().ToArray());
                if (nullable)
                {
                    choices.Add(JValue.CreateNull());
                }

                schema["enum"] = choices;
            }
        }
        else if (IsInteger(valueType))
        {
            jsonType = "integer";
        }
        else if (valueType == typeof(float) || valueType == typeof(double) || valueType == typeof(decimal))
        {
            jsonType = "number";
        }
        else if (depth >= MaxDepth || valueType == typeof(object) || typeof(JToken).IsAssignableFrom(valueType)
                 || typeof(System.Text.Json.Nodes.JsonNode).IsAssignableFrom(valueType))
        {
            return schema;
        }
        else
        {
            // 2. 复合类型：按 System.Text.Json 的类型元数据生成
            var info = SoloTaskJson.Options.GetTypeInfo(valueType);
            switch (info.Kind)
            {
                case JsonTypeInfoKind.Enumerable:
                    jsonType = "array";
                    if (info.ElementType is { } elementType)
                    {
                        schema["items"] = CreateValueSchema(elementType, depth + 1);
                    }

                    break;
                case JsonTypeInfoKind.Dictionary:
                    jsonType = "object";
                    break;
                case JsonTypeInfoKind.Object:
                    jsonType = "object";
                    schema["properties"] = new JObject(info.Properties
                        .Where(p => p.Get is not null)
                        .Select(p => new JProperty(p.Name, p.CustomConverter is not null
                            ? new JObject()
                            : CreateValueSchema(p.PropertyType, depth + 1))));
                    schema["additionalProperties"] = false;
                    break;
                default:
                    // 使用自定义转换器的类型，结构由转换器决定，不做约束
                    return schema;
            }
        }

        schema["type"] = nullable ? new JArray(jsonType, "null") : new JValue(jsonType);
        return schema;
    }

    /// <summary>
    /// 判断是否为整数类型。
    /// </summary>
    private static bool IsInteger(Type type)
        => type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
           || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);
}
