using System;
using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 保存当前进程内复制的 Pulonia 子树和复制时的有效参数，用于跨计划粘贴。
/// </summary>
public sealed class PuloniaTaskClipboardService
{
    /// <summary>
    /// 保留原节点 ID 的剪贴板副本，仅用于关联复制时的有效参数。
    /// </summary>
    private PuloniaTask? _sourceSubtree;

    /// <summary>
    /// 以原节点 ID 为键的叶子有效参数副本。
    /// </summary>
    private IReadOnlyDictionary<string, JObject> _effectiveParametersByTaskId =
        new Dictionary<string, JObject>(StringComparer.Ordinal);

    /// <summary>
    /// 复制来源计划的用户可见名称。
    /// </summary>
    public string? SourcePlanName { get; private set; }

    /// <summary>
    /// 当前是否存在可粘贴的子树。
    /// </summary>
    public bool HasContent => _sourceSubtree is not null;

    /// <summary>
    /// 用独立副本替换剪贴板内容，避免后续编辑污染已复制内容。
    /// </summary>
    public void Set(string sourcePlanName, PuloniaTask subtree,
        IReadOnlyDictionary<string, JObject> effectiveParametersByTaskId)
    {
        ArgumentNullException.ThrowIfNull(subtree);
        ArgumentNullException.ThrowIfNull(effectiveParametersByTaskId);

        _sourceSubtree = ClonePreservingIds(subtree);
        var parameterCopies = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var (taskId, parameters) in effectiveParametersByTaskId)
            parameterCopies[taskId] = (JObject)parameters.DeepClone();
        _effectiveParametersByTaskId = parameterCopies;
        SourcePlanName = sourcePlanName;
    }

    /// <summary>
    /// 建立一次粘贴所需的原 ID 副本、新 ID 副本和有效参数副本。
    /// </summary>
    public bool TryCreatePaste(out PuloniaTask? sourceSubtree, out PuloniaTask? pastedSubtree,
        out IReadOnlyDictionary<string, JObject> effectiveParametersByTaskId)
    {
        if (_sourceSubtree is null)
        {
            sourceSubtree = null;
            pastedSubtree = null;
            effectiveParametersByTaskId = new Dictionary<string, JObject>(StringComparer.Ordinal);
            return false;
        }

        sourceSubtree = ClonePreservingIds(_sourceSubtree);
        pastedSubtree = PuloniaTaskJson.CopySubtree(_sourceSubtree);
        var parameterCopies = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var (taskId, parameters) in _effectiveParametersByTaskId)
            parameterCopies[taskId] = (JObject)parameters.DeepClone();
        effectiveParametersByTaskId = parameterCopies;
        return true;
    }

    /// <summary>
    /// 通过临时计划复用正式 JSON 合同，深复制子树但保留稳定 ID。
    /// </summary>
    private static PuloniaTask ClonePreservingIds(PuloniaTask subtree)
    {
        var wrapper = new PuloniaTaskPlan();
        wrapper.RootTask.Children.Add(subtree);
        return PuloniaTaskJson.ClonePlan(wrapper).RootTask.Children[0];
    }
}
