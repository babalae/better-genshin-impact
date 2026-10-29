using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 节点尝试的类型专属详情，只使用运行快照与真实结果，不读取已经编辑过的计划或资源。
/// 结果键与核验边界由 <see cref="PuloniaTaskResultInterpreter"/> 解释，本类只负责格式化展示。
/// 展示分三层：结果（默认可见）、当次使用的设置与技术详情（默认收起，排查问题时查看）。
/// </summary>
public sealed class PuloniaTaskNodeResultViewModel : ObservableObject
{
    /// <summary>
    /// 合并后的展示状态，状态文字、颜色与图标都由它派生。
    /// </summary>
    private readonly (string Text, string Bucket, PuloniaTaskRunStatusCategory Category) _status;

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
    /// 合并调度状态与业务完成程度后的单一状态，例如“成功”“已执行，待核对”“路线 CD 中”。
    /// </summary>
    public string StatusText => _status.Text;

    /// <summary>
    /// 运行摘要中归入的类别。
    /// </summary>
    internal string StatusBucket => _status.Bucket;

    /// <summary>
    /// 着色分类，供默认选中失败或待核对的节点。
    /// </summary>
    internal PuloniaTaskRunStatusCategory StatusCategory => _status.Category;

    /// <summary>
    /// 视图触发器使用的着色键，与运行状态共用同一套样式。
    /// </summary>
    public string StatusKind => PuloniaTaskHistoryText.CategoryKey(_status.Category);

    /// <summary>
    /// 状态图标；中性状态（跳过、停止）使用减号圆圈。
    /// </summary>
    public SymbolRegular StatusIcon => PuloniaTaskHistoryText.CategoryIcon(_status.Category, SymbolRegular.SubtractCircle24);

    /// <summary>
    /// 是否实际调用过执行器；未启用、续跑沿用和路线 CD 跳过的节点没有执行时间。
    /// </summary>
    public bool WasExecuted => Attempt > 0;

    /// <summary>
    /// 节点行右侧的耗时；未执行时为空。
    /// </summary>
    public string? DurationText => WasExecuted ? PuloniaTaskHistoryText.Duration(Result.FinishedAt - Result.StartedAt) : null;

    /// <summary>
    /// 详情中的时间摘要：开始、结束与耗时；未执行时说明原因。
    /// </summary>
    public string TimingText => WasExecuted
        ? $"{PuloniaTaskHistoryText.Time(Result.StartedAt)} → {Result.FinishedAt.ToLocalTime():HH:mm:ss} · 用时 {DurationText}"
        : "未执行";

    /// <summary>
    /// 重试时的轮次标记，例如“第 2 次尝试”；首次尝试为空。
    /// </summary>
    public string? AttemptText => Attempt > 1 ? $"第 {Attempt} 次尝试" : null;

    /// <summary>
    /// 执行器原始消息或异常原因。
    /// </summary>
    public string Message => Result.Message;

    /// <summary>
    /// 节点行下方的一行说明：只在非成功时显示原因，成功的行保持简洁。
    /// </summary>
    public string? BriefText => _status.Category == PuloniaTaskRunStatusCategory.Success || string.IsNullOrWhiteSpace(Message)
        ? null
        : Message;

    /// <summary>
    /// 结果需要人工核对时的核验边界说明，避免把脚本返回或路线结束解释为已领取/采集成功；其余情况为空。
    /// </summary>
    public string? VerificationNote { get; }

    /// <summary>
    /// 类型专属的结果字段，保留未知扩展数据；执行器没有返回数据时为空。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> ResultFields { get; }

    /// <summary>
    /// 当次快照中的资源、有效参数（含来源）与执行策略。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> SettingsFields { get; }

    /// <summary>
    /// 排查问题用的内部信息：节点地址、原因码、资源版本、结构化证据与路线 CD 记录。
    /// </summary>
    public IReadOnlyList<PuloniaTaskHistoryField> TechnicalFields { get; }

    /// <summary>
    /// 未格式化删减的节点原始 JSON，用于排查扩展执行器结果。
    /// </summary>
    public string RawJson => PuloniaTaskJson.Write(Result);

    /// <summary>
    /// 从固定快照建立类型专属展示，旧历史缺少输入时明确说明。
    /// </summary>
    public PuloniaTaskNodeResultViewModel(PuloniaTaskNodeResult result, PuloniaTaskPreparedTask? task,
        string typeText, IEnumerable<PuloniaTaskConfirmedEffect> confirmedEffects)
    {
        Result = result;
        TypeText = typeText;
        _status = PuloniaTaskHistoryText.AttemptStatus(result);
        // 各类型的核验边界由 Pulonia 结果解释统一描述，只在结果需要核对时提示。
        VerificationNote = _status.Category == PuloniaTaskRunStatusCategory.Attention
            ? PuloniaTaskResultInterpreter.GetVerificationNote(result.TaskType)
            : null;
        ResultFields = PuloniaTaskResultInterpreter.ReadEntries(result)
            .Select(entry => new PuloniaTaskHistoryField(entry.Label, FormatEntry(entry)))
            .ToArray();
        SettingsFields = CreateSettingsFields(task);
        TechnicalFields = CreateTechnicalFields(result, task,
            confirmedEffects.Where(item => item.TaskAddress == TaskAddress && item.Attempt == Attempt));
    }

    /// <summary>
    /// 把一条路线 CD 记录展示为完整可复制内容，保留事件键以便核对幂等性。
    /// </summary>
    public static PuloniaTaskHistoryField CreateEffectField(PuloniaTaskConfirmedEffect effect)
    {
        var entry = effect.Entry;
        return new PuloniaTaskHistoryField($"路线 CD 记录 · 第 {effect.Attempt} 次尝试",
            $"资源：{entry.EffectKey}\n节点：{effect.TaskAddress}\n事件：{entry.EventKey}\n作用域：{entry.ScopeKey}\n"
            + $"记录时间：{PuloniaTaskHistoryText.Time(entry.OccurredAt)}\n"
            + $"记录时规则：{entry.Evidence.Data.Value<string>("collection_rule") ?? "未记录"}\n"
            + $"下次可执行：{PuloniaTaskHistoryText.Time(entry.NextEligibleAt)}");
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
    /// 展示当次快照的资源、有效参数和执行策略；参数来源转换为中文，不展示内部地址。
    /// </summary>
    private static IReadOnlyList<PuloniaTaskHistoryField> CreateSettingsFields(PuloniaTaskPreparedTask? task)
    {
        if (task is null)
            return [new PuloniaTaskHistoryField("当次设置", "这条记录的快照中找不到对应任务，无法显示当时的设置。")];
        var fields = new List<PuloniaTaskHistoryField>();
        if (task.Path is not null)
            fields.Add(new PuloniaTaskHistoryField(PuloniaTaskResultInterpreter.GetResourcePathLabel(task.TaskType), task.Path));
        foreach (var parameter in task.Parameters.Properties())
        {
            var source = task.ParameterSources.TryGetValue(parameter.Name, out var origin) ? $"（{DescribeParameterSource(origin)}）" : "";
            // 参数中文名与编辑器共用同一份映射，未知扩展键保持原文。
            fields.Add(new PuloniaTaskHistoryField(PuloniaTaskFieldNames.GetDisplayName(parameter.Name) + source,
                FormatValue(parameter.Value)));
        }
        fields.Add(new PuloniaTaskHistoryField("执行策略", DescribePolicy(task.Policy)));
        return fields;
    }

    /// <summary>
    /// 把快照中记录的参数来源（default/类型、settings、节点地址、call/地址）转换为用户能理解的说明。
    /// </summary>
    private static string DescribeParameterSource(string source)
    {
        if (source.StartsWith("default", StringComparison.Ordinal))
            return "默认值";
        if (source == "settings")
            return "共享设置";
        return source.StartsWith("call/", StringComparison.Ordinal) ? "本次运行指定" : "任务自身设置";
    }

    /// <summary>
    /// 把执行策略写成一句话：失败后行为、重试与时限。
    /// </summary>
    private static string DescribePolicy(PuloniaTaskPolicy policy)
    {
        var retries = policy.MaxRetries ?? 0;
        var delay = policy.RetryDelaySeconds ?? 0;
        var retry = retries <= 0
            ? "不重试"
            : delay <= 0
                ? $"失败后立即重试，最多 {retries} 次"
                : $"失败后等待 {PuloniaTaskHistoryText.Duration(TimeSpan.FromSeconds(delay))} 重试，最多 {retries} 次";
        var timeout = policy.TimeoutSeconds is { } seconds
            ? "时限 " + PuloniaTaskHistoryText.Duration(TimeSpan.FromSeconds(seconds))
            : "不限时";
        return $"失败时{PuloniaTaskHistoryText.FailureBehavior(policy.FailureBehavior)}；{retry}；{timeout}";
    }

    /// <summary>
    /// 收集排查问题用的内部信息；结构化证据和路线 CD 记录只在存在时列出。
    /// </summary>
    private static IReadOnlyList<PuloniaTaskHistoryField> CreateTechnicalFields(PuloniaTaskNodeResult result,
        PuloniaTaskPreparedTask? task, IEnumerable<PuloniaTaskConfirmedEffect> effects)
    {
        var fields = new List<PuloniaTaskHistoryField>
        {
            new("任务地址", result.TaskAddress),
            new("任务类型", result.TaskType),
            new("尝试次数", result.Attempt == 0 ? "未执行" : result.Attempt.ToString())
        };
        if (!string.IsNullOrWhiteSpace(result.ReasonCode))
            fields.Add(new PuloniaTaskHistoryField("原因码", result.ReasonCode));
        if (task?.ResourceVersion is { } version)
            fields.Add(new PuloniaTaskHistoryField("固定资源版本（SHA-256）", version));
        // 确认事件与节点结果在同一次写盘中提交，这里保留完整记录，便于核对事件键与作用域。
        fields.AddRange(effects.Select(CreateEffectField));
        fields.AddRange(result.Evidence.Select((item, index) => new PuloniaTaskHistoryField(
            $"证据 {index + 1} · {item.Kind} · {item.Source}",
            $"发生时间：{PuloniaTaskHistoryText.Time(item.OccurredAt)}\n{item.Data.ToString(Formatting.Indented)}")));
        return fields;
    }

    /// <summary>
    /// 保留文本和 JSON 的完整内容，空字符串、null 和布尔值明确区分。
    /// </summary>
    private static string FormatValue(JToken value) => value.Type switch
    {
        JTokenType.Boolean => value.Value<bool>() ? "是" : "否",
        JTokenType.Null => "未设置",
        JTokenType.String => string.IsNullOrEmpty(value.Value<string>()) ? "（空）" : value.Value<string>()!,
        _ => value.ToString(Formatting.Indented)
    };
}
