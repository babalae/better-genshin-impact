using System.IO;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 统一的 JSON 读写与复制入口，保持参数类型并拒绝隐式格式迁移。
/// </summary>
public static class PuloniaTaskJson
{
    /// <summary>
    /// 从 JSON 读取并校验计划；不在缺少身份时悄悄生成新 ID。
    /// </summary>
    public static PuloniaTaskPlan ReadPlan(string json)
    {
        var plan = Read<PuloniaTaskPlan>(json);
        PuloniaTaskValidator.ValidatePlan(plan);
        return plan;
    }

    /// <summary>
    /// 校验后输出计划 JSON。
    /// </summary>
    public static string WritePlan(PuloniaTaskPlan plan)
    {
        PuloniaTaskValidator.ValidatePlan(plan);
        return Write(plan);
    }

    /// <summary>
    /// 从 JSON 读取并校验参数预设。
    /// </summary>
    public static PuloniaTaskPreset ReadPreset(string json)
    {
        var preset = Read<PuloniaTaskPreset>(json);
        PuloniaTaskValidator.ValidatePreset(preset);
        return preset;
    }

    /// <summary>
    /// 校验后输出预设 JSON。
    /// </summary>
    public static string WritePreset(PuloniaTaskPreset preset)
    {
        PuloniaTaskValidator.ValidatePreset(preset);
        return Write(preset);
    }

    /// <summary>
    /// 深复制计划，保留全部稳定 ID；运行准备使用此入口。
    /// </summary>
    public static PuloniaTaskPlan ClonePlan(PuloniaTaskPlan plan) => ReadPlan(WritePlan(plan));

    /// <summary>
    /// 读取运行准备样例的显式选项，业务模型与选项使用相同 JSON 类型规则。
    /// </summary>
    public static PuloniaTaskBuildOptions ReadBuildOptions(string json) => Read<PuloniaTaskBuildOptions>(json);

    /// <summary>
    /// 复制子树供粘贴使用；每个节点生成新 ID，参数仍按同一格式读取。
    /// </summary>
    public static PuloniaTask CopySubtree(PuloniaTask task)
    {
        // 使用临时根进行同一套结构校验，不允许对象循环进入序列化。
        var wrapper = new PuloniaTaskPlan();
        wrapper.RootTask.Children.Add(task);
        var clone = ClonePlan(wrapper).RootTask.Children[0];
        AssignNewIds(clone);
        return clone;
    }

    /// <summary>
    /// 读取单一对象，拒绝重复键、额外根值、未知属性与过深 JSON。
    /// </summary>
    internal static T Read<T>(string json)
    {
        using var input = new StringReader(json);
        using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None, MaxDepth = 256 };
        var token = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read())
            throw new JsonSerializationException("JSON 根对象后不能包含其他内容。");
        return token.ToObject<T>(JsonSerializer.Create(CreateSettings()))
               ?? throw new JsonSerializationException("JSON 对象不能为空。");
    }

    /// <summary>
    /// 固定格式输出；不使用项目的 System.Text.Json 配置。
    /// </summary>
    internal static string Write(object value) => JsonConvert.SerializeObject(value, CreateSettings());

    /// <summary>
    /// 每次创建独立序列化配置，不共享可变 JsonSerializer 实例。
    /// </summary>
    private static JsonSerializerSettings CreateSettings() => new()
    {
        Formatting = Formatting.Indented,
        DateParseHandling = DateParseHandling.None,
        TypeNameHandling = TypeNameHandling.None,
        MissingMemberHandling = MissingMemberHandling.Error,
        ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
        MaxDepth = 256
    };

    /// <summary>
    /// 递归更新副本身份，不改变原树。
    /// </summary>
    private static void AssignNewIds(PuloniaTask task)
    {
        task.Id = System.Guid.NewGuid().ToString("N");
        foreach (var child in task.Children)
            AssignNewIds(child);
    }
}
