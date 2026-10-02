using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 本次运行准备的显式输入，不从全局业务配置读取参数。
/// </summary>
public sealed class PuloniaTaskBuildOptions
{
    /// <summary>
    /// 可用类型和资源说明；未注册类型在准备阶段报错。
    /// </summary>
    public List<PuloniaTaskDefinition> Definitions { get; init; } = [];

    /// <summary>
    /// 资源基础目录，用于解析相对路径。
    /// </summary>
    public string BaseDirectory { get; init; } = System.AppContext.BaseDirectory;

    /// <summary>
    /// 路径变量名和绝对目录，例如 pathingRepoFolder。
    /// </summary>
    public Dictionary<string, string> PathVariables { get; init; } = new();

    /// <summary>
    /// 可选账号绑定；仅替换当前计划内的预设选择。
    /// </summary>
    public string? AccountId { get; init; }

    /// <summary>
    /// 本次调用覆盖，以运行节点地址为键；不改写计划。
    /// </summary>
    public Dictionary<string, JObject> ParameterOverrides { get; init; } = new();

    /// <summary>
    /// 最大展开深度，同时约束计划引用与任务树。
    /// </summary>
    public int MaxDepth { get; init; } = 64;

    /// <summary>
    /// 最大展开节点数，防止目录与重复展开无界占用内存。
    /// </summary>
    public int MaxNodes { get; init; } = 10000;
}
