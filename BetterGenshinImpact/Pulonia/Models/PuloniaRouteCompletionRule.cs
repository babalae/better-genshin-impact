using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 完成记录中“记录时的刷新规则”摘要：模式、该模式用到的时长或重置时刻、识别的材料与规则来源。
/// 只输出当前模式用到的字段，例如 <c>{ "mode": "rolling", "hours": 46, "material": "落落莓", "source": "目录自动识别" }</c>。
/// </summary>
public sealed class PuloniaRouteCompletionRule
{
    /// <summary>记录时生效的模式，不会是自动识别。</summary>
    [JsonProperty("mode", Required = Required.Always)]
    [JsonConverter(typeof(StringEnumConverter), typeof(SnakeCaseNamingStrategy))]
    public PuloniaRouteRefreshMode Mode { get; init; } = PuloniaRouteRefreshMode.Disabled;

    /// <summary>滚动计时的小时数；只在滚动模式时有值。</summary>
    [JsonProperty("hours", NullValueHandling = NullValueHandling.Ignore)]
    public double? Hours { get; init; }

    /// <summary>日期间隔天数；只在日期间隔模式时有值。</summary>
    [JsonProperty("days", NullValueHandling = NullValueHandling.Ignore)]
    public int? Days { get; init; }

    /// <summary>服务器重置小时；只在每日、每周模式时有值。</summary>
    [JsonProperty("reset_hour", NullValueHandling = NullValueHandling.Ignore)]
    public int? ResetHour { get; init; }

    /// <summary>每周重置的星期（1 为周一）；只在每周模式时有值。</summary>
    [JsonProperty("reset_weekday", NullValueHandling = NullValueHandling.Ignore)]
    public int? ResetWeekday { get; init; }

    /// <summary>识别的材料或类别目录名；未识别时为空。</summary>
    [JsonProperty("material", NullValueHandling = NullValueHandling.Ignore)]
    public string? Material { get; init; }

    /// <summary>规则来源，写中文取值（目录自动识别、混合类别、未匹配规则、手动覆盖）。</summary>
    [JsonProperty("source", Required = Required.Always)]
    [JsonConverter(typeof(StringEnumConverter))]
    public PuloniaRouteRefreshRuleSource Source { get; init; } = PuloniaRouteRefreshRuleSource.Unmatched;

    /// <summary>是否为文档标明数据不确定的参考规则；为 false 时不写出。</summary>
    [JsonProperty("is_reference", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool IsReference { get; init; }

    /// <summary>
    /// 从一份已固定的刷新规则生成摘要，只保留当前模式用到的字段。
    /// </summary>
    /// <param name="rule">节点固定的刷新规则。</param>
    /// <returns>规则摘要。</returns>
    public static PuloniaRouteCompletionRule From(PuloniaRouteRefreshRule rule) => new()
    {
        Mode = rule.Mode,
        Hours = rule.Mode == PuloniaRouteRefreshMode.Rolling ? rule.Hours : null,
        Days = rule.Mode == PuloniaRouteRefreshMode.CalendarDays ? rule.Days : null,
        ResetHour = rule.Mode is PuloniaRouteRefreshMode.Daily or PuloniaRouteRefreshMode.Weekly ? rule.ResetHour : null,
        ResetWeekday = rule.Mode == PuloniaRouteRefreshMode.Weekly ? rule.ResetWeekday : null,
        Material = rule.MaterialName,
        Source = rule.Source,
        IsReference = rule.IsReference
    };

    /// <summary>
    /// 还原为可参与判定和生成说明文字的刷新规则；总览按它显示“记录时规则”和按记录时规则推算的状态。
    /// </summary>
    /// <param name="route">完成记录的路线身份。</param>
    /// <returns>记录时的刷新规则。</returns>
    public PuloniaRouteRefreshRule ToRefreshRule(string route) => new()
    {
        FileIdentity = route,
        MaterialName = Material,
        Mode = Mode,
        Hours = Hours ?? 0,
        Days = Days ?? 0,
        ResetHour = ResetHour ?? 4,
        ResetWeekday = ResetWeekday ?? 1,
        IsReference = IsReference,
        Source = Source
    };
}
