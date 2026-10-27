using System;
using System.Collections.Generic;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 保存当前进程内复制的 Pulonia 子树、复制时的有效参数和子树外层的有效共享设置，用于跨计划粘贴。
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
    /// 复制时子树外层的有效共享设置：设置项 ID 到值。
    /// </summary>
    private JObject _outerSettings = new();

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
    /// <param name="sourcePlanName">来源计划名称。</param>
    /// <param name="subtree">复制的子树。</param>
    /// <param name="effectiveParametersByTaskId">复制时每个叶子的有效参数。</param>
    /// <param name="outerSettings">复制时子树外层的有效共享设置。</param>
    public void Set(string sourcePlanName, PuloniaTask subtree,
        IReadOnlyDictionary<string, JObject> effectiveParametersByTaskId, JObject outerSettings)
    {
        ArgumentNullException.ThrowIfNull(subtree);
        ArgumentNullException.ThrowIfNull(effectiveParametersByTaskId);
        ArgumentNullException.ThrowIfNull(outerSettings);

        _sourceSubtree = ClonePreservingIds(subtree);
        var parameterCopies = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var (taskId, parameters) in effectiveParametersByTaskId)
            parameterCopies[taskId] = (JObject)parameters.DeepClone();
        _effectiveParametersByTaskId = parameterCopies;
        _outerSettings = (JObject)outerSettings.DeepClone();
        SourcePlanName = sourcePlanName;
    }

    /// <summary>
    /// 建立一次粘贴所需的原 ID 副本、新 ID 副本、有效参数副本和外层共享设置副本。
    /// </summary>
    public bool TryCreatePaste(out PuloniaTask? sourceSubtree, out PuloniaTask? pastedSubtree,
        out IReadOnlyDictionary<string, JObject> effectiveParametersByTaskId, out JObject outerSettings)
    {
        if (_sourceSubtree is null)
        {
            sourceSubtree = null;
            pastedSubtree = null;
            effectiveParametersByTaskId = new Dictionary<string, JObject>(StringComparer.Ordinal);
            outerSettings = new JObject();
            return false;
        }

        sourceSubtree = ClonePreservingIds(_sourceSubtree);
        pastedSubtree = PuloniaTaskJson.CopySubtree(_sourceSubtree);
        var parameterCopies = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var (taskId, parameters) in _effectiveParametersByTaskId)
            parameterCopies[taskId] = (JObject)parameters.DeepClone();
        effectiveParametersByTaskId = parameterCopies;
        outerSettings = (JObject)_outerSettings.DeepClone();
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
