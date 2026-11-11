using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 单份地图追踪路线的只读刷新规则；准备时固定进快照（字段 <c>refresh_rule</c>），不随排队期间的设置变化。
/// </summary>
public sealed class PuloniaRouteRefreshRule
{
    /// <summary>星期显示名，下标 0 为周一。</summary>
    private static readonly string[] WeekdayNames = ["一", "二", "三", "四", "五", "六", "日"];

    /// <summary>
    /// 路线身份：不含资源版本、计划和节点身份的原始文件定位（<c>repository:</c> 或 <c>local:</c> 开头）。
    /// JSON 字段名沿用 <c>file_identity</c>，已固定的快照只需改外层字段名。
    /// </summary>
    public string FileIdentity { get; init; } = string.Empty;

    /// <summary>从原始资源目录识别的材料或类别目录名；未识别时为空。</summary>
    public string? MaterialName { get; init; }

    /// <summary>实际生效模式：不会是 <see cref="PuloniaRouteRefreshMode.Auto"/>。</summary>
    [JsonConverter(typeof(StringEnumConverter), typeof(SnakeCaseNamingStrategy))]
    public PuloniaRouteRefreshMode Mode { get; init; } = PuloniaRouteRefreshMode.Disabled;

    /// <summary>相对计时的小时数。</summary>
    public double Hours { get; init; }

    /// <summary>午夜日期估算的间隔天数。</summary>
    public int Days { get; init; }

    /// <summary>服务器重置小时。</summary>
    public int ResetHour { get; init; } = 4;

    /// <summary>每周重置的星期，1 为周一，7 为周日。</summary>
    public int ResetWeekday { get; init; } = 1;

    /// <summary>是否采用文档标明的不确定参考值。</summary>
    public bool IsReference { get; init; }

    /// <summary>规则来自目录自动识别、混合类别、未匹配或手动设置。</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public PuloniaRouteRefreshRuleSource Source { get; init; } = PuloniaRouteRefreshRuleSource.Unmatched;

    /// <summary>是否检查 CD；不检查时成功后仍记录时间。</summary>
    [JsonIgnore]
    public bool IsChecked => Mode != PuloniaRouteRefreshMode.Disabled;

    /// <summary>可读规则说明，用于设置预览、跳过原因和运行历史；规则的界面文字只在这里生成。</summary>
    [JsonIgnore]
    public string Description => (Mode switch
    {
        PuloniaRouteRefreshMode.Rolling => $"成功后 {Hours:g} 小时",
        PuloniaRouteRefreshMode.Daily => $"服务器每日 {ResetHour:00}:00 重置",
        PuloniaRouteRefreshMode.CalendarDays => $"成功日期后第 {Days} 天 00:00（午夜保守估算）",
        PuloniaRouteRefreshMode.Weekly => $"服务器每周{WeekdayNames[ResetWeekday - 1]} {ResetHour:00}:00 重置",
        PuloniaRouteRefreshMode.Once => "一次性：成功后不再自动执行",
        _ => Source switch
        {
            PuloniaRouteRefreshRuleSource.Unmatched => "未识别刷新规则：不检查 CD，成功后仍记录时间，可手动设置",
            PuloniaRouteRefreshRuleSource.Mixed => "混合类别：不检查 CD",
            _ => "不检查 CD"
        }
    }) + (IsReference ? "（参考规则）" : string.Empty);
}
