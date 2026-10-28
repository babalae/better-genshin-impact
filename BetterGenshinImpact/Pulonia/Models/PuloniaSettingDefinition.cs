using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 共享设置项的编辑提示；服务层只给出提示，具体控件由视图层决定。
/// </summary>
public enum PuloniaSettingEditorKind
{
    /// <summary>开关。</summary>
    Boolean,

    /// <summary>单行文本。</summary>
    Text,

    /// <summary>整数。</summary>
    Integer,

    /// <summary>小数。</summary>
    Number,

    /// <summary>从固定选项中选择。</summary>
    Choice,

    /// <summary>战斗策略选择器。</summary>
    CombatStrategy,

    /// <summary>路线 CD 专用编辑器。</summary>
    CollectionCooldown
}

/// <summary>
/// 设置项的一个固定选项。
/// </summary>
/// <param name="Value">写入设置的值。</param>
/// <param name="Label">界面显示文字。</param>
public sealed record PuloniaSettingChoice(JToken Value, string Label);

/// <summary>
/// 设置项之间的显示或启用条件：另一设置项的有效值在（或不在）给定集合中时成立。
/// </summary>
/// <param name="Key">被依赖的设置项 ID。</param>
/// <param name="Values">比较的取值集合。</param>
/// <param name="Negate">为 true 时表示有效值不在集合中才成立。</param>
public sealed record PuloniaSettingCondition(string Key, IReadOnlyList<JToken> Values, bool Negate = false);

/// <summary>
/// 共享设置的一个类别，决定编辑器的分组与顺序。
/// </summary>
/// <param name="Id">类别 ID，同时是设置项 ID 的前缀。</param>
/// <param name="DisplayName">类别名称。</param>
/// <param name="Order">排序，小的在前。</param>
public sealed record PuloniaSettingCategory(string Id, string DisplayName, int Order);

/// <summary>
/// 代码注册的一项共享设置；不持久化，设置项 ID 一经发布不再改变。
/// </summary>
public sealed class PuloniaSettingDefinition
{
    /// <summary>稳定 ID，格式为“类别.名称”。</summary>
    public required string Key { get; init; }

    /// <summary>所属类别 ID。</summary>
    public required string Category { get; init; }

    /// <summary>类别内排序。</summary>
    public int Order { get; init; }

    /// <summary>中文名称。</summary>
    public required string DisplayName { get; init; }

    /// <summary>中文说明；为空时不显示。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 宿主能力（地图追踪、JS、Shell）共用的参数路径，点分隔，各段与参数 JSON 属性名一致；
    /// 能力是否使用该设置项由其说明中的绑定决定。
    /// </summary>
    public required string ParameterPath { get; init; }

    /// <summary>值的 Schema，沿用参数 Schema 子集。</summary>
    public required JObject ValueSchema { get; init; }

    /// <summary>整条设置链都未设置时使用的值；读取时返回副本。</summary>
    public required JToken DefaultValue { get; init; }

    /// <summary>是否默认隐藏，仅在“显示高级设置”时出现。</summary>
    public bool IsAdvanced { get; init; }

    /// <summary>编辑提示。</summary>
    public PuloniaSettingEditorKind Editor { get; init; }

    /// <summary>固定选项；仅 <see cref="PuloniaSettingEditorKind.Choice"/> 使用。</summary>
    public IReadOnlyList<PuloniaSettingChoice> Choices { get; init; } = [];

    /// <summary>数值单位，例如“秒”；为空时不显示。</summary>
    public string? Unit { get; init; }

    /// <summary>全部成立时才显示；为空表示总是显示。</summary>
    public IReadOnlyList<PuloniaSettingCondition> VisibleWhen { get; init; } = [];

    /// <summary>全部成立时才可编辑；为空表示总是可编辑。</summary>
    public IReadOnlyList<PuloniaSettingCondition> EnabledWhen { get; init; } = [];

    /// <summary>不可编辑时的说明。</summary>
    public string? DisabledReason { get; init; }
}
