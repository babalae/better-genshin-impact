using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 高级设置窗口使用的数据查询与变更：原始参数、策略、来源 JSON 与共享预设。
/// 编辑器文本、选项列表和提示由窗口视图模型按需持有，文档只做校验和一次可撤销的修改。
/// </summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>
    /// 解析参数 JSON 并整体替换节点参数；分组节点替换类型公共参数覆盖。解析或校验失败时不修改模型。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="json">参数对象 JSON；分组为公共参数覆盖数组 JSON。</param>
    /// <exception cref="JsonException">JSON 格式错误。</exception>
    /// <exception cref="InvalidOperationException">节点已经不在当前计划中。</exception>
    internal void ApplyRawParameters(PuloniaTaskNodeViewModel node, string json)
    {
        EnsureEditableNode(node);
        if (node.TaskType == "group")
        {
            var overrides = ReadParameterOverrides(json);
            ApplyMutation(() => node.Model.ParameterOverrides = overrides, node);
            return;
        }
        var parameters = PuloniaTaskJson.Read<JObject>(json);
        ApplyMutation(() => node.Model.Parameters = parameters, node);
    }

    /// <summary>
    /// 解析并校验执行策略 JSON 后替换节点策略；失败时不修改原策略。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="json">执行策略 JSON。</param>
    /// <exception cref="JsonException">JSON 格式错误。</exception>
    /// <exception cref="PuloniaTaskValidationException">策略取值无效。</exception>
    internal void ApplyRawPolicy(PuloniaTaskNodeViewModel node, string json)
    {
        EnsureEditableNode(node);
        var policy = PuloniaTaskJson.Read<PuloniaTaskPolicy>(json);
        PuloniaTaskValidator.ValidatePolicy(policy, node.Id + "/policy");
        ApplyMutation(() => node.Model.Policy = policy, node);
    }

    /// <summary>
    /// 解析并替换计划或目录引用来源；空文本清除来源。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="json">来源 JSON；空白表示清除。</param>
    /// <exception cref="JsonException">JSON 格式错误。</exception>
    internal void ApplyRawSource(PuloniaTaskNodeViewModel node, string json)
    {
        EnsureEditableNode(node);
        var source = string.IsNullOrWhiteSpace(json)
            ? null
            : PuloniaTaskJson.Read<PuloniaTaskSource>(json);
        ApplyMutation(() =>
        {
            node.Model.Source = source;
            node.NotifySourceChanged();
        }, node);
    }

    /// <summary>
    /// 为叶子节点选择共享预设；为空表示不使用预设。选择未变化时不产生编辑。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="presetId">预设 ID。</param>
    internal void SelectPreset(PuloniaTaskNodeViewModel node, string? presetId)
    {
        if (!node.IsLeaf || node.Model.PresetId == presetId || IsDeleted || !EnumerateNodes().Contains(node))
            return;
        ApplyMutation(() => node.Model.PresetId = presetId, node);
    }

    /// <summary>
    /// 列出节点可选择的共享预设；节点引用缺失或不匹配的预设时追加警示项并给出提示。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="warning">预设缺失或不匹配时的提示；正常时为 null。</param>
    /// <returns>首项固定为“不使用共享预设”。</returns>
    internal IReadOnlyList<PuloniaTaskPresetOptionViewModel> GetPresetOptions(PuloniaTaskNodeViewModel node, out string? warning)
    {
        warning = null;
        var options = new List<PuloniaTaskPresetOptionViewModel> { new(null, "不使用共享预设") };
        // 与运行准备共用同一作用域规则（类型、资源与参数版本）
        var definition = GetDefinition(node.Model);
        options.AddRange(_presets
            .Where(preset => PuloniaParameterResolver.MatchesScope(preset.TaskType, preset.ResourceId,
                preset.SchemaVersion, node.Model, definition))
            .OrderBy(preset => preset.Name, StringComparer.CurrentCulture)
            .Select(preset => new PuloniaTaskPresetOptionViewModel(preset.Id, $"{preset.Name} · r{preset.Revision}")));
        if (node.Model.PresetId is { } selectedPresetId && options.All(option => option.Id != selectedPresetId))
        {
            var knownPreset = _presets.FirstOrDefault(preset => preset.Id == selectedPresetId);
            options.Add(new PuloniaTaskPresetOptionViewModel(selectedPresetId,
                knownPreset is null
                    ? $"⚠ 缺失预设：{selectedPresetId}"
                    : $"⚠ 预设与节点不匹配：{knownPreset.Name}"));
            warning = knownPreset is null
                ? "此任务选用的共享预设已不存在，请重新选择；不会自动清除这项选择。"
                : "此任务选用的共享预设与任务类型、资源或参数版本不匹配，请重新选择。";
        }
        return options;
    }

    /// <summary>
    /// 计算叶子节点的有效参数及每个字段的最后来源，按参数名排序。
    /// </summary>
    /// <param name="node">叶子节点。</param>
    internal IReadOnlyList<PuloniaTaskEffectiveParameterViewModel> GetEffectiveParameterSources(PuloniaTaskNodeViewModel node)
    {
        if (!node.IsLeaf)
            return [];
        var effective = ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out var sources);
        return effective.Properties().OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new PuloniaTaskEffectiveParameterViewModel(property.Name,
                property.Value.ToString(Formatting.None), sources[property.Name]))
            .ToArray();
    }
}
