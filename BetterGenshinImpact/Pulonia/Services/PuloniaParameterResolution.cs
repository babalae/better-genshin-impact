using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 有效参数顶层字段的来源层级，按生效顺序排列：后面的层级覆盖前面的同名顶层字段。
/// </summary>
public enum PuloniaParameterLayer
{
    /// <summary>能力说明的默认参数。</summary>
    Default,

    /// <summary>由共享设置按绑定写入（顶层参数内的明细来源见设置来源）。</summary>
    Settings,

    /// <summary>节点自身的任务参数。</summary>
    Node,

    /// <summary>本次运行按节点地址提供的调用覆盖。</summary>
    Call
}

/// <summary>
/// 共享设置值的来源种类。
/// </summary>
public enum PuloniaSettingSourceKind
{
    /// <summary>设置目录默认值。</summary>
    Default,

    /// <summary>某个节点所挂的设置方案。</summary>
    Profile,

    /// <summary>某个节点自身的共享设置。</summary>
    Node
}

/// <summary>
/// 设置链中的一层：节点及其已加载的方案。节点 <see cref="PuloniaTask.ProfileId"/> 不为空而方案为 null 表示方案缺失。
/// </summary>
/// <param name="Node">提供设置的节点（编辑树或准备树中的节点）。</param>
/// <param name="Location">节点位置文本：运行准备为节点地址，界面预览可为空。</param>
/// <param name="Profile">节点所挂的方案；未挂或缺失时为 null。</param>
public sealed record PuloniaSettingLevel(PuloniaTask Node, string Location, PuloniaSettingsProfile? Profile);

/// <summary>
/// 单个共享设置项的最终来源。
/// </summary>
/// <param name="Kind">来源种类。</param>
/// <param name="Level">提供该值的设置链层级；默认值为 null。</param>
public readonly record struct PuloniaSettingSource(PuloniaSettingSourceKind Kind, PuloniaSettingLevel? Level = null);

/// <summary>
/// 一条设置链解析出的全部共享设置有效值与来源。
/// </summary>
public sealed class PuloniaSettingsResolution
{
    /// <summary>目录中每个设置项的有效值，由调用方独占。</summary>
    public IReadOnlyDictionary<string, JToken> Values { get; }

    /// <summary>每个设置项的来源。</summary>
    public IReadOnlyDictionary<string, PuloniaSettingSource> Sources { get; }

    /// <summary>挂了方案但方案缺失的层级；运行准备应报错，界面预览应提示。</summary>
    public IReadOnlyList<PuloniaSettingLevel> MissingProfiles { get; }

    /// <summary>建立解析结果；仅由 <see cref="PuloniaParameterResolver"/> 创建。</summary>
    internal PuloniaSettingsResolution(IReadOnlyDictionary<string, JToken> values,
        IReadOnlyDictionary<string, PuloniaSettingSource> sources, IReadOnlyList<PuloniaSettingLevel> missingProfiles)
    {
        Values = values;
        Sources = sources;
        MissingProfiles = missingProfiles;
    }
}

/// <summary>
/// 一次有效参数解析的结果：参数副本、顶层来源、共享设置明细，以及运行准备需要报错的冲突。
/// </summary>
public sealed class PuloniaParameterResolution
{
    /// <summary>
    /// 合并后的有效参数，由调用方独占，可直接修改或保存。
    /// </summary>
    public JObject Parameters { get; }

    /// <summary>
    /// 每个顶层参数最后一次被赋值的来源层级。
    /// </summary>
    public IReadOnlyDictionary<string, PuloniaParameterLayer> Sources { get; }

    /// <summary>
    /// 设置链的解析结果，包含未被本能力使用的设置项。
    /// </summary>
    public PuloniaSettingsResolution Settings { get; }

    /// <summary>
    /// 本能力绑定使用的设置项 ID。
    /// </summary>
    public IReadOnlyList<string> BoundSettingKeys { get; }

    /// <summary>
    /// 任务参数中试图直接写入被共享设置覆盖的顶层参数的字段；这些字段未参与解析，运行准备应报错。
    /// </summary>
    public IReadOnlyList<string> ConflictingParameters { get; }

    /// <summary>
    /// 调用覆盖中试图直接写入被共享设置覆盖的顶层参数的字段；这些字段未参与解析，运行准备应报错。
    /// </summary>
    public IReadOnlyList<string> ConflictingCallParameters { get; }

    /// <summary>
    /// 叶子自身共享设置中本能力不使用的设置项；运行准备应报错。
    /// </summary>
    public IReadOnlyList<string> UnusedLeafSettings { get; }

    /// <summary>
    /// 建立解析结果；仅由 <see cref="PuloniaParameterResolver"/> 创建。
    /// </summary>
    internal PuloniaParameterResolution(JObject parameters, IReadOnlyDictionary<string, PuloniaParameterLayer> sources,
        PuloniaSettingsResolution settings, IReadOnlyList<string> boundSettingKeys, IReadOnlyList<string> conflictingParameters,
        IReadOnlyList<string> conflictingCallParameters, IReadOnlyList<string> unusedLeafSettings)
    {
        Parameters = parameters;
        Sources = sources;
        Settings = settings;
        BoundSettingKeys = boundSettingKeys;
        ConflictingParameters = conflictingParameters;
        ConflictingCallParameters = conflictingCallParameters;
        UnusedLeafSettings = unusedLeafSettings;
    }
}
