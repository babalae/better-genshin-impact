using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 将现有配置组的运行设置映射为可继承的 Pulonia 参数，保留原配置模型的序列化约定。
/// </summary>
public static class PuloniaTaskCommonSettings
{
    /// <summary>
    /// 旧调度器专属字段由 Pulonia 自己的调度与账本负责，不作为游戏执行参数传递。
    /// </summary>
    private static readonly HashSet<string> LegacySchedulingProperties =
    [
        nameof(PathingPartyConfig.SkipDuring), nameof(PathingPartyConfig.HideOnRepeat),
        nameof(PathingPartyConfig.TaskCycleConfig), nameof(PathingPartyConfig.TaskCompletionSkipRuleConfig),
        nameof(PathingPartyConfig.PreExecutionPriorityConfig), nameof(PathingPartyConfig.OnlyInTeleportRecover)
    ];

    /// <summary>
    /// 可持久化的行走、战斗和食物设置；忽略运行标记、只读选项和旧调度字段。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, PropertyInfo> PathingProperties =
        GetSerializableProperties(typeof(PathingPartyConfig))
            .Where(property => !LegacySchedulingProperties.Contains(property.Name))
            .ToDictionary(property => property.Name == nameof(PathingPartyConfig.Enabled)
                ? "pathing_enabled"
                : JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name), StringComparer.Ordinal);

    /// <summary>
    /// Shell 公共参数名，与命令内容分开继承。
    /// </summary>
    private static readonly HashSet<string> ShellParameterNames =
        ["shell_config_enabled", "shell_disabled", "shell_timeout_seconds", "shell_no_window", "shell_output"];

    /// <summary>
    /// 判断字段是否属于跨 JS 资源安全复用的宿主设置；脚本 settings 不属于此集合。
    /// </summary>
    public static bool IsPathingParameter(string name)
        => PathingProperties.ContainsKey(name) || name == "skip_party_switch";

    /// <summary>
    /// 判断字段是否属于指定任务类型的公共配置表单。
    /// </summary>
    public static bool IsCommonParameter(string taskType, string name)
        => taskType == "shell" ? ShellParameterNames.Contains(name)
            : taskType is "pathing" or "javascript" && IsPathingParameter(name);

    /// <summary>
    /// 创建能力默认值；返回独立对象，避免定义、编辑器与执行实例共享可变配置。
    /// </summary>
    public static JObject CreateDefaults(string taskType) => ToParameters(taskType, new ScriptGroupConfig());

    /// <summary>
    /// 将强类型设置转为顶层字段，以便现有参数合并、来源展示及撤销直接复用。
    /// </summary>
    public static JObject ToParameters(string taskType, ScriptGroupConfig config)
    {
        if (taskType == "shell")
            return new JObject
            {
                ["shell_config_enabled"] = config.EnableShellConfig,
                ["shell_disabled"] = config.ShellConfig.Disable,
                ["shell_timeout_seconds"] = config.ShellConfig.Timeout,
                ["shell_no_window"] = config.ShellConfig.NoWindow,
                ["shell_output"] = config.ShellConfig.Output
            };

        // 旧配置使用 System.Text.Json；不能通过 Newtonsoft 忽略其 JsonIgnore 或枚举约定。
        var serialized = PuloniaTaskJson.Read<JObject>(JsonSerializer.Serialize(config.PathingConfig));
        var result = new JObject();
        foreach (var (name, property) in PathingProperties)
            result[name] = serialized[property.Name]!.DeepClone();
        result["skip_party_switch"] = config.PathingConfig.SkipPartySwitch;
        return result;
    }

    /// <summary>
    /// 从有效参数恢复表单或执行器使用的独立配置，未提供的字段保留类型默认值。
    /// </summary>
    public static ScriptGroupConfig FromParameters(string taskType, JObject parameters)
    {
        if (taskType == "shell")
            return new ScriptGroupConfig
            {
                EnableShellConfig = parameters.Value<bool?>("shell_config_enabled") ?? false,
                ShellConfig = new ShellConfig
                {
                    Disable = parameters.Value<bool?>("shell_disabled") ?? false,
                    Timeout = parameters.Value<int?>("shell_timeout_seconds") ?? 60,
                    NoWindow = parameters.Value<bool?>("shell_no_window") ?? true,
                    Output = parameters.Value<bool?>("shell_output") ?? true
                }
            };

        var serialized = new JObject();
        foreach (var (name, property) in PathingProperties)
        {
            if (parameters.TryGetValue(name, out var value))
                serialized[property.Name] = value.DeepClone();
        }
        var pathing = JsonSerializer.Deserialize<PathingPartyConfig>(serialized.ToString())!;
        pathing.SkipPartySwitch = parameters.Value<bool?>("skip_party_switch") ?? false;
        return new ScriptGroupConfig { PathingConfig = pathing };
    }

    /// <summary>
    /// 为公共参数生成与现有强类型模型一致的 Schema，嵌套战斗设置同样接受类型检查。
    /// </summary>
    public static JObject CreateSchemaProperties(string taskType)
    {
        if (taskType == "shell")
            return new JObject(ShellParameterNames.Select(name => new JProperty(name,
                new JObject { ["type"] = name == "shell_timeout_seconds" ? "integer" : "boolean" })));

        var properties = new JObject();
        foreach (var (name, property) in PathingProperties)
            properties[name] = CreatePropertySchema(property);
        properties["skip_party_switch"] = new JObject { ["type"] = "boolean" };
        return properties;
    }

    /// <summary>
    /// 仅枚举 JSON 会持久化且能够恢复的公开属性。
    /// </summary>
    private static IEnumerable<PropertyInfo> GetSerializableProperties(Type type)
        => type.GetProperties().Where(property => property.CanRead && property.CanWrite
            && property.GetCustomAttribute<JsonIgnoreAttribute>() is null);

    /// <summary>
    /// 根据属性类型和可空声明生成校验约束，不把显式 null 转为缺省值。
    /// </summary>
    internal static JObject CreatePropertySchema(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var nullable = new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;
        var valueType = type == typeof(bool) ? "boolean"
            : type == typeof(string) ? "string"
            : type.IsArray || typeof(System.Collections.IList).IsAssignableFrom(type) ? "array"
            : type == typeof(double) || type == typeof(float) || type == typeof(decimal) ? "number"
            : type.IsEnum || type.IsPrimitive ? "integer" : "object";
        var schema = new JObject { ["type"] = nullable ? new JArray(valueType, "null") : new JValue(valueType) };
        if (valueType == "array")
            schema["items"] = new JObject { ["type"] = "string" };
        if (type.IsEnum)
        {
            var choices = new JArray(Enum.GetValues(type).Cast<object>().Select(Convert.ToInt32));
            if (nullable)
                choices.Add(JValue.CreateNull());
            schema["enum"] = choices;
        }
        if (valueType == "object")
        {
            schema["properties"] = new JObject(GetSerializableProperties(type)
                .Select(child => new JProperty(child.Name, CreatePropertySchema(child))));
            schema["additionalProperties"] = false;
        }
        return schema;
    }
}
