using System;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 地图追踪路线 CD 的设置校验、刷新规则解析、时间计算与状态判定，不解析路线正文。
/// 路线 CD 不单独存储：由刷新规则（计划设置，快照中固定）和完成记录（运行账户的进度）推算。
/// 运行判定、运行预览、任务树标记和 CD 总览都通过 <see cref="GetStatus"/> 得到同一口径的状态。
/// </summary>
public static class PuloniaRouteCooldown
{
    /// <summary>整体继承和覆盖的地图追踪参数名。</summary>
    public const string ParameterName = "route_cd";

    /// <summary>因路线仍在 CD 中跳过的节点结果原因码。</summary>
    public const string ReasonCooldownActive = "pathing.cooldown_active";

    /// <summary>因一次性路线已完成跳过的节点结果原因码。</summary>
    public const string ReasonCooldownCompleted = "pathing.cooldown_completed";

    /// <summary>成功判定的依据说明，历史与界面共用。</summary>
    public const string CompletionBasis = "按整份文件执行成功估算采集完成";

    /// <summary>参数中的模式名与枚举的对照，顺序即表单中的显示顺序。</summary>
    private static readonly (PuloniaRouteRefreshMode Mode, string Name)[] ModeNames =
    [
        (PuloniaRouteRefreshMode.Auto, "auto"), (PuloniaRouteRefreshMode.Disabled, "disabled"),
        (PuloniaRouteRefreshMode.Rolling, "rolling"), (PuloniaRouteRefreshMode.Daily, "daily"),
        (PuloniaRouteRefreshMode.CalendarDays, "calendar_days"), (PuloniaRouteRefreshMode.Weekly, "weekly"),
        (PuloniaRouteRefreshMode.Once, "once")
    ];

    /// <summary>星期显示名，下标 0 为周一。</summary>
    private static readonly string[] WeekdayNames = ["一", "二", "三", "四", "五", "六", "日"];

    /// <summary>校验使用的唯一 Schema；对外只提供副本，避免被嵌入其他对象后修改。</summary>
    private static readonly JObject SchemaTemplate = new()
    {
        ["type"] = "object",
        ["properties"] = new JObject
        {
            ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray(ModeNames.Select(item => item.Name)) },
            ["hours"] = new JObject { ["type"] = "number" },
            ["days"] = new JObject { ["type"] = "integer" },
            ["reset_hour"] = new JObject { ["type"] = "integer" },
            ["reset_weekday"] = new JObject { ["type"] = "integer" }
        },
        ["required"] = new JArray("mode"),
        ["additionalProperties"] = false
    };

    /// <summary>创建默认的自动识别配置。</summary>
    public static JObject CreateDefaults() => new() { ["mode"] = FormatMode(PuloniaRouteRefreshMode.Auto) };

    /// <summary>供参数 Schema 和设置目录嵌入的 Schema 副本。</summary>
    public static JObject CreateSchema() => (JObject)SchemaTemplate.DeepClone();

    /// <summary>模式在参数中的字符串取值。</summary>
    public static string FormatMode(PuloniaRouteRefreshMode mode) => ModeNames.First(item => item.Mode == mode).Name;

    /// <summary>解析参数中的模式字符串。</summary>
    /// <param name="text">参数取值，例如 <c>calendar_days</c>。</param>
    /// <param name="mode">解析出的模式。</param>
    /// <returns>是否为已知模式。</returns>
    public static bool TryParseMode(string? text, out PuloniaRouteRefreshMode mode)
    {
        foreach (var item in ModeNames)
        {
            if (!string.Equals(item.Name, text, StringComparison.Ordinal)) continue;
            mode = item.Mode;
            return true;
        }
        mode = PuloniaRouteRefreshMode.Auto;
        return false;
    }

    /// <summary>校验模式相关数值，拒绝非有限数值和无法表示的日期。</summary>
    public static void ValidateSettings(JToken? value, string location)
    {
        if (value is null) return;
        PuloniaTaskValidator.ValidateParameters(value, SchemaTemplate, location);
        var settings = (JObject)value;
        TryParseMode(settings.Value<string>("mode"), out var mode);
        var hour = settings.Value<int?>("reset_hour") ?? PuloniaServerTime.DayResetHour;
        var weekday = settings.Value<int?>("reset_weekday") ?? 1;
        if (hour is < 0 or > 23 || weekday is < 1 or > 7)
            throw new PuloniaTaskValidationException(location, "重置小时必须为 0—23，星期必须为 1（周一）—7（周日）。");
        try
        {
            if (mode == PuloniaRouteRefreshMode.Rolling)
            {
                var hours = settings.Value<double?>("hours");
                if (hours is null or <= 0 || !double.IsFinite(hours.Value))
                    throw new PuloniaTaskValidationException(location, "相对 CD 小时数必须是有限正数。");
                DateTimeOffset.UtcNow.AddHours(hours.Value);
            }
            if (mode == PuloniaRouteRefreshMode.CalendarDays)
            {
                var days = settings.Value<int?>("days");
                if (days is null or <= 0)
                    throw new PuloniaTaskValidationException(location, "日期间隔必须是正整数天数。");
                DateTimeOffset.UtcNow.AddDays(days.Value);
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new PuloniaTaskValidationException(location, "CD 时长超过可表示的日期范围。", ex);
        }
    }

    /// <summary>从原始来源目录和有效参数生成不可变刷新规则，缓存路径不参与仓库资源身份。</summary>
    public static PuloniaRouteRefreshRule Resolve(string? path, ScriptResourceReference? resource, JObject parameters)
        => CreateRule(GetFileIdentity(path, resource), resource?.RelativePath ?? path ?? string.Empty, parameters[ParameterName]);

    /// <summary>
    /// 从完成记录中的路线身份和一份路线 CD 设置生成刷新规则，供目录引用与 CD 总览在不展开目录时判定。
    /// 路线身份中的路径已转小写，规则表中的中文目录名不受影响。
    /// </summary>
    /// <param name="fileIdentity">路线身份原文。</param>
    /// <param name="settings">有效的路线 CD 设置；为空时按自动识别。</param>
    public static PuloniaRouteRefreshRule ResolveFromFileIdentity(string fileIdentity, JToken? settings)
        => CreateRule(fileIdentity, fileIdentity, settings);

    /// <summary>
    /// 路线身份：仓库文件为 <c>repository:{仓库 ID}/{小写相对路径}</c>，本地文件为 <c>local:{小写规范化绝对路径}</c>。
    /// 不含计划、节点、版本或内容哈希；完成记录直接以它区分路线。
    /// </summary>
    public static string GetFileIdentity(string? path, ScriptResourceReference? resource) => resource is null
        ? "local:" + (string.IsNullOrEmpty(path) ? "preview" : Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant())
        : "repository:" + resource.RepositoryId + "/" + resource.RelativePath.ToLowerInvariant();

    /// <summary>按服务器时区计算严格晚于成功时刻的日历边界；一次性和不检查返回空时间。</summary>
    public static DateTimeOffset? GetNextEligibleAt(PuloniaRouteRefreshRule rule,
        DateTimeOffset succeededAt, int serverUtcOffsetMinutes)
    {
        if (rule.Mode == PuloniaRouteRefreshMode.Rolling) return succeededAt.AddHours(rule.Hours).ToUniversalTime();
        var offset = TimeSpan.FromMinutes(serverUtcOffsetMinutes);
        var local = succeededAt.ToOffset(offset);
        // 日期间隔按服务器时区的午夜保守估算，不按游戏日切换。
        if (rule.Mode == PuloniaRouteRefreshMode.CalendarDays)
            return new DateTimeOffset(local.Date.AddDays(rule.Days), offset).ToUniversalTime();
        if (rule.Mode is not (PuloniaRouteRefreshMode.Daily or PuloniaRouteRefreshMode.Weekly)) return null;
        // 下一游戏日开始的那一刻就是每日边界；成功恰在边界上时属于新的游戏日，下次边界顺延一天。
        var next = PuloniaServerTime.GetGameDate(succeededAt, offset, rule.ResetHour).AddDays(1);
        // 每周边界再顺延到指定星期（ResetWeekday：1 为周一、7 为周日）。
        if (rule.Mode == PuloniaRouteRefreshMode.Weekly)
            next = next.AddDays((rule.ResetWeekday % 7 - (int)next.DayOfWeek + 7) % 7);
        return new DateTimeOffset(next.AddHours(rule.ResetHour), offset).ToUniversalTime();
    }

    /// <summary>
    /// 唯一的状态判定：规则修改后始终从原完成时间按当前规则重新计算，完成记录不保存下次可执行时间。
    /// </summary>
    /// <param name="rule">节点固定或当前有效的刷新规则。</param>
    /// <param name="latest">该账户这份路线的完成记录；没有时为空。</param>
    /// <param name="now">判定时刻。</param>
    /// <param name="serverUtcOffsetMinutes">账户服务器相对 UTC 的分钟偏移。</param>
    public static PuloniaRouteCooldownStatus GetStatus(PuloniaRouteRefreshRule rule,
        PuloniaRouteCompletion? latest, DateTimeOffset now, int serverUtcOffsetMinutes)
    {
        var next = latest is null ? null : GetNextEligibleAt(rule, latest.CompletedAt, serverUtcOffsetMinutes);
        var state = latest is not null && rule.Mode == PuloniaRouteRefreshMode.Once
            ? PuloniaRouteCooldownState.Completed
            : next > now ? PuloniaRouteCooldownState.CoolingDown
            : !rule.IsChecked
                ? rule.Source == PuloniaRouteRefreshRuleSource.Unmatched
                    ? PuloniaRouteCooldownState.Unmatched : PuloniaRouteCooldownState.NotChecked
                : PuloniaRouteCooldownState.Ready;
        return new PuloniaRouteCooldownStatus(state, latest?.CompletedAt, next, latest);
    }

    /// <summary>跳过节点的原因码；不跳过时为空。</summary>
    public static string? GetReasonCode(PuloniaRouteCooldownStatus status) => status.State switch
    {
        PuloniaRouteCooldownState.CoolingDown => ReasonCooldownActive,
        PuloniaRouteCooldownState.Completed => ReasonCooldownCompleted,
        _ => null
    };

    /// <summary>节点结果是否因路线 CD 跳过（按原因码判断，不解析消息文字）。</summary>
    public static bool IsCooldownSkip(PuloniaTaskNodeResult result)
        => result.ReasonCode is ReasonCooldownActive or ReasonCooldownCompleted;

    /// <summary>跳过原因的说明，时间按本机时区显示。</summary>
    public static string FormatSkipReason(PuloniaRouteRefreshRule rule, PuloniaRouteCooldownStatus status)
        => status.State == PuloniaRouteCooldownState.Completed
            ? $"一次性路线已成功执行，不再自动执行（{rule.MaterialName ?? "路线"}）。"
            : $"仍在 CD，{status.NextEligibleAt?.ToLocalTime():yyyy-MM-dd HH:mm} 后可执行（{rule.MaterialName ?? "路线"} · {rule.Description}）。";

    /// <summary>
    /// 固定本次规则和时间的机器可读说明，写入地图追踪节点结果的 <c>route_*</c> 数据；时间为 UTC ISO 字符串，界面显示时转换。
    /// </summary>
    public static JObject Describe(PuloniaRouteRefreshRule rule, DateTimeOffset? lastSuccess, DateTimeOffset? next)
        => new()
        {
            ["route_material"] = rule.MaterialName,
            ["route_rule"] = rule.Description,
            ["route_rule_source"] = rule.Source switch
            {
                PuloniaRouteRefreshRuleSource.Directory => "目录自动识别",
                PuloniaRouteRefreshRuleSource.Mixed => "混合类别",
                PuloniaRouteRefreshRuleSource.Manual => "手动设置",
                _ => "未识别"
            },
            ["route_last_success_at"] = lastSuccess?.ToUniversalTime().ToString("O"),
            ["route_next_eligible_at"] = next?.ToUniversalTime().ToString("O"),
            ["route_basis"] = CompletionBasis
        };

    /// <summary>
    /// 把一份路线 CD 设置概括为短文字，用于共享设置行；与表单的三种选择一致。
    /// </summary>
    /// <param name="value">设置值；为空或无法解析时按自动识别。</param>
    public static string DescribeSettings(JToken? value)
    {
        if (value is not JObject settings || !TryParseMode(settings.Value<string>("mode"), out var mode))
            return "自动识别";
        var hour = settings.Value<int?>("reset_hour") ?? PuloniaServerTime.DayResetHour;
        var weekday = Math.Clamp(settings.Value<int?>("reset_weekday") ?? 1, 1, 7);
        return mode switch
        {
            PuloniaRouteRefreshMode.Disabled => "不检查",
            PuloniaRouteRefreshMode.Rolling => $"每 {settings.Value<double?>("hours") ?? 0:g} 小时",
            PuloniaRouteRefreshMode.Daily => $"每日 {hour:00}:00",
            PuloniaRouteRefreshMode.Weekly => $"每周{WeekdayNames[weekday - 1]} {hour:00}:00",
            PuloniaRouteRefreshMode.CalendarDays => $"成功日期后第 {settings.Value<int?>("days") ?? 0} 天 00:00",
            PuloniaRouteRefreshMode.Once => "只执行一次",
            _ => "自动识别"
        };
    }

    /// <summary>按有效设置和识别结果建立刷新规则；自动识别忽略设置中的其他字段。</summary>
    /// <param name="identity">路线身份。</param>
    /// <param name="materialPath">用于识别材料的原始路径或路线身份。</param>
    /// <param name="settingsToken">有效的路线 CD 设置；为空时按自动识别。</param>
    private static PuloniaRouteRefreshRule CreateRule(string identity, string materialPath, JToken? settingsToken)
    {
        ValidateSettings(settingsToken, ParameterName);
        var settings = settingsToken as JObject ?? CreateDefaults();
        TryParseMode(settings.Value<string>("mode"), out var mode);
        var matched = PuloniaRouteRefreshRuleCatalog.TryMatch(materialPath, out var material, out var rule);
        if (mode == PuloniaRouteRefreshMode.Auto)
            return new PuloniaRouteRefreshRule
            {
                FileIdentity = identity,
                MaterialName = matched ? material : null,
                Mode = matched ? rule.Mode : PuloniaRouteRefreshMode.Disabled,
                Hours = rule.Hours,
                Days = rule.Days,
                ResetHour = PuloniaServerTime.DayResetHour,
                IsReference = matched && rule.IsReference,
                Source = !matched ? PuloniaRouteRefreshRuleSource.Unmatched
                    : rule.Mode == PuloniaRouteRefreshMode.Disabled ? PuloniaRouteRefreshRuleSource.Mixed
                    : PuloniaRouteRefreshRuleSource.Directory
            };
        return new PuloniaRouteRefreshRule
        {
            FileIdentity = identity,
            MaterialName = matched ? material : null,
            Mode = mode,
            Hours = settings.Value<double?>("hours") ?? 0,
            Days = settings.Value<int?>("days") ?? 0,
            ResetHour = settings.Value<int?>("reset_hour") ?? PuloniaServerTime.DayResetHour,
            ResetWeekday = settings.Value<int?>("reset_weekday") ?? 1,
            Source = PuloniaRouteRefreshRuleSource.Manual
        };
    }
}
