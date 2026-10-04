using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>将内置战斗配置冻结为普通参数，避免新一条龙在执行时读取其他页面的配置。</summary>
public static class PuloniaCombatTaskSettings
{
    /// <summary>提取可恢复的业务属性，忽略界面派生属性。</summary>
    private static PropertyInfo[] Properties<T>() => typeof(T).GetProperties()
        .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() is null).ToArray();

    /// <summary>使用原配置的序列化约定生成独立参数副本。</summary>
    public static JObject ToParameters<T>(T config)
    {
        var json = PuloniaTaskJson.Read<JObject>(JsonSerializer.Serialize(config));
        return new JObject(Properties<T>().Select(p => new JProperty(
            JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name), json[p.Name]!.DeepClone())));
    }

    /// <summary>从本次有效参数恢复独立配置，不写回全局设置。</summary>
    public static T FromParameters<T>(JObject parameters) where T : new()
    {
        var json = new JObject(Properties<T>().Where(p => parameters.ContainsKey(JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name)))
            .Select(p => new JProperty(p.Name, parameters[JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name)]!.DeepClone())));
        return JsonSerializer.Deserialize<T>(json.ToString()) ?? throw new InvalidOperationException("无法恢复内置任务参数。");
    }

    /// <summary>建立可供创建表单、编辑器和运行校验共同使用的能力定义。</summary>
    public static PuloniaTaskDefinition Definition<T>(string type, string name, T defaults, JObject? extraDefaults = null)
    {
        var values = ToParameters(defaults);
        var properties = new JObject(Properties<T>().Select(p => new JProperty(
            JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name), PuloniaTaskCommonSettings.CreatePropertySchema(p))));
        if (extraDefaults is not null)
            foreach (var field in extraDefaults.Properties())
            {
                values[field.Name] = field.Value.DeepClone();
                properties[field.Name] = new JObject { ["type"] = field.Value.Type switch
                {
                    JTokenType.Boolean => "boolean", JTokenType.Integer => "integer", JTokenType.Object => "object", _ => "string"
                } };
            }
        return new PuloniaTaskDefinition
        {
            TaskType = type, DisplayName = name, Description = $"使用本计划的队伍、策略和树脂配置执行{name}。",
            RequiresGameSession = true, DefaultParameters = values,
            ParameterSchema = new JObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false },
            PublicParameters = values.Properties().Select(p => p.Name).ToArray()
        };
    }
}
