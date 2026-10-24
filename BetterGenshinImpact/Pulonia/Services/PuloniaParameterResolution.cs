using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 有效参数的来源层级，按生效顺序排列：后面的层级覆盖前面的同名顶层字段。
/// </summary>
public enum PuloniaParameterLayer
{
    /// <summary>能力说明的默认参数。</summary>
    Default,

    /// <summary>节点选用（或账号替换）的共享预设。</summary>
    Preset,

    /// <summary>当前计划内祖先分组的公共参数。</summary>
    Group,

    /// <summary>节点自身参数。</summary>
    Node,

    /// <summary>计划引用节点向被引用计划传入的公共参数。</summary>
    ReferencePublic,

    /// <summary>本次运行按节点地址提供的调用覆盖。</summary>
    Call
}

/// <summary>
/// 预设层的判定结果；运行准备据此报错，界面预览据此提示。
/// </summary>
public enum PuloniaPresetStatus
{
    /// <summary>节点未选用预设。</summary>
    None,

    /// <summary>预设存在且作用域匹配，已参与解析。</summary>
    Applied,

    /// <summary>预设不存在，预设层被跳过。</summary>
    Missing,

    /// <summary>预设的类型、资源或参数版本与节点不匹配，预设层被跳过。</summary>
    Incompatible
}

/// <summary>
/// 单个顶层参数的最终来源；分组和引用公共参数同时给出提供该值的覆盖项，调用方据此生成各自的来源文本。
/// </summary>
/// <param name="Layer">来源层级。</param>
/// <param name="Override">提供该值的分组或引用公共参数项；其他层级为 null。</param>
public readonly record struct PuloniaParameterSource(PuloniaParameterLayer Layer, PuloniaTaskParameterOverride? Override = null);

/// <summary>
/// 一次有效参数解析的结果：参数副本、逐字段来源和预设判定。
/// </summary>
public sealed class PuloniaParameterResolution
{
    /// <summary>
    /// 合并后的有效参数，由调用方独占，可直接修改或保存。
    /// </summary>
    public JObject Parameters { get; }

    /// <summary>
    /// 每个顶层参数最后一次被赋值的来源，按字段首次出现的顺序排列。
    /// </summary>
    public IReadOnlyDictionary<string, PuloniaParameterSource> Sources { get; }

    /// <summary>
    /// 实际参与判定的预设 ID（已应用账号替换）；未选用时为 null。
    /// </summary>
    public string? PresetId { get; }

    /// <summary>
    /// 按 <see cref="PresetId"/> 找到的预设；缺失时为 null，不兼容时仍给出以便展示名称。
    /// </summary>
    public PuloniaTaskPreset? Preset { get; }

    /// <summary>
    /// 预设层的判定结果。
    /// </summary>
    public PuloniaPresetStatus PresetStatus { get; }

    /// <summary>
    /// 是否有匹配的计划引用公共参数项包含能力未声明为公共的字段；该项不参与解析，运行准备应报错。
    /// </summary>
    public bool HasUndeclaredPublicOverride { get; }

    /// <summary>
    /// 建立解析结果；仅由 <see cref="PuloniaParameterResolver"/> 创建。
    /// </summary>
    internal PuloniaParameterResolution(JObject parameters, IReadOnlyDictionary<string, PuloniaParameterSource> sources,
        string? presetId, PuloniaTaskPreset? preset, PuloniaPresetStatus presetStatus, bool hasUndeclaredPublicOverride)
    {
        Parameters = parameters;
        Sources = sources;
        PresetId = presetId;
        Preset = preset;
        PresetStatus = presetStatus;
        HasUndeclaredPublicOverride = hasUndeclaredPublicOverride;
    }
}
