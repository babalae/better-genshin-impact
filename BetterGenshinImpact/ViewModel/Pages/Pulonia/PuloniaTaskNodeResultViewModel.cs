using System.Collections.Generic;
using System.Linq;
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
            "pathing" => "路线结束和战斗标记来自路线执行器，不代表逐点采集数量或实际收益。",
            "javascript" => "旧 JS 脚本正常返回不等于游戏副作用已核验；仅展示执行器实际返回的数据与证据。",
            "keymouse" => "宏回放结束不等于游戏目标完成；录制文件本身没有结构化收益证据。",
            "shell" => "退出码表示进程结果；标准输出/错误每路最多保留 65536 字符，截断会明确标记。",
            "csharp" => "返回值与确认事件来自注册的 C# 能力；局部确认不等于整个节点成功。",
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
                AddResultField(fields, handled, result.Data, "success_end", "路线完整执行");
                AddResultField(fields, handled, result.Data, "success_fight", "战斗成功标记");
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
        "occurred_at" => "事件发生时间", "report_twice" => "重复报告事件（验收用）", _ => key
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
