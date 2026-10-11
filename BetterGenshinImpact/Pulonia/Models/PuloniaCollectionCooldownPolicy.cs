using Newtonsoft.Json;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 单份地图追踪文件的只读采集 CD 规则；准备时固定，不随排队期间的设置变化。
/// </summary>
public sealed class PuloniaCollectionCooldownPolicy
{
    /// <summary>不含资源版本、计划和节点身份的原始文件定位。</summary>
    public string FileIdentity { get; init; } = string.Empty;

    /// <summary>从原始资源目录识别的材料或类别；未知时为空。</summary>
    public string? MaterialName { get; init; }

    /// <summary>实际生效模式：disabled、rolling、daily、calendar_days、weekly 或 once。</summary>
    public string Mode { get; init; } = "disabled";

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

    /// <summary>规则来自目录自动识别、手动覆盖或未匹配。</summary>
    public string Source { get; init; } = "未匹配规则";

    /// <summary>可读规则说明，用于设置预览和运行历史。</summary>
    [JsonIgnore]
    public string Description => (Mode switch
    {
        "rolling" => $"成功后 {Hours:g} 小时",
        "daily" => $"服务器每日 {ResetHour:00}:00 重置",
        "calendar_days" => $"成功日期后第 {Days} 天 00:00（午夜保守估算）",
        "weekly" => $"服务器每周{new[] { "一", "二", "三", "四", "五", "六", "日" }[ResetWeekday - 1]} {ResetHour:00}:00 重置",
        "once" => "一次性：成功后不再自动执行",
        _ => Source == "未匹配规则" ? "未自动设置 CD，执行成功仍记录时间，可手动配置" : "已关闭 CD 限制"
    }) + (IsReference ? "（参考规则）" : string.Empty);
}
