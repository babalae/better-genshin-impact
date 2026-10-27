using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 高级设置窗口使用的数据查询与变更：原始任务参数、共享设置、策略和来源 JSON。
/// 编辑器文本和提示由窗口视图模型按需持有，文档只做校验和一次可撤销的修改。
/// </summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>
    /// 解析参数 JSON 并整体替换叶子的任务参数；分组没有任务参数。解析失败时不修改模型。
    /// </summary>
    /// <param name="node">目标叶子节点。</param>
    /// <param name="json">参数对象 JSON。</param>
    /// <exception cref="JsonException">JSON 格式错误。</exception>
    /// <exception cref="InvalidOperationException">节点已经不在当前计划中，或是分组。</exception>
    internal void ApplyRawParameters(PuloniaTaskNodeViewModel node, string json)
    {
        EnsureEditableNode(node);
        if (!node.IsLeaf)
            throw new InvalidOperationException("分组没有任务参数，请使用共享设置。");
        var parameters = PuloniaTaskJson.Read<JObject>(json);
        ApplyMutation(() => node.Model.Parameters = parameters, node);
    }

    /// <summary>
    /// 解析共享设置 JSON 并整体替换节点自身的共享设置；键和值按设置目录校验，失败时不修改模型。
    /// </summary>
    /// <param name="node">目标节点。</param>
    /// <param name="json">设置项 ID 到值的对象 JSON。</param>
    /// <exception cref="JsonException">JSON 格式错误。</exception>
    /// <exception cref="PuloniaTaskValidationException">设置项不存在或值无效。</exception>
    internal void ApplyRawSettings(PuloniaTaskNodeViewModel node, string json)
    {
        EnsureEditableNode(node);
        var settings = PuloniaTaskJson.Read<JObject>(json);
        PuloniaTaskValidator.ValidateSettings(settings, node.Name + "/settings");
        ApplyMutation(() =>
        {
            node.Model.Settings = settings;
            node.NotifySharedSettingsChanged();
        }, node);
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
