using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 地图追踪路线 CD 的配置校验、规则解析、时间计算与状态判定，不解析路线正文。
/// 运行判定、运行预览、任务树标记和 CD 总览都通过 <see cref="GetStatus"/> 得到同一口径的状态。
/// </summary>
public static class PuloniaCollectionCooldown
{
    /// <summary>整体继承和覆盖的地图追踪参数名。</summary>
    public const string ParameterName = "collection_cd";

    /// <summary>因路线仍在 CD 中跳过的节点结果原因码。</summary>
    public const string ReasonCooldownActive = "pathing.cooldown_active";

    /// <summary>因一次性路线已完成跳过的节点结果原因码。</summary>
    public const string ReasonCooldownCompleted = "pathing.cooldown_completed";

    /// <summary>资源键前缀，后接文件身份原文。</summary>
    public const string EffectKeyPrefix = "pathing.file:";

    /// <summary>
    /// 路线 CD 作用域键的固定前缀；格式沿用 <c>cn/world/{身份}</c>，已有账本记录的键仍然有效。
    /// </summary>
    public const string ScopeKeyPrefix = "cn/world/";

    /// <summary>
    /// 未切换账号（默认账号或没有账号准备）时的作用域身份：代表“当前登录账号”，普通计划与一条龙默认账号共用。
    /// </summary>
    public const string DefaultScopeIdentity = "default";

    /// <summary>成功判定的依据说明，历史与界面共用。</summary>
    public const string CompletionBasis = "按整份文件执行成功估算采集完成";

    /// <summary>参数中的模式名与枚举的对照，顺序即表单中的显示顺序。</summary>
    private static readonly (PuloniaCollectionCooldownMode Mode, string Name)[] ModeNames =
    [
        (PuloniaCollectionCooldownMode.Auto, "auto"), (PuloniaCollectionCooldownMode.Disabled, "disabled"),
        (PuloniaCollectionCooldownMode.Rolling, "rolling"), (PuloniaCollectionCooldownMode.Daily, "daily"),
        (PuloniaCollectionCooldownMode.CalendarDays, "calendar_days"), (PuloniaCollectionCooldownMode.Weekly, "weekly"),
        (PuloniaCollectionCooldownMode.Once, "once")
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
    public static JObject CreateDefaults() => new() { ["mode"] = FormatMode(PuloniaCollectionCooldownMode.Auto) };

    /// <summary>供参数 Schema 和设置目录嵌入的 Schema 副本。</summary>
    public static JObject CreateSchema() => (JObject)SchemaTemplate.DeepClone();

    /// <summary>模式在参数中的字符串取值。</summary>
    public static string FormatMode(PuloniaCollectionCooldownMode mode) => ModeNames.First(item => item.Mode == mode).Name;

    /// <summary>解析参数中的模式字符串。</summary>
    /// <param name="text">参数取值，例如 <c>calendar_days</c>。</param>
    /// <param name="mode">解析出的模式。</param>
    /// <returns>是否为已知模式。</returns>
    public static bool TryParseMode(string? text, out PuloniaCollectionCooldownMode mode)
    {
        foreach (var item in ModeNames)
        {
            if (!string.Equals(item.Name, text, StringComparison.Ordinal)) continue;
            mode = item.Mode;
            return true;
        }
        mode = PuloniaCollectionCooldownMode.Auto;
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
            if (mode == PuloniaCollectionCooldownMode.Rolling)
            {
                var hours = settings.Value<double?>("hours");
                if (hours is null or <= 0 || !double.IsFinite(hours.Value))
                    throw new PuloniaTaskValidationException(location, "相对 CD 小时数必须是有限正数。");
                DateTimeOffset.UtcNow.AddHours(hours.Value);
            }
            if (mode == PuloniaCollectionCooldownMode.CalendarDays)
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

    /// <summary>从原始来源目录和有效参数生成不可变规则，缓存路径不参与仓库资源身份。</summary>
    public static PuloniaCollectionCooldownPolicy Resolve(string? path, ScriptResourceReference? resource, JObject parameters)
        => CreatePolicy(GetFileIdentity(path, resource), resource?.RelativePath ?? path ?? string.Empty, parameters[ParameterName]);

    /// <summary>
    /// 从账本中的文件身份和一份路线 CD 设置生成规则，供目录引用与 CD 总览在不展开目录时判定。
    /// 文件身份中的路径已转小写，规则表中的中文目录名不受影响。
    /// </summary>
    /// <param name="fileIdentity">文件身份原文。</param>
    /// <param name="settings">有效的路线 CD 设置；为空时按自动识别。</param>
    public static PuloniaCollectionCooldownPolicy ResolveFromFileIdentity(string fileIdentity, JToken? settings)
        => CreatePolicy(fileIdentity, fileIdentity, settings);

    /// <summary>
    /// 文件身份：仓库文件为 <c>repository:{仓库 ID}/{小写相对路径}</c>，本地文件为 <c>local:{小写规范化绝对路径}</c>。
    /// 不含计划、节点、版本或内容哈希。
    /// </summary>
    public static string GetFileIdentity(string? path, ScriptResourceReference? resource) => resource is null
        ? "local:" + (string.IsNullOrEmpty(path) ? "preview" : Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant())
        : "repository:" + resource.RepositoryId + "/" + resource.RelativePath.ToLowerInvariant();

    /// <summary>
    /// 为账本生成不包含计划、版本和内容哈希的文件资源键；直接使用文件身份原文，历史中能看出是哪个文件。
    /// </summary>
    public static string GetEffectKey(PuloniaCollectionCooldownPolicy policy) => EffectKeyPrefix + policy.FileIdentity;

    /// <summary>从资源键取出文件身份；不是路线文件的资源键时返回 false。</summary>
    public static bool TryGetFileIdentity(string effectKey, out string fileIdentity)
    {
        var matched = effectKey.StartsWith(EffectKeyPrefix, StringComparison.Ordinal);
        fileIdentity = matched ? effectKey[EffectKeyPrefix.Length..] : string.Empty;
        return matched;
    }

    /// <summary>按服务器时区计算严格晚于成功时刻的日历边界；一次性和不检查返回空时间。</summary>
    public static DateTimeOffset? GetNextEligibleAt(PuloniaCollectionCooldownPolicy policy,
        DateTimeOffset succeededAt, int serverUtcOffsetMinutes)
    {
        if (policy.Mode == PuloniaCollectionCooldownMode.Rolling) return succeededAt.AddHours(policy.Hours).ToUniversalTime();
        var offset = TimeSpan.FromMinutes(serverUtcOffsetMinutes);
        var local = succeededAt.ToOffset(offset);
        // 日期间隔按服务器时区的午夜保守估算，不按游戏日切换。
        if (policy.Mode == PuloniaCollectionCooldownMode.CalendarDays)
            return new DateTimeOffset(local.Date.AddDays(policy.Days), offset).ToUniversalTime();
        if (policy.Mode is not (PuloniaCollectionCooldownMode.Daily or PuloniaCollectionCooldownMode.Weekly)) return null;
        // 下一游戏日开始的那一刻就是每日边界；成功恰在边界上时属于新的游戏日，下次边界顺延一天。
        var next = PuloniaServerTime.GetGameDate(succeededAt, offset, policy.ResetHour).AddDays(1);
        // 每周边界再顺延到指定星期（ResetWeekday：1 为周一、7 为周日）。
        if (policy.Mode == PuloniaCollectionCooldownMode.Weekly)
            next = next.AddDays((policy.ResetWeekday % 7 - (int)next.DayOfWeek + 7) % 7);
        return new DateTimeOffset(next.AddHours(policy.ResetHour), offset).ToUniversalTime();
    }

    /// <summary>
    /// 建立路线 CD 所属世界的作用域键，不依赖计划名、节点 ID 或账号资料 ID。
    /// 同一真实账号在普通计划与一条龙之间共享 CD：未切换账号时都用 <see cref="DefaultScopeIdentity"/>，切换到指定账号时用核验后的 UID。
    /// </summary>
    /// <param name="switchedAccountUid">切换到指定账号后核验的实际 UID；未切换账号时为空。</param>
    /// <returns>作用域键。</returns>
    public static string BuildScopeKey(string? switchedAccountUid = null)
        => ScopeKeyPrefix + (switchedAccountUid ?? DefaultScopeIdentity);

    /// <summary>在账本中查找（作用域，文件）的最新记录；按“每个身份只保留一条”写入，这里仍取最晚的一条。</summary>
    public static PuloniaTaskLedgerEntry? FindLatest(IEnumerable<PuloniaTaskLedgerEntry> ledger, string scopeKey, string effectKey)
        => ledger.Where(entry => entry.ScopeKey == scopeKey && entry.EffectKey == effectKey)
            .OrderByDescending(entry => entry.OccurredAt).FirstOrDefault();

    /// <summary>
    /// 唯一的状态判定：规则修改后始终从原成功时间按当前规则重新计算，不使用记录时存下的下次可执行时间。
    /// </summary>
    /// <param name="policy">节点固定或当前有效的规则。</param>
    /// <param name="latest">该（作用域，文件）的最新成功记录；没有时为空。</param>
    /// <param name="now">判定时刻。</param>
    /// <param name="serverUtcOffsetMinutes">服务器相对 UTC 的分钟偏移。</param>
    public static PuloniaCollectionCooldownStatus GetStatus(PuloniaCollectionCooldownPolicy policy,
        PuloniaTaskLedgerEntry? latest, DateTimeOffset now, int serverUtcOffsetMinutes)
    {
        var next = latest is null ? null : GetNextEligibleAt(policy, latest.OccurredAt, serverUtcOffsetMinutes);
        var state = latest is not null && policy.Mode == PuloniaCollectionCooldownMode.Once
            ? PuloniaCollectionCooldownState.Completed
            : next > now ? PuloniaCollectionCooldownState.CoolingDown
            : !policy.IsChecked
                ? policy.Source == PuloniaCollectionCooldownRuleSource.Unmatched
                    ? PuloniaCollectionCooldownState.Unmatched : PuloniaCollectionCooldownState.NotChecked
                : PuloniaCollectionCooldownState.Ready;
        return new PuloniaCollectionCooldownStatus(state, latest?.OccurredAt, next, latest);
    }

    /// <summary>跳过节点的原因码；不跳过时为空。</summary>
    public static string? GetReasonCode(PuloniaCollectionCooldownStatus status) => status.State switch
    {
        PuloniaCollectionCooldownState.CoolingDown => ReasonCooldownActive,
        PuloniaCollectionCooldownState.Completed => ReasonCooldownCompleted,
        _ => null
    };

    /// <summary>节点结果是否因路线 CD 跳过（按原因码判断，不解析消息文字）。</summary>
    public static bool IsCooldownSkip(PuloniaTaskNodeResult result)
        => result.ReasonCode is ReasonCooldownActive or ReasonCooldownCompleted;

    /// <summary>跳过原因的说明，时间按本机时区显示。</summary>
    public static string FormatSkipReason(PuloniaCollectionCooldownPolicy policy, PuloniaCollectionCooldownStatus status)
        => status.State == PuloniaCollectionCooldownState.Completed
            ? $"一次性路线已成功执行，不再自动执行（{policy.MaterialName ?? "路线"}）。"
            : $"仍在 CD，{status.NextEligibleAt?.ToLocalTime():yyyy-MM-dd HH:mm} 后可执行（{policy.MaterialName ?? "路线"} · {policy.Description}）。";

    /// <summary>固定本次规则和时间的机器可读说明，供历史和证据复用；时间为 UTC ISO 字符串，界面显示时转换。</summary>
    public static JObject Describe(PuloniaCollectionCooldownPolicy policy, DateTimeOffset? lastSuccess, DateTimeOffset? next)
        => new()
        {
            ["collection_material"] = policy.MaterialName,
            ["collection_rule"] = policy.Description,
            ["collection_rule_source"] = policy.Source switch
            {
                PuloniaCollectionCooldownRuleSource.Directory => "目录自动识别",
                PuloniaCollectionCooldownRuleSource.Mixed => "混合类别",
                PuloniaCollectionCooldownRuleSource.Manual => "手动设置",
                _ => "未识别"
            },
            ["collection_last_success_at"] = lastSuccess?.ToUniversalTime().ToString("O"),
            ["collection_next_eligible_at"] = next?.ToUniversalTime().ToString("O"),
            ["collection_basis"] = CompletionBasis
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
            PuloniaCollectionCooldownMode.Disabled => "不检查",
            PuloniaCollectionCooldownMode.Rolling => $"每 {settings.Value<double?>("hours") ?? 0:g} 小时",
            PuloniaCollectionCooldownMode.Daily => $"每日 {hour:00}:00",
            PuloniaCollectionCooldownMode.Weekly => $"每周{WeekdayNames[weekday - 1]} {hour:00}:00",
            PuloniaCollectionCooldownMode.CalendarDays => $"成功日期后第 {settings.Value<int?>("days") ?? 0} 天 00:00",
            PuloniaCollectionCooldownMode.Once => "只执行一次",
            _ => "自动识别"
        };
    }

    /// <summary>
    /// 把一次文件成功写入账本：同一（作用域，文件）只保留这条最新记录，旧记录的明细已在对应运行历史的确认事件中。
    /// </summary>
    /// <remarks>新成功总是刚刚发生，按“后写入即最新”替换，不处理系统时钟回拨造成的时间倒序。</remarks>
    /// <param name="ledger">候选状态中的账本，会被直接修改。</param>
    /// <param name="entry">本次成功事实。</param>
    internal static void PutLatest(List<PuloniaTaskLedgerEntry> ledger, PuloniaTaskLedgerEntry entry)
    {
        ledger.RemoveAll(item => item.ScopeKey == entry.ScopeKey && item.EffectKey == entry.EffectKey);
        ledger.Add(entry);
    }

    /// <summary>删除指定（作用域，文件）的记录；运行历史中的确认事件不受影响。</summary>
    /// <param name="ledger">候选状态中的账本，会被直接修改。</param>
    /// <param name="keys">要删除的记录身份。</param>
    /// <returns>删除的条目数。</returns>
    internal static int RemoveEntries(List<PuloniaTaskLedgerEntry> ledger, IReadOnlyCollection<PuloniaCollectionCooldownKey> keys)
    {
        var targets = keys.ToHashSet();
        return ledger.RemoveAll(item => targets.Contains(new PuloniaCollectionCooldownKey(item.ScopeKey, item.EffectKey)));
    }

    /// <summary>按有效设置和识别结果建立规则；自动识别忽略设置中的其他字段。</summary>
    /// <param name="identity">文件身份。</param>
    /// <param name="materialPath">用于识别材料的原始路径或文件身份。</param>
    /// <param name="settingsToken">有效的路线 CD 设置；为空时按自动识别。</param>
    private static PuloniaCollectionCooldownPolicy CreatePolicy(string identity, string materialPath, JToken? settingsToken)
    {
        ValidateSettings(settingsToken, ParameterName);
        var settings = settingsToken as JObject ?? CreateDefaults();
        TryParseMode(settings.Value<string>("mode"), out var mode);
        var matched = PuloniaCollectionCooldownRules.TryMatch(materialPath, out var material, out var rule);
        if (mode == PuloniaCollectionCooldownMode.Auto)
            return new PuloniaCollectionCooldownPolicy
            {
                FileIdentity = identity,
                MaterialName = matched ? material : null,
                Mode = matched ? rule.Mode : PuloniaCollectionCooldownMode.Disabled,
                Hours = rule.Hours,
                Days = rule.Days,
                ResetHour = PuloniaServerTime.DayResetHour,
                IsReference = matched && rule.IsReference,
                Source = !matched ? PuloniaCollectionCooldownRuleSource.Unmatched
                    : rule.Mode == PuloniaCollectionCooldownMode.Disabled ? PuloniaCollectionCooldownRuleSource.Mixed
                    : PuloniaCollectionCooldownRuleSource.Directory
            };
        return new PuloniaCollectionCooldownPolicy
        {
            FileIdentity = identity,
            MaterialName = matched ? material : null,
            Mode = mode,
            Hours = settings.Value<double?>("hours") ?? 0,
            Days = settings.Value<int?>("days") ?? 0,
            ResetHour = settings.Value<int?>("reset_hour") ?? PuloniaServerTime.DayResetHour,
            ResetWeekday = settings.Value<int?>("reset_weekday") ?? 1,
            Source = PuloniaCollectionCooldownRuleSource.Manual
        };
    }
}
