using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 独立任务参数与结果数据的统一 JSON 约定：snake_case、枚举存为字符串、包含公共字段。
/// 参数类会复用 AllConfig 下已用 System.Text.Json 序列化的配置类，因此沿用 System.Text.Json。
/// </summary>
public static class SoloTaskJson
{
    /// <summary>
    /// 独立任务参数与结果数据的序列化选项。
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
        // 显式指定反射解析器，供任务计划按类型元数据生成参数 Schema
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    /// <summary>
    /// 按声明类型序列化对象。
    /// </summary>
    /// <param name="value">要序列化的对象。</param>
    /// <param name="type">对象的声明类型。</param>
    /// <returns>JSON 文本。</returns>
    public static string Serialize(object? value, Type type) => JsonSerializer.Serialize(value, type, Options);

    /// <summary>
    /// 按声明类型反序列化对象。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="type">目标类型。</param>
    /// <returns>反序列化得到的对象。</returns>
    /// <exception cref="JsonException">JSON 为 null 字面量或格式无效。</exception>
    public static object Deserialize(string json, Type type)
        => JsonSerializer.Deserialize(json, type, Options) ?? throw new JsonException($"无法把 JSON 转换为 {type.Name}。");

    /// <summary>
    /// 序列化强类型对象。
    /// </summary>
    /// <param name="value">要序列化的对象。</param>
    /// <returns>JSON 文本。</returns>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// 反序列化强类型对象。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <returns>反序列化得到的对象。</returns>
    public static T Deserialize<T>(string json) => (T)Deserialize(json, typeof(T));
}
