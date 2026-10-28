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
/// 地图追踪、JS 与 Shell 宿主参数和旧配置组模型之间的互转，保留原配置模型的序列化约定。
/// 哪些参数可由共享设置覆盖由 <see cref="PuloniaSettingCatalog"/> 与能力绑定决定。
/// </summary>
public static class PuloniaTaskCommonSettings
{
    /// <summary>
    /// 条件选队参数名：fixed 使用参数中的队伍与角色位，condition 按地图追踪条件配置选择。
    /// </summary>
    public const string PartySourceParameter = "party_source";

    /// <summary>
    /// 不作为 Pulonia 参数的旧字段：旧调度字段由 Pulonia 自己的调度与账本负责；
    /// “启用本组配置”总开关在“未设置即继承”的模型中没有意义，执行时固定开启；两个开关字段没有读取方。
    /// </summary>
    private static readonly HashSet<string> ExcludedProperties =
    [
        nameof(PathingPartyConfig.SkipDuring), nameof(PathingPartyConfig.HideOnRepeat),
        nameof(PathingPartyConfig.TaskCycleConfig), nameof(PathingPartyConfig.OnlyInTeleportRecover),
        nameof(PathingPartyConfig.Enabled), nameof(PathingPartyConfig.AutoFightEnabled),
        nameof(PathingPartyConfig.JsScriptUseEnabled), nameof(PathingPartyConfig.SoloTaskUseFightEnabled)
    ];

    /// <summary>
    /// 可持久化的行走、战斗和食物设置；忽略运行标记、只读选项、总开关和旧调度字段。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, PropertyInfo> PathingProperties =
        GetSerializableProperties(typeof(PathingPartyConfig))
            .Where(property => !ExcludedProperties.Contains(property.Name))
            .ToDictionary(property => JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name), StringComparer.Ordinal);

    /// <summary>
    /// Shell 宿主参数名，与命令内容分开。
    /// </summary>
    private static readonly string[] ShellParameterNames =
        ["shell_disabled", "shell_timeout_seconds", "shell_no_window", "shell_output"];

    /// <summary>
    /// 创建能力默认值；返回独立对象，避免定义、编辑器与执行实例共享可变配置。
    /// </summary>
    public static JObject CreateDefaults(string taskType)
    {
        if (taskType == "shell")
        {
            // 默认值与改造前“未启用 Shell 配置”时的实际行为一致：不额外限时、隐藏窗口、不写实时日志、不跳过。
            return new JObject
            {
                ["shell_disabled"] = false,
                ["shell_timeout_seconds"] = 0,
                ["shell_no_window"] = true,
                ["shell_output"] = false
            };
        }
        var defaults = ToParameters(taskType, new ScriptGroupConfig());
        // 路线 CD 只属于 Pulonia 地图追踪，不传给旧配置或 JS 的行走设置。
        if (taskType == "pathing") defaults[PuloniaCollectionCooldown.ParameterName] = PuloniaCollectionCooldown.CreateDefaults();
        return defaults;
    }

    /// <summary>
    /// 将强类型设置转为顶层字段，供导入和默认值生成复用。
    /// </summary>
    public static JObject ToParameters(string taskType, ScriptGroupConfig config)
    {
        if (taskType == "shell")
            return new JObject
            {
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
        result[PartySourceParameter] = config.PathingConfig.PartyByCondition ? "condition" : "fixed";
        return result;
    }

    /// <summary>
    /// 从有效参数恢复执行器使用的独立配置，未提供的字段保留类型默认值；总开关固定开启。
    /// </summary>
    public static ScriptGroupConfig FromParameters(string taskType, JObject parameters)
    {
        if (taskType == "shell")
            return new ScriptGroupConfig
            {
                EnableShellConfig = true,
                ShellConfig = new ShellConfig
                {
                    Disable = parameters.Value<bool?>("shell_disabled") ?? false,
                    Timeout = parameters.Value<int?>("shell_timeout_seconds") ?? 0,
                    NoWindow = parameters.Value<bool?>("shell_no_window") ?? true,
                    Output = parameters.Value<bool?>("shell_output") ?? false
                }
            };

        var serialized = new JObject();
        foreach (var (name, property) in PathingProperties)
        {
            if (parameters.TryGetValue(name, out var value))
                serialized[property.Name] = value.DeepClone();
        }
        var pathing = JsonSerializer.Deserialize<PathingPartyConfig>(serialized.ToString())!;
        // 战斗与行走配置一律来自参数，不再因总开关回落全局配置；条件选队只决定队伍与角色位。
        pathing.Enabled = true;
        pathing.AutoFightEnabled = true;
        pathing.SkipPartySwitch = parameters.Value<bool?>("skip_party_switch") ?? false;
        pathing.PartyByCondition = parameters.Value<string>(PartySourceParameter) == "condition";
        return new ScriptGroupConfig { PathingConfig = pathing };
    }

    /// <summary>
    /// 为宿主参数生成与现有强类型模型一致的 Schema，嵌套战斗设置同样接受类型检查。
    /// </summary>
    public static JObject CreateSchemaProperties(string taskType)
    {
        if (taskType == "shell")
            return new JObject(ShellParameterNames.Select(name => new JProperty(name, name == "shell_timeout_seconds"
                ? new JObject { ["type"] = "integer", ["minimum"] = 0 }
                : new JObject { ["type"] = "boolean" })));

        var properties = new JObject();
        foreach (var (name, property) in PathingProperties)
            properties[name] = CreatePropertySchema(property);
        properties["skip_party_switch"] = new JObject { ["type"] = "boolean" };
        properties[PartySourceParameter] = new JObject { ["type"] = "string", ["enum"] = new JArray("fixed", "condition") };
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
