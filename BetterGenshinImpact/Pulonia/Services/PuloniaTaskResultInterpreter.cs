using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.ResinPlan;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 按任务类型解释节点结果：各执行器的关键结果键、核验边界与资源类别都在这里描述，界面只负责格式化展示。
/// 未识别的扩展字段原样保留，不丢弃扩展执行器的数据。
/// </summary>
public static class PuloniaTaskResultInterpreter
{
    /// <summary>
    /// 奖励汇总在结果数据中的路径，对应 ResinClaimData.Rewards 按 SoloTaskJson 序列化后的 items。
    /// </summary>
    public const string RewardItemsKey = "rewards.items";

    /// <summary>
    /// 体力计划的任务类型；结果按阶段展示。
    /// </summary>
    private static readonly string ResinPlanTaskType = PuloniaSoloTaskExecutor.ToTaskType(ResinPlanTask.TaskId);

    /// <summary>
    /// Shell 执行器的关键结果字段；缺失时标记为未返回，不冒充退出码 0 或空输出。
    /// </summary>
    private static readonly (string Key, string Label)[] ShellKeyFields =
    [
        ("exit_code", "进程退出码"), ("standard_output", "标准输出"), ("standard_error", "标准错误"),
        ("standard_output_truncated", "标准输出已截断"), ("standard_error_truncated", "标准错误已截断")
    ];

    /// <summary>
    /// 地图追踪路线 CD 的结果字段；只在执行器实际写入时展示。
    /// </summary>
    private static readonly string[] CollectionKeys =
    [
        "collection_material", "collection_rule", "collection_rule_source", "collection_last_success_at",
        "collection_next_eligible_at"
    ];

    /// <summary>
    /// 每个地图追踪结果都相同的固定说明（采集完成依据），核验边界已在提示中说明，不逐条展示。
    /// </summary>
    private const string CollectionBasisKey = "collection_basis";

    /// <summary>路线 CD 结果中以 UTC ISO 字符串保存的时间字段，展示时转换为本机时间。</summary>
    private static readonly HashSet<string> CollectionTimeKeys =
        new(["collection_last_success_at", "collection_next_eligible_at"], StringComparer.Ordinal);

    /// <summary>
    /// 各类型结果的核验边界，避免把脚本返回或路线结束解释为已领取/采集成功。
    /// </summary>
    public static string GetVerificationNote(string taskType) => taskType switch
    {
        "pathing" => "路线 CD 按整份文件执行成功估算采集完成，不记录部分成功；不代表逐点采集数量或实际收益。",
        "javascript" => "旧 JS 脚本正常返回不等于游戏副作用已核验；仅展示执行器实际返回的数据与证据。",
        "keymouse" => "宏回放结束不等于游戏目标完成；录制文件本身没有结构化收益证据。",
        "shell" => "退出码表示进程结果；标准输出/错误每路最多保留 65536 字符，截断会明确标记。",
        "csharp" => "返回值与确认事件来自注册的 C# 能力；局部确认不等于整个节点成功。",
        _ when PuloniaSoloTaskExecutor.IsSoloTaskType(taskType) =>
            "独立任务的结果种类与原因码由任务本身给出；“已执行，未核验”表示任务没有确认手段，需要核对游戏状态。奖励只列出任务识别到的物品，未识别时不推算收益。",
        _ => "没有返回领取/合成数量时不推算收益；“已执行，未核验”需要核对游戏状态。"
    };

    /// <summary>
    /// 快照节点资源路径的中文标签。
    /// </summary>
    public static string GetResourcePathLabel(string taskType) => taskType switch
    {
        "pathing" => "路线文件", "javascript" => "JS 项目目录", "keymouse" => "录制文件", _ => "资源路径"
    };

    /// <summary>
    /// 分类型挑选关键结果并保持展示顺序：原因码、奖励、类型关键字段，最后是其余全部字段。
    /// </summary>
    /// <param name="result">节点尝试结果。</param>
    /// <returns>按展示顺序排列的结果项；执行器没有附带任何结构化数据时为空列表。</returns>
    public static IReadOnlyList<PuloniaTaskResultEntry> ReadEntries(PuloniaTaskNodeResult result)
    {
        var entries = new List<PuloniaTaskResultEntry>();
        var handled = new HashSet<string>(StringComparer.Ordinal);
        var data = result.Data;
        // 1. 原因码是机器可读标识，界面放在技术详情中，结果消息已说明原因，这里不重复列出
        // 2. 奖励汇总（ResinClaimData 的 rewards.items）由界面逐行展示为“名称 ×数量”。
        if (TryReadRewards(result, out var rewards))
        {
            handled.Add("rewards");
            entries.Add(new PuloniaTaskResultEntry("奖励", PuloniaTaskResultEntryKind.Rewards, rewards));
        }
        if (data["resin"] is JObject resin)
        {
            try
            {
                var report = SoloTaskJson.Deserialize<ResinUsageReport>(resin.ToString());
                report.Claims ??= new();
                report.Consumed ??= new();
                entries.Add(new PuloniaTaskResultEntry("树脂使用", PuloniaTaskResultEntryKind.Value,
                    new JValue(ResinUsageText.Report(report))));
                handled.Add("resin");
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or ArgumentException)
            {
                // 损坏或其他执行器的 resin 数据留给后面的原始字段展示，不推算出一份报告。
            }
        }
        // 3. 各类型的关键结果
        switch (result.TaskType)
        {
            case "shell":
                foreach (var (key, label) in ShellKeyFields)
                    AddKeyField(entries, handled, data, key, label);
                break;
            case "pathing":
                AddPathingFields(entries, handled, data, PuloniaCollectionCooldown.IsCooldownSkip(result));
                break;
            case "csharp":
                if (data.ContainsKey("sum"))
                {
                    AddKeyField(entries, handled, data, "sum", "计算合计");
                    AddKeyField(entries, handled, data, "value_count", "参与计算的数值数量");
                }
                break;
        }
        if (result.TaskType == ResinPlanTaskType)
            AddResinPlanFields(entries, handled, data);
        // 4. 其余字段全部保留，未登记的扩展键使用原字段名
        foreach (var property in data.Properties().Where(item => !handled.Contains(item.Name)))
            entries.Add(new PuloniaTaskResultEntry(PuloniaTaskFieldNames.GetDisplayName(property.Name),
                PuloniaTaskResultEntryKind.Value, property.Value));
        return entries;
    }

    /// <summary>
    /// 解释体力计划结果：预算一行，每个阶段一行“结果：说明”，奖励已在“奖励”项中合并展示。
    /// </summary>
    private static void AddResinPlanFields(List<PuloniaTaskResultEntry> entries, HashSet<string> handled, JObject data)
    {
        if (data["budget"] is JObject budget)
        {
            handled.Add("budget");
            entries.Add(new PuloniaTaskResultEntry("体力预算", PuloniaTaskResultEntryKind.Value,
                new JValue(FormatResinPlanBudget(budget))));
        }

        // 活动识别结果一行；未执行识别（旧记录或 null）时不展示
        if (data.ContainsKey("events"))
        {
            handled.Add("events");
            if (data["events"] is JObject events)
                entries.Add(new PuloniaTaskResultEntry("活动识别", PuloniaTaskResultEntryKind.Value,
                    new JValue(FormatResinPlanEvents(events))));
        }

        if (data["steps"] is not JArray steps)
            return;
        handled.Add("steps");
        var index = 0;
        foreach (var step in steps.OfType<JObject>())
        {
            index++;
            entries.Add(new PuloniaTaskResultEntry($"第 {index} 步 · {step.Value<string>("name")}",
                PuloniaTaskResultEntryKind.Value, new JValue(FormatResinPlanStep(step))));
        }
    }

    /// <summary>格式化体力计划的预算行。</summary>
    private static string FormatResinPlanBudget(JObject budget)
    {
        var max = budget.Value<int?>("max_resin") ?? 0;
        var condensed = budget["start_condensed"] is { Type: JTokenType.Integer } value ? value.ToString() : "数量未识别";
        var text = (max > 0 ? $"最多 {max} 点" : "全部可用树脂")
                   + $"；开始时原粹 {budget.Value<int?>("start_original") ?? 0}、浓缩 {condensed}"
                   + $"；领奖共消耗 {budget.Value<int?>("spent_points") ?? 0} 点";
        var crafted = budget.Value<int?>("crafted_condensed") ?? 0;
        return crafted > 0 ? text + $"；合成浓缩 {crafted} 个" : text;
    }

    /// <summary>格式化体力计划的活动识别：移涌活动与紊乱爆发期状态。</summary>
    private static string FormatResinPlanEvents(JObject events)
    {
        if (events.Value<bool?>("recognized") != true)
        {
            var message = events.Value<string>("message");
            return string.IsNullOrWhiteSpace(message) ? "未能识别活动页，按没有活动处理" : $"未能识别活动页（{message}），按没有活动处理";
        }

        // 每个移涌活动显示为“名称（今日剩余双倍 3/3）”；未知的活动值（例如后续版本新增）按原文展示
        var overflows = (events["overflows"] as JArray)?.OfType<JObject>()
            .Select(item =>
            {
                var text = item.Value<string>("event") ?? string.Empty;
                var name = Enum.TryParse<ResinPlanOverflowEvent>(text, out var value) ? ResinPlanEventDetector.DisplayName(value) : text;
                var remaining = ResinPlanEventDetector.FormatRemaining(new ResinPlanOverflowStatus
                {
                    RemainingClaims = item.Value<int?>("remaining_claims"),
                    DailyClaims = item.Value<int?>("daily_claims")
                });
                return $"{name}（今日剩余双倍 {remaining}）";
            })
            .ToArray() ?? [];
        var burst = Enum.TryParse<StygianBurstState>(events.Value<string>("stygian_burst"), out var state)
            ? ResinPlanEventDetector.DisplayName(state)
            : "未识别";
        return (overflows.Length > 0 ? "移涌活动：" + string.Join("、", overflows) : "没有移涌活动")
               + "；幽境危战紊乱爆发期：" + burst;
    }

    /// <summary>格式化体力计划的一个阶段：结果、对象、说明与本步奖励。</summary>
    private static string FormatResinPlanStep(JObject step)
    {
        var kind = step.Value<string>("kind") switch
        {
            nameof(SoloTaskResultKind.Succeeded) => "成功",
            nameof(SoloTaskResultKind.Unverified) => "已执行，未核验",
            nameof(SoloTaskResultKind.Partial) => "部分完成",
            nameof(SoloTaskResultKind.Failed) => "失败",
            _ => "跳过"
        };
        var target = step.Value<string>("target");
        var text = string.IsNullOrWhiteSpace(target) ? kind : $"{kind}（{target}）";
        var message = step.Value<string>("message");
        if (!string.IsNullOrWhiteSpace(message))
            text += "：" + message;
        if (step.Value<bool?>("executed") != true)
            return text;

        // 只列出子任务实际识别到的奖励；不具备识别能力的任务明确标注，不推算收益
        var rewards = (step.SelectToken("rewards.items") as JArray)?.OfType<JObject>()
            .Select(item => $"{item.Value<string>("name")} ×{item.Value<int?>("count") ?? 0}").ToArray() ?? [];
        if (rewards.Length > 0)
            text += "；奖励：" + string.Join("、", rewards);
        else if (step.Value<bool?>("rewards_recognized") != true && (step.SelectToken("resin.claim_count")?.Value<int?>() ?? 0) > 0)
            text += "；此任务暂不识别奖励";
        return text;
    }

    /// <summary>
    /// 解释地图追踪结果：实际执行的路线按 is_success 展示完整执行、失败原因与拾取统计；
    /// 执行器异常没有执行结果时只标记“路线完整执行”未返回；因路线 CD 跳过的节点只展示 CD 字段。
    /// </summary>
    /// <param name="entries">结果项列表。</param>
    /// <param name="handled">已处理的字段名。</param>
    /// <param name="data">节点结果数据。</param>
    /// <param name="isCooldownSkip">节点是否因路线 CD 跳过（未执行）。</param>
    private static void AddPathingFields(List<PuloniaTaskResultEntry> entries, HashSet<string> handled, JObject data,
        bool isCooldownSkip)
    {
        if (data.ContainsKey("is_success"))
        {
            AddKeyField(entries, handled, data, "is_success", "路线完整执行");
            AddKeyField(entries, handled, data, "success_fight_count", "成功战斗次数");
            handled.Add("error_message");
            entries.Add(data["error_message"] is { Type: not JTokenType.Null } error
                ? new PuloniaTaskResultEntry("失败原因", PuloniaTaskResultEntryKind.Value, error)
                : new PuloniaTaskResultEntry("失败原因", data.Value<bool>("is_success")
                    ? PuloniaTaskResultEntryKind.NoFailure : PuloniaTaskResultEntryKind.FailureNotProvided));
            handled.Add("revive_count");
            handled.Add("collected_items");
            entries.Add(data["revive_count"] is { Type: not JTokenType.Null } revive
                ? new PuloniaTaskResultEntry("复活次数", PuloniaTaskResultEntryKind.Value, revive)
                : new PuloniaTaskResultEntry("复活次数", PuloniaTaskResultEntryKind.NotCounted));
            entries.Add(data["collected_items"] is JObject items
                ? new PuloniaTaskResultEntry("拾取物品", PuloniaTaskResultEntryKind.CollectedItems, items)
                : new PuloniaTaskResultEntry("拾取物品", PuloniaTaskResultEntryKind.NotCounted));
        }
        else if (!isCooldownSkip)
            // 执行器异常时没有执行结果，明确标记未返回，不推断成功或失败；因 CD 跳过的节点本来就没有执行，不再标注。
            AddKeyField(entries, handled, data, "is_success", "路线完整执行");
        else
            handled.Add("is_success");
        handled.Add(CollectionBasisKey);
        foreach (var key in CollectionKeys.Where(data.ContainsKey))
        {
            if (!CollectionTimeKeys.Contains(key))
            {
                AddKeyField(entries, handled, data, key, PuloniaTaskFieldNames.GetDisplayName(key));
                continue;
            }
            // 时间字段按本机时间展示，与跳过消息和 CD 总览一致；没有时间时显示为“无”。
            handled.Add(key);
            var text = data.Value<string>(key);
            entries.Add(new PuloniaTaskResultEntry(PuloniaTaskFieldNames.GetDisplayName(key), PuloniaTaskResultEntryKind.Value,
                new JValue(DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                    ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "无")));
        }
    }

    /// <summary>
    /// 判断结果数据中的 rewards.items 能否按奖励解释。
    /// 每项必须带 name；空数组只对独立任务解释为“未识别到奖励”，避免误读扩展数据。
    /// </summary>
    /// <param name="result">节点结果。</param>
    /// <param name="rewards">可以按奖励解释的数组；无法解释时为 null。</param>
    /// <returns>能够按奖励解释时返回 true。</returns>
    private static bool TryReadRewards(PuloniaTaskNodeResult result, out JArray? rewards)
    {
        rewards = null;
        if (result.Data.SelectToken(RewardItemsKey) is not JArray items)
            return false;
        var isSoloTask = PuloniaSoloTaskExecutor.IsSoloTaskType(result.TaskType);
        var isRewardShaped = items.All(item => item is JObject entry && entry["name"] is { Type: JTokenType.String });
        if (!isRewardShaped || (items.Count == 0 && !isSoloTask))
            return false;
        rewards = items;
        return true;
    }

    /// <summary>
    /// 登记一个关键结果字段；缺失时标记为未返回，不把缺失 bool 或退出码解释为 false 或 0。
    /// </summary>
    private static void AddKeyField(List<PuloniaTaskResultEntry> entries, HashSet<string> handled,
        JObject data, string key, string label)
    {
        handled.Add(key);
        entries.Add(data.TryGetValue(key, out var value)
            ? new PuloniaTaskResultEntry(label, PuloniaTaskResultEntryKind.Value, value)
            : new PuloniaTaskResultEntry(label, PuloniaTaskResultEntryKind.NotReturned));
    }
}
