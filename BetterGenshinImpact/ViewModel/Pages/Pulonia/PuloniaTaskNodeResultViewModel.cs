using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 节点尝试的类型专属详情，只使用运行快照与真实结果，不读取已经编辑过的计划或资源。
/// </summary>
public sealed class PuloniaTaskNodeResultViewModel : ObservableObject
{
    /// <summary>
    /// 奖励汇总在结果数据中的键，对应 RewardSummary 按 SoloTaskJson 序列化后的 items。
    /// </summary>
    private const string RewardItemsKey = "items";

    /// <summary>
    /// 本次尝试的原始结果。
    /// </summary>
    public PuloniaTaskNodeResult Result { get; }

    /// <summary>
    /// 快照中的稳定节点地址。
    /// </summary>
    public string TaskAddress => Result.TaskAddress;

    /// <summary>
    /// 提交时固定的节点名称。
    /// </summary>
    public string TaskName => Result.TaskName;

    /// <summary>
    /// 节点尝试次数，供刷新后恢复选择。
    /// </summary>
    public int Attempt => Result.Attempt;

    /// <summary>
    /// 当前类型的中文名称。
    /// </summary>
    public string TypeText { get; }

    /// <summary>
    /// 调度状态与实际完成程度。
    /// </summary>
    public string StatusText => $"{PuloniaTaskHistoryText.NodeStatus(Result.Status)} · {PuloniaTaskHistoryText.Outcome(Result.OutcomeKind)}";

    /// <summary>
    /// 本次尝试的时间与耗时摘要。
    /// </summary>
    public string TimingText => $"{PuloniaTaskHistoryText.Time(Result.StartedAt)} → {PuloniaTaskHistoryText.Time(Result.FinishedAt)} · {PuloniaTaskHistoryText.Duration(Result.StartedAt, Result.FinishedAt)}";

    /// <summary>
    /// 类型、重试轮次与证据数量摘要。
    /// </summary>
    public string SummaryText => $"{TypeText} · {(Attempt == 0 ? "未执行" : $"尝试 {Attempt}")} · 证据 {Result.Evidence.Count} 条";

    /// <summary>
    /// 执行器原始消息或异常原因。
    /// </summary>
    public string Message => Result.Message;

    /// <summary>
    /// 各类型结果的核验边界，避免把脚本返回或路线结束解释为已领取/采集成功。
    /// </summary>
    public string VerificationNote { get; }

    /// <summary>
    /// 类型专属的结果字段，保留未知扩展数据。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> ResultFields { get; }

    /// <summary>
    /// 原快照的资源与有效输入参数。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> InputFields { get; }

    /// <summary>
    /// 可追溯的结构化证据及已持久化副作用。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> EvidenceFields { get; }

    /// <summary>
    /// 未格式化删减的节点原始 JSON，用于排查扩展执行器结果。
    /// </summary>
    public string RawJson => PuloniaTaskJson.Write(Result);

    /// <summary>
    /// 从固定快照建立类型专属展示，旧历史缺少输入或证据时明确说明。
    /// </summary>
    public PuloniaTaskNodeResultViewModel(PuloniaTaskNodeResult result, PuloniaTaskPreparedTask? task,
        string typeText, IEnumerable<PuloniaTaskConfirmedEffect> confirmedEffects)
    {
        Result = result;
        TypeText = typeText;
        VerificationNote = result.TaskType switch
        {
            "pathing" => "文件级 CD 按整份文件执行成功估算采集完成，不记录部分成功；不代表逐点采集数量或实际收益，旧记录不回填采集时间。",
            "javascript" => "旧 JS 脚本正常返回不等于游戏副作用已核验；仅展示执行器实际返回的数据与证据。",
            "keymouse" => "宏回放结束不等于游戏目标完成；录制文件本身没有结构化收益证据。",
            "shell" => "退出码表示进程结果；标准输出/错误每路最多保留 65536 字符，截断会明确标记。",
            "csharp" => "返回值与确认事件来自注册的 C# 能力；局部确认不等于整个节点成功。",
            _ when PuloniaSoloTaskExecutor.IsSoloTaskType(result.TaskType) =>
                "独立任务的结果种类与原因码由任务本身给出；“已执行，未核验”表示任务没有确认手段，需要核对游戏状态。奖励只列出任务识别到的物品，未识别时不推算收益。",
            _ => "没有返回领取/合成数量时不推算收益；“已执行，未核验”需要核对游戏状态。"
        };
        ResultFields = CreateResultFields(result);
        InputFields = CreateInputFields(task);
        var evidence = result.Evidence.Select((item, index) => new PuloniaTaskHistoryField(
            $"证据 {index + 1} · {item.Kind} · {item.Source}",
            $"发生时间：{PuloniaTaskHistoryText.Time(item.OccurredAt)}\n{item.Data.ToString(Formatting.Indented)}")).ToList();
        // 确认事件可能早于执行器失败，必须单独展示，不能只依赖最终 Outcome.Evidence。
        evidence.AddRange(confirmedEffects.Where(item => item.TaskAddress == TaskAddress && item.Attempt == Attempt)
            .Select(item => CreateEffectField(item)));
        if (evidence.Count == 0)
            evidence.Add(new PuloniaTaskHistoryField("核验记录", "本次尝试没有结构化证据或已确认的 CD/额度事件。"));
        EvidenceFields = evidence;
    }

    /// <summary>
    /// 把已确认副作用展示为完整可复制内容，保留事件键以便核对幂等性。
    /// </summary>
    public static PuloniaTaskHistoryField CreateEffectField(PuloniaTaskConfirmedEffect effect)
    {
        var entry = effect.Entry;
        return new PuloniaTaskHistoryField($"已确认副作用 · {entry.EffectKey} · 尝试 {effect.Attempt}",
            $"节点：{effect.TaskAddress}\n事件：{entry.EventKey}\n作用域：{entry.ScopeKey}\n"
            + $"确认时间：{PuloniaTaskHistoryText.Time(entry.OccurredAt)}\n周期：{entry.WindowKey} · 消耗 {entry.Units} 单位\n"
            + $"下次可执行：{PuloniaTaskHistoryText.Time(entry.NextEligibleAt)}\n"
            + $"证据：{entry.Evidence.Kind} / {entry.Evidence.Source}\n{entry.Evidence.Data.ToString(Formatting.Indented)}");
    }

    /// <summary>
    /// 分类型选择关键结果；所有未识别字段仍可查看，不丢弃扩展执行器数据。
    /// </summary>
    private static IReadOnlyList<PuloniaTaskHistoryField> CreateResultFields(PuloniaTaskNodeResult result)
    {
        var fields = new List<PuloniaTaskHistoryField>();
        var handled = new HashSet<string>();
        // 1. 执行器给出的原因码放在最前，便于按原因筛查失败与跳过
        if (!string.IsNullOrWhiteSpace(result.ReasonCode))
            fields.Add(new PuloniaTaskHistoryField("原因码", result.ReasonCode));
        // 2. 奖励汇总（RewardSummary 的 items）逐行展示为“名称 ×数量”
        if (TryFormatRewards(result, out var rewards))
        {
            handled.Add(RewardItemsKey);
            fields.Add(new PuloniaTaskHistoryField("奖励", rewards));
        }
        // 3. 各类型的关键结果
        switch (result.TaskType)
        {
            case "shell":
                AddResultField(fields, handled, result.Data, "exit_code", "进程退出码");
                AddResultField(fields, handled, result.Data, "standard_output", "标准输出");
                AddResultField(fields, handled, result.Data, "standard_error", "标准错误");
                AddResultField(fields, handled, result.Data, "standard_output_truncated", "标准输出已截断");
                AddResultField(fields, handled, result.Data, "standard_error_truncated", "标准错误已截断");
                break;
            case "pathing":
                if (result.Data.ContainsKey("is_success"))
                {
                    AddResultField(fields, handled, result.Data, "is_success", "路线完整执行");
                    AddResultField(fields, handled, result.Data, "success_fight_count", "成功战斗次数");
                    handled.Add("error_message");
                    fields.Add(new PuloniaTaskHistoryField("失败原因", result.Data["error_message"] is { Type: not JTokenType.Null } error
                        ? FormatValue(error) : result.Data.Value<bool>("is_success") ? "无" : "未提供"));
                    handled.Add("revive_count");
                    handled.Add("collected_items");
                    fields.Add(new PuloniaTaskHistoryField("复活次数", result.Data["revive_count"] is { Type: not JTokenType.Null } revive ? FormatValue(revive) : "未统计"));
                    fields.Add(new PuloniaTaskHistoryField("拾取物品", result.Data["collected_items"] is JObject items
                        ? items.Count == 0 ? "已统计，未拾取物品" : string.Join("\n", items.Properties().Select(item => $"{item.Name} × {FormatValue(item.Value)}"))
                        : "未统计"));
                }
                else
                {
                    // 旧历史保留原始展示字段，不回填成功或采集事实。
                    AddResultField(fields, handled, result.Data, "success_end", "路线完整执行");
                    AddResultField(fields, handled, result.Data, "success_fight", "战斗成功标记");
                    if (result.Data.ContainsKey("fully_executed")) AddResultField(fields, handled, result.Data, "fully_executed", "文件完整执行成功");
                }
                foreach (var key in new[] { "collection_material", "collection_rule", "collection_rule_source", "collection_last_success_at", "collection_next_eligible_at", "collection_basis" })
                    if (result.Data.ContainsKey(key)) AddResultField(fields, handled, result.Data, key, ParameterLabel(key));
                break;
            case "csharp":
                if (result.Data.ContainsKey("sum"))
                {
                    AddResultField(fields, handled, result.Data, "sum", "计算合计");
                    AddResultField(fields, handled, result.Data, "value_count", "参与计算的数值数量");
                }
                break;
        }
        foreach (var property in result.Data.Properties().Where(item => !handled.Contains(item.Name)))
            fields.Add(new PuloniaTaskHistoryField(ParameterLabel(property.Name), FormatValue(property.Value)));
        if (fields.Count == 0)
            fields.Add(new PuloniaTaskHistoryField("执行结果", "执行器没有附带结构化返回值，请查看结果消息和证据。"));
        return fields;
    }

    /// <summary>
    /// 把结果数据中的奖励数组格式化为逐行的“名称 ×数量”。
    /// 独立任务的 items 一律按奖励解释；其他类型只在每一项都带 name 时才按奖励解释，避免误读扩展数据。
    /// </summary>
    /// <param name="result">节点结果。</param>
    /// <param name="text">格式化后的奖励文本；无法按奖励解释时为空字符串。</param>
    /// <returns>能够按奖励解释时返回 true。</returns>
    private static bool TryFormatRewards(PuloniaTaskNodeResult result, out string text)
    {
        text = string.Empty;
        if (result.Data[RewardItemsKey] is not JArray items)
            return false;
        var isSoloTask = PuloniaSoloTaskExecutor.IsSoloTaskType(result.TaskType);
        var isRewardShaped = items.All(item => item is JObject entry && entry["name"] is { Type: JTokenType.String });
        if (!isRewardShaped || (items.Count == 0 && !isSoloTask))
            return false;
        text = items.Count == 0
            ? "没有识别到奖励。"
            : string.Join("\n", items.OfType<JObject>().Select(item =>
                $"{item.Value<string>("name")} ×{(item["count"] is { Type: not JTokenType.Null } count ? count.ToString(Formatting.None) : "?")}"));
        return true;
    }

    /// <summary>
    /// 显示关键结果的缺省状态，不把缺失 bool 或退出码解释为 false 或 0。
    /// </summary>
    private static void AddResultField(List<PuloniaTaskHistoryField> fields, HashSet<string> handled,
        JObject data, string key, string label)
    {
        handled.Add(key);
        fields.Add(new PuloniaTaskHistoryField(label, data.TryGetValue(key, out var value)
            ? FormatValue(value) : "未返回（可能在结果产生前失败或取消）"));
    }

    /// <summary>
    /// 展示固定资源和有效参数，参数来源也来自当次快照。
    /// </summary>
    private static IReadOnlyList<PuloniaTaskHistoryField> CreateInputFields(PuloniaTaskPreparedTask? task)
    {
        if (task is null)
            return [new PuloniaTaskHistoryField("快照输入", "无法匹配原快照节点，仍可查看真实结果及原始记录。")];
        var fields = new List<PuloniaTaskHistoryField>();
        if (task.Path is not null)
            fields.Add(new PuloniaTaskHistoryField(task.TaskType switch
            {
                "pathing" => "路线文件", "javascript" => "JS 项目目录", "keymouse" => "录制文件", _ => "资源路径"
            }, task.Path));
        if (task.ResourceVersion is not null)
            fields.Add(new PuloniaTaskHistoryField("固定资源版本（SHA-256）", task.ResourceVersion));
        foreach (var parameter in task.Parameters.Properties())
        {
            var source = task.ParameterSources.TryGetValue(parameter.Name, out var origin) ? $" · 来源：{origin}" : "";
            fields.Add(new PuloniaTaskHistoryField(ParameterLabel(parameter.Name) + source, FormatValue(parameter.Value)));
        }
        fields.Add(new PuloniaTaskHistoryField("当次执行策略", PuloniaTaskJson.Write(task.Policy)));
        return fields;
    }

    /// <summary>
    /// 为已知类型的输入和结果提供中文标签，未知扩展键保持原文。
    /// </summary>
    private static string ParameterLabel(string key) => key switch
    {
        "file_name" => "可执行程序", "arguments" => "参数列表", "working_directory" => "工作目录",
        "operation" => "C# 操作", "values" => "计算输入", "settings" => "JS 脚本设置",
        "party_name" => "队伍名称", "skip_party_switch" => "跳过队伍切换", "auto_pick_enabled" => "自动拾取",
        "auto_fight_enabled" => "自动战斗", "auto_skip_enabled" => "自动跳过", "with_delay" => "按录制延时回放",
        "country" => "国家", "resource_version" => "执行资源版本", "run_id" => "运行 ID",
        "delay_milliseconds" => "延时（毫秒）", "units" => "额度单位", "event_key" => "事件键",
        "occurred_at" => "事件发生时间", "report_twice" => "重复报告事件（验收用）",
        "collection_cd" => "采集 CD 配置", "collection_material" => "采集物",
        "collection_rule" => "本次 CD 规则", "collection_rule_source" => "规则依据",
        "collection_last_success_at" => "最后成功时间（UTC）", "collection_next_eligible_at" => "下次可执行时间（UTC）",
        "collection_basis" => "采集完成依据",
        // 独立任务常用参数
        "weekday_overrides" => "每周安排", "combat_strategy_path" => "战斗策略", "strategy_name" => "战斗策略名称",
        "domain_name" => "秘境", "boss_name" => "首领", "team_name" => "队伍名称",
        "min_resin_to_keep" => "保留原粹树脂", "teleport_type" => "进壶方式", "secret_treasure_objects" => "洞天百宝购买",
        _ => key
    };

    /// <summary>
    /// 保留文本和 JSON 的完整内容，空字符串、null 和布尔值明确区分。
    /// </summary>
    private static string FormatValue(JToken value) => value.Type switch
    {
        JTokenType.Boolean => value.Value<bool>() ? "是" : "否",
        JTokenType.Null => "未设置（null）",
        JTokenType.String => string.IsNullOrEmpty(value.Value<string>()) ? "（空）" : value.Value<string>()!,
        _ => value.ToString(Formatting.Indented)
    };
}
