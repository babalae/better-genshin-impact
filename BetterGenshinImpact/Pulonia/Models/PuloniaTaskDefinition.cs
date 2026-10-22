using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 由执行器提供的任务类型、默认参数及校验说明。
/// </summary>
public sealed class PuloniaTaskDefinition
{
    /// <summary>
    /// 面向用户展示的能力名称。
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 面向用户展示的能力用途说明。
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 此能力执行前是否必须准备游戏会话并独占任务输入。
    /// </summary>
    public bool RequiresGameSession { get; init; }

    /// <summary>
    /// 相对资源路径的默认根目录；为空时使用本次构建的基础目录。
    /// </summary>
    public string? ResourceBaseDirectory { get; init; }

    /// <summary>
    /// 任务类型。
    /// </summary>
    public string TaskType { get; init; } = string.Empty;

    /// <summary>
    /// 绑定的资源标识；JS 必须填写，并与节点 Path 一致。
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// 参数格式版本。
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// 类型默认参数。
    /// </summary>
    public JObject DefaultParameters { get; init; } = new();

    /// <summary>
    /// 受支持的 JSON Schema 子集：type、properties、required、additionalProperties、items、enum。
    /// </summary>
    public JObject ParameterSchema { get; init; } = new() { ["type"] = "object" };

    /// <summary>
    /// 允许由计划引用节点覆盖的公共参数名；其余参数只能在被引用计划内配置。
    /// </summary>
    public IReadOnlyList<string> PublicParameters { get; init; } = [];

    /// <summary>
    /// 此能力声明的 CD 与周期额度规则；只有结构化确认事件才会消费规则。
    /// </summary>
    public IReadOnlyList<PuloniaTaskAvailabilityRule> AvailabilityRules { get; init; } = [];

    /// <summary>
    /// 是否是由独立任务注册表提供的能力；添加菜单把它们收拢到“独立任务”子菜单。
    /// </summary>
    public bool IsSoloTask { get; init; }

    /// <summary>
    /// 菜单分组名，同组能力之间不插分隔线。
    /// </summary>
    public string Group { get; init; } = string.Empty;

    /// <summary>
    /// 菜单排序，数值小的在前。
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// 新建一条龙配置时的默认顺序；0 表示不默认加入。
    /// </summary>
    public int OneDragonOrder { get; init; }
}
