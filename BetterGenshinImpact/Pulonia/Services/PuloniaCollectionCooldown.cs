using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 地图追踪文件级采集 CD 的材料目录识别、配置校验和时间计算，不解析路线正文。
/// </summary>
public static class PuloniaCollectionCooldown
{
    /// <summary>整体继承和覆盖的地图追踪参数名。</summary>
    public const string ParameterName = "collection_cd";

    /// <summary>按刷新文档维护的精确目录名与明确别名；不使用模糊匹配。</summary>
    private static readonly IReadOnlyDictionary<string, (string Mode, double Hours, int Days, bool Reference)> Rules = CreateRules();

    /// <summary>创建默认的自动识别配置。</summary>
    public static JObject CreateDefaults() => new() { ["mode"] = "auto" };

    /// <summary>供参数校验和编辑器使用的有限模式及字段 Schema。</summary>
    public static JObject CreateSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JObject
        {
            ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray("auto", "disabled", "rolling", "daily", "calendar_days", "weekly", "once") },
            ["hours"] = new JObject { ["type"] = "number" },
            ["days"] = new JObject { ["type"] = "integer" },
            ["reset_hour"] = new JObject { ["type"] = "integer" },
            ["reset_weekday"] = new JObject { ["type"] = "integer" }
        },
        ["required"] = new JArray("mode"),
        ["additionalProperties"] = false
    };

    /// <summary>校验模式相关数值，拒绝非有限数值和无法表示的日期。</summary>
    public static void ValidateSettings(JToken? value, string location)
    {
        if (value is null) return;
        PuloniaTaskValidator.ValidateParameters(value, CreateSchema(), location);
        var settings = (JObject)value;
        var mode = settings.Value<string>("mode");
        var hour = settings.Value<int?>("reset_hour") ?? 4;
        var weekday = settings.Value<int?>("reset_weekday") ?? 1;
        if (hour is < 0 or > 23 || weekday is < 1 or > 7)
            throw new PuloniaTaskValidationException(location, "重置小时必须为 0—23，星期必须为 1（周一）—7（周日）。");
        try
        {
            if (mode == "rolling")
            {
                var hours = settings.Value<double?>("hours");
                if (hours is null or <= 0 || !double.IsFinite(hours.Value))
                    throw new PuloniaTaskValidationException(location, "相对 CD 小时数必须是有限正数。");
                DateTimeOffset.UtcNow.AddHours(hours.Value);
            }
            if (mode == "calendar_days")
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
    {
        var settings = parameters[ParameterName] as JObject ?? CreateDefaults();
        ValidateSettings(parameters[ParameterName], ParameterName);
        var originalPath = resource?.RelativePath ?? path ?? string.Empty;
        var names = originalPath.Replace('\\', '/').Split('/');
        string? material = null;
        (string Mode, double Hours, int Days, bool Reference) rule = ("disabled", 0, 0, false);
        for (var i = names.Length - 2; i >= 0; i--)
        {
            if (!Rules.TryGetValue(names[i], out rule)) continue;
            material = names[i];
            break;
        }
        if (material is null) rule = ("disabled", 0, 0, false);
        var automatic = settings.Value<string>("mode") == "auto";
        var identity = resource is null
            ? "local:" + (string.IsNullOrEmpty(path) ? "preview" : Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant())
            : "repository:" + resource.RepositoryId + "/" + resource.RelativePath.ToLowerInvariant();
        return new PuloniaCollectionCooldownPolicy
        {
            FileIdentity = identity,
            MaterialName = material,
            Mode = automatic ? rule.Mode : settings.Value<string>("mode")!,
            Hours = automatic ? rule.Hours : settings.Value<double?>("hours") ?? 0,
            Days = automatic ? rule.Days : settings.Value<int?>("days") ?? 0,
            ResetHour = automatic ? 4 : settings.Value<int?>("reset_hour") ?? 4,
            ResetWeekday = settings.Value<int?>("reset_weekday") ?? 1,
            IsReference = automatic && rule.Reference,
            Source = automatic ? material is null || rule.Mode == "disabled" ? "未匹配规则" : "目录自动识别" : "手动覆盖"
        };
    }

    /// <summary>为账本生成不包含计划、版本和内容哈希的文件资源键。</summary>
    public static string GetEffectKey(PuloniaCollectionCooldownPolicy policy)
        => "pathing.file:" + PuloniaTaskResourceFingerprint.ComputeTextVersion(policy.FileIdentity);

    /// <summary>按服务器时区计算严格晚于成功时刻的日历边界；一次性和无限制返回空时间。</summary>
    public static DateTimeOffset? GetNextEligibleAt(PuloniaCollectionCooldownPolicy policy,
        DateTimeOffset succeededAt, int serverUtcOffsetMinutes)
    {
        if (policy.Mode == "rolling") return succeededAt.AddHours(policy.Hours).ToUniversalTime();
        var offset = TimeSpan.FromMinutes(serverUtcOffsetMinutes);
        var local = succeededAt.ToOffset(offset);
        if (policy.Mode == "calendar_days")
            return new DateTimeOffset(local.Date.AddDays(policy.Days), offset).ToUniversalTime();
        if (policy.Mode is not ("daily" or "weekly")) return null;
        var boundary = new DateTimeOffset(local.Date.AddHours(policy.ResetHour), offset);
        if (policy.Mode == "weekly")
            boundary = boundary.AddDays((policy.ResetWeekday % 7 - (int)boundary.DayOfWeek + 7) % 7);
        if (boundary <= local) boundary = boundary.AddDays(policy.Mode == "weekly" ? 7 : 1);
        return boundary.ToUniversalTime();
    }

    /// <summary>查询最新成功事实，规则修改后始终从原成功时间重新计算。</summary>
    internal static PuloniaTaskAvailabilityDecision Evaluate(PuloniaCollectionCooldownPolicy policy,
        PuloniaTaskRequest request, IEnumerable<PuloniaTaskLedgerEntry> ledger, DateTimeOffset now,
        out JObject data, string? verifiedUid = null)
    {
        var scope = PuloniaTaskAvailability.BuildScopeKey(request, PuloniaTaskScopeKind.World, verifiedUid);
        var key = GetEffectKey(policy);
        var latest = ledger.Where(entry => entry.ScopeKey == scope && entry.EffectKey == key)
            .OrderByDescending(entry => entry.OccurredAt).FirstOrDefault();
        var next = latest is null ? null : GetNextEligibleAt(policy, latest.OccurredAt, request.ServerUtcOffsetMinutes);
        data = Describe(policy, latest?.OccurredAt, next);
        if (latest is not null && policy.Mode == "once")
            return new(false, false, null, $"{policy.MaterialName ?? "路线"}为一次性任务，该文件已成功执行，已跳过。");
        if (next > now)
            return new(false, false, next, $"{policy.MaterialName ?? "路线"}仍在 CD，{next.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss} 后可执行，已跳过。规则：{policy.Description}。");
        return new(true, false, next, policy.Description);
    }

    /// <summary>固定本次规则和时间的机器可读说明，供历史和证据复用。</summary>
    public static JObject Describe(PuloniaCollectionCooldownPolicy policy, DateTimeOffset? lastSuccess, DateTimeOffset? next)
        => new()
        {
            ["collection_material"] = policy.MaterialName,
            ["collection_rule"] = policy.Description,
            ["collection_rule_source"] = policy.Source,
            ["collection_last_success_at"] = lastSuccess?.ToUniversalTime().ToString("O"),
            ["collection_next_eligible_at"] = next?.ToUniversalTime().ToString("O"),
            ["collection_basis"] = "按整份文件执行成功估算采集完成"
        };

    /// <summary>建立刷新文档中的明确材料与别名；混合规则不猜测统一时长。</summary>
    private static IReadOnlyDictionary<string, (string Mode, double Hours, int Days, bool Reference)> CreateRules()
    {
        var rules = new Dictionary<string, (string, double, int, bool)>(StringComparer.Ordinal);
        Add("rolling", 46, 0, false, "钩钩果、落落莓、风车菊、塞西莉亚花、塞西莉亚、小灯草、慕风蘑菇、蒲公英籽、蒲公英、嘟嘟莲、空羽蛾、琉璃袋、绝云椒椒、石珀、霓裳花、琉璃百合、清心、星螺、夜泊石、清水玉、海灵芝、鸣草、鬼兜虫、晶化骨髓、血斛、绯樱绣球、天云草实、珊瑚真珠、幽灯蕈、月莲、劫波莲、帕蒂沙兰、帕蒂莎兰、树王圣体菇、圣金虫、赤念果、沙脂蛹、万相石、悼灵花、柔灯铃、虹彩蔷薇、苍晶螺、海露花、幽光星星、子探测单元、湖光铃兰、初露之源、青蜜莓、浪沫羽鳃、肉龙掌、灼灼彩菊、枯叶紫英、微光角菌、云岩裂叶、琉鳞石、霜盏花、便携轴承、月落银、冬凌草、松珀香、凝月泪滴、植绒草、金蕨、霜仙花");
        Add("rolling", 46, 0, true, "冰雾花、冰雾花花朵、烈焰花、烈焰花花蕊、莲蓬");
        Add("rolling", 12, 0, false, "甜甜花、薄荷、蝴蝶翅膀、树莓、野猪、鸟、鸭子、兽肉、禽肉、可捕捉的鱼、鱼肉");
        Add("rolling", 24, 0, false, "蘑菇、松果、苹果、日落果、马尾、胡萝卜、白萝卜、小麦、琥珀、青蛙、螃蟹");
        Add("rolling", 24, 0, true, "电气水晶、蜥蜴、蜥蜴尾巴、发光髓");
        Add("rolling", 72, 0, false, "钓鱼、钓鱼点、钓鱼点鱼群");
        Add("daily", 0, 0, true, "晶核、晶蝶");
        Add("daily", 0, 0, false, "魔物、怪物、普通怪、普通怪物、精英怪、精英怪物、地方传奇、地脉花、盗宝团");
        Add("calendar_days", 0, 1, false, "铁块");
        Add("calendar_days", 0, 2, false, "白铁块、星银矿石");
        Add("calendar_days", 0, 3, false, "水晶块、紫晶块、魔晶块、萃凝晶、虹滴晶");
        Add("once", 0, 0, false, "宝箱、岩之印调查");
        Add("disabled", 0, 0, false, "圣遗物、圣遗物调查点、木材、摩拉盒");
        return rules;

        // 同一档材料集中维护，别名也独立匹配，不把父目录整组作为采集记录。
        void Add(string mode, double hours, int days, bool reference, string names)
        {
            foreach (var name in names.Split('、')) rules.Add(name, (mode, hours, days, reference));
        }
    }
}
