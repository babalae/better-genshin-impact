using System;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>新增与编辑任务弹窗使用的参数预览和一次性提交入口。</summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>
    /// 预览尚未插入的新任务放到指定父节点下之后的有效参数，让新增弹窗展示分组覆盖等继承值。
    /// </summary>
    /// <param name="task">待插入的任务模型。</param>
    /// <param name="parent">插入位置的父节点。</param>
    /// <returns>有效参数副本。</returns>
    internal JObject PreviewEffectiveParameters(PuloniaTask task, PuloniaTaskNodeViewModel parent)
        => ResolveEffectiveParameters(task, GetAncestorModels(parent), out _);

    /// <summary>
    /// 把配置弹窗中的名称、启用、执行次数、资源路径和参数变化作为一次可撤销编辑提交。
    /// 参数校验失败时不修改任何字段。
    /// </summary>
    /// <param name="node">被编辑的节点。</param>
    /// <param name="name">新名称。</param>
    /// <param name="isEnabled">节点自身启用状态。</param>
    /// <param name="repeatCount">分组执行次数；非分组节点忽略。</param>
    /// <param name="path">资源路径；仅未绑定仓库资源的叶子节点生效。</param>
    /// <param name="parameterChanges">相对当前有效参数发生变化的字段。</param>
    /// <returns>存在实际修改并已提交时返回 true。</returns>
    /// <exception cref="InvalidOperationException">节点已经不在当前计划中。</exception>
    /// <exception cref="PuloniaTaskValidationException">合并后的参数不符合 Schema。</exception>
    internal bool ApplyNodeSettings(PuloniaTaskNodeViewModel node, string name, bool isEnabled, int repeatCount,
        string? path, JObject parameterChanges)
    {
        EnsureEditableNode(node);
        var model = node.Model;

        // 1. 归一化：执行次数只对分组生效且 1 次不持久化；路径只对未绑定仓库资源的叶子生效
        var clampedRepeat = Math.Clamp(repeatCount, 1, PuloniaTaskValidator.MaxTreeNodes);
        int? storedRepeat = node.CanConfigureRepeat ? (clampedRepeat == 1 ? null : clampedRepeat) : model.RepeatCount;
        var storedPath = node.IsLeaf && model.Resource is null && path != model.Path
            ? (string.IsNullOrWhiteSpace(path) ? null : path.Trim())
            : model.Path;
        var pathChanged = storedPath != model.Path;
        if (model.Name == name && model.IsEnabled == isEnabled && model.RepeatCount == storedRepeat
            && !pathChanged && parameterChanges.Count == 0)
            return false;

        // 2. 先完整校验参数，避免名称等字段写入后参数才失败
        if (parameterChanges.Count > 0)
            ValidateParameterChanges(node, parameterChanges);

        // 3. 路径变化后旧路径的资源检查结果失效，需在内容变更通知前清除
        if (pathChanged)
            node.ResetResourceCheck();

        // 4. 全部字段在同一次变更中写入，撤销时整体恢复
        ApplyMutation(() =>
        {
            model.Name = name;
            model.IsEnabled = isEnabled;
            model.RepeatCount = storedRepeat;
            model.Path = storedPath;
            foreach (var field in parameterChanges.Properties())
                model.Parameters[field.Name] = field.Value.DeepClone();
        }, node);
        node.NotifySettingsApplied();
        return true;
    }

    /// <summary>确认节点仍属于当前未删除的计划，弹窗打开期间节点可能被撤销或删除。</summary>
    /// <param name="node">待编辑节点。</param>
    /// <exception cref="InvalidOperationException">节点已经移除。</exception>
    private void EnsureEditableNode(PuloniaTaskNodeViewModel node)
    {
        if (IsDeleted || !EnumerateNodes().Contains(node))
            throw new InvalidOperationException("当前任务已移除，请重新选择。");
    }

    /// <summary>把变化字段合并到当前有效参数后按能力 Schema 校验。</summary>
    /// <param name="node">目标节点。</param>
    /// <param name="changes">变化字段。</param>
    /// <exception cref="PuloniaTaskValidationException">合并后的参数不符合 Schema。</exception>
    private void ValidateParameterChanges(PuloniaTaskNodeViewModel node, JObject changes)
    {
        var effective = GetOneDragonParameters(node);
        foreach (var field in changes.Properties())
            effective[field.Name] = field.Value.DeepClone();
        if (GetDefinition(node.Model) is { } definition)
            PuloniaTaskValidator.ValidateParameters(effective, definition.ParameterSchema, node.Name);
    }
}
