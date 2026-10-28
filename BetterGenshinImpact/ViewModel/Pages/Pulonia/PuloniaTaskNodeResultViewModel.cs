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
/// 结果键与核验边界由 <see cref="PuloniaTaskResultInterpreter"/> 解释，本类只负责格式化展示。
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
        // 各类型的核验边界由 Pulonia 结果解释统一描述。
        VerificationNote = PuloniaTaskResultInterpreter.GetVerificationNote(result.TaskType);
        ResultFields = CreateResultFields(result);
        InputFields = CreateInputFields(task);
        var evidence = result.Evidence.Select((item, index) => new PuloniaTaskHistoryField(
            $"证据 {index + 1} · {item.Kind} · {item.Source}",
            $"发生时间：{PuloniaTaskHistoryText.Time(item.OccurredAt)}\n{item.Data.ToString(Formatting.Indented)}")).ToList();
        // 确认事件可能早于执行器失败，必须单独展示，不能只依赖最终 Outcome.Evidence。
        evidence.AddRange(confirmedEffects.Where(item => item.TaskAddress == TaskAddress && item.Attempt == Attempt)
            .Select(item => CreateEffectField(item)));
        if (evidence.Count == 0)
            evidence.Add(new PuloniaTaskHistoryField("核验记录", "本次尝试没有结构化证据或已确认的路线 CD 记录。"));
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
            + $"确认时间：{PuloniaTaskHistoryText.Time(entry.OccurredAt)}\n"
            + $"记录时规则：{entry.Evidence.Data.Value<string>("collection_rule") ?? "未记录"}\n"
            + $"下次可执行：{PuloniaTaskHistoryText.Time(entry.NextEligibleAt)}\n"
            + $"证据：{entry.Evidence.Kind} / {entry.Evidence.Source}\n{entry.Evidence.Data.ToString(Formatting.Indented)}");
    }

    /// <summary>
    /// 按结果解释的顺序展示关键结果；所有未识别字段仍可查看，不丢弃扩展执行器数据。
    /// </summary>
    private static IReadOnlyList<PuloniaTaskHistoryField> CreateResultFields(PuloniaTaskNodeResult result)
    {
        var fields = PuloniaTaskResultInterpreter.ReadEntries(result)
            .Select(entry => new PuloniaTaskHistoryField(entry.Label, FormatEntry(entry)))
            .ToList();
        if (fields.Count == 0)
            fields.Add(new PuloniaTaskHistoryField("执行结果", "执行器没有附带结构化返回值，请查看结果消息和证据。"));
        return fields;
    }

    /// <summary>
    /// 把一项结果解释转换为展示文本；缺失的关键结果明确说明，不把缺失 bool 或退出码显示为 false 或 0。
    /// </summary>
    private static string FormatEntry(PuloniaTaskResultEntry entry) => entry.Kind switch
    {
        PuloniaTaskResultEntryKind.NotReturned => "未返回（可能在结果产生前失败或取消）",
        PuloniaTaskResultEntryKind.NotCounted => "未统计",
        PuloniaTaskResultEntryKind.NoFailure => "无",
        PuloniaTaskResultEntryKind.FailureNotProvided => "未提供",
        PuloniaTaskResultEntryKind.Rewards => FormatRewards((JArray)entry.Value!),
        PuloniaTaskResultEntryKind.CollectedItems => FormatCollectedItems((JObject)entry.Value!),
        _ => FormatValue(entry.Value ?? JValue.CreateNull())
    };

    /// <summary>
    /// 把奖励数组格式化为逐行的“名称 ×数量”；数量缺失时显示问号。
    /// </summary>
    private static string FormatRewards(JArray items) => items.Count == 0
        ? "没有识别到奖励。"
        : string.Join("\n", items.OfType<JObject>().Select(item =>
            $"{item.Value<string>("name")} ×{(item["count"] is { Type: not JTokenType.Null } count ? count.ToString(Formatting.None) : "?")}"));

    /// <summary>
    /// 把路线拾取统计格式化为逐行的“名称 × 数量”。
    /// </summary>
    private static string FormatCollectedItems(JObject items) => items.Count == 0
        ? "已统计，未拾取物品"
        : string.Join("\n", items.Properties().Select(item => $"{item.Name} × {FormatValue(item.Value)}"));

    /// <summary>
    /// 展示固定资源和有效参数，参数来源也来自当次快照。
    /// </summary>
    private static IReadOnlyList<PuloniaTaskHistoryField> CreateInputFields(PuloniaTaskPreparedTask? task)
    {
        if (task is null)
            return [new PuloniaTaskHistoryField("快照输入", "无法匹配原快照节点，仍可查看真实结果及原始记录。")];
        var fields = new List<PuloniaTaskHistoryField>();
        if (task.Path is not null)
            fields.Add(new PuloniaTaskHistoryField(PuloniaTaskResultInterpreter.GetResourcePathLabel(task.TaskType), task.Path));
        if (task.ResourceVersion is not null)
            fields.Add(new PuloniaTaskHistoryField("固定资源版本（SHA-256）", task.ResourceVersion));
        foreach (var parameter in task.Parameters.Properties())
        {
            var source = task.ParameterSources.TryGetValue(parameter.Name, out var origin) ? $" · 来源：{origin}" : "";
            // 参数中文名与编辑器共用同一份映射，未知扩展键保持原文。
            fields.Add(new PuloniaTaskHistoryField(PuloniaTaskFieldNames.GetDisplayName(parameter.Name) + source,
                FormatValue(parameter.Value)));
        }
        fields.Add(new PuloniaTaskHistoryField("当次执行策略", PuloniaTaskJson.Write(task.Policy)));
        return fields;
    }

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
