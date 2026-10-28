using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 一个节点的设置页需要的使用情况：哪些设置项会影响子树中的任务，以及各类别影响的任务数。
/// </summary>
/// <param name="Keys">子树中任务会用到的设置项；为 null 表示无法确定（含计划引用），按全部设置项处理。</param>
/// <param name="CategoryCounts">类别 ID → 任务类型名称 → 受影响的任务数；计划引用和目录引用各算一项。</param>
internal sealed record PuloniaSharedSettingUsage(IReadOnlySet<string>? Keys,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CategoryCounts);

/// <summary>
/// 设置项“值的来历”中的一层。
/// </summary>
/// <param name="Location">层级描述，例如“计划”“分组「蒙德」”“方案「深渊队」（分组「蒙德」）”。</param>
/// <param name="Value">该层提供的值。</param>
/// <param name="IsEffective">是否为最终生效的一层。</param>
internal sealed record PuloniaSharedSettingHistoryEntry(string Location, JToken Value, bool IsEffective);

/// <summary>
/// 一次跨父节点移动造成的共享设置变化，供“保留原值”把移动前的外层设置写回子树根。
/// </summary>
/// <param name="Node">被移动的子树根。</param>
/// <param name="OuterSettingsBefore">移动前子树外层的有效共享设置。</param>
/// <param name="ChangedKeys">至少一个任务的有效值发生变化的设置项，按目录顺序排列。</param>
/// <param name="ChangedTaskCount">有效设置发生变化的任务数；计划引用和目录引用各算一项。</param>
/// <param name="Details">变化明细，每行为“任务「名称」：设置项 旧值 → 新值”。</param>
/// <param name="UndoMergeKey">本次移动使用的撤销合并标识；保留原值沿用它，与移动合为一步撤销。</param>
internal sealed record PuloniaMovedSettingsChange(PuloniaTaskNodeViewModel Node, JObject OuterSettingsBefore,
    IReadOnlyList<string> ChangedKeys, int ChangedTaskCount, IReadOnlyList<string> Details, string UndoMergeKey);

/// <summary>
/// 共享设置的读取与修改：节点设置链解析、单项覆盖与恢复继承、方案选择。全部修改走同一撤销与自动保存入口。
/// </summary>
public partial class PuloniaTaskPlanDocumentViewModel
{
    /// <summary>
    /// 解析节点处的有效共享设置（含节点自身方案与自身设置）。
    /// </summary>
    internal PuloniaSettingsResolution ResolveNodeSettings(PuloniaTaskNodeViewModel node)
        => PuloniaParameterResolver.ResolveSettings(CreateNodeChain(node, includeOwnSettings: true));

    /// <summary>
    /// 解析恢复继承后的有效共享设置：保留节点所挂方案，去掉节点自身的设置覆盖。
    /// </summary>
    internal PuloniaSettingsResolution ResolveInheritedSettings(PuloniaTaskNodeViewModel node)
        => PuloniaParameterResolver.ResolveSettings(CreateNodeChain(node, includeOwnSettings: false));

    /// <summary>
    /// 计算节点设置页的使用情况：叶子为其能力绑定；分组为子树中全部任务的并集。
    /// </summary>
    internal PuloniaSharedSettingUsage GetSharedSettingUsage(PuloniaTaskNodeViewModel node)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var unknown = false;
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        Visit(node.Model);
        return new PuloniaSharedSettingUsage(unknown ? null : keys,
            counts.ToDictionary(item => item.Key, item => (IReadOnlyDictionary<string, int>)item.Value, StringComparer.Ordinal));

        void Visit(PuloniaTask task)
        {
            if (task.Source is { Kind: "plan" })
            {
                // 被引用计划的内容不在本文档中，按全部设置项计入
                unknown = true;
                foreach (var category in PuloniaSettingCatalog.Categories)
                    Count(category.Id, "计划引用");
                return;
            }
            if (task.Source is { Kind: "directory", TaskType: { } directoryType })
            {
                Add(PuloniaParameterResolver.FindDefinition(_definitions, new PuloniaTask { TaskType = directoryType }), "目录引用");
                return;
            }
            if (task.TaskType == "group")
            {
                foreach (var child in task.Children)
                    Visit(child);
                return;
            }
            var definition = GetDefinition(task);
            Add(definition, definition?.DisplayName ?? task.TaskType);
        }

        void Add(PuloniaTaskDefinition? definition, string typeName)
        {
            if (definition is null || definition.SettingBindings.Count == 0)
                return;
            keys.UnionWith(definition.SettingBindings.Keys);
            foreach (var category in definition.SettingBindings.Keys
                         .Select(key => PuloniaSettingCatalog.Get(key).Category).Distinct(StringComparer.Ordinal))
                Count(category, typeName);
        }

        void Count(string category, string typeName)
        {
            if (!counts.TryGetValue(category, out var byType))
                counts[category] = byType = new Dictionary<string, int>(StringComparer.Ordinal);
            byType[typeName] = byType.GetValueOrDefault(typeName) + 1;
        }
    }

    /// <summary>
    /// 在节点上覆盖一个设置项；同一节点同一项的连续输入合并为一次撤销，不改变当前选择。
    /// </summary>
    /// <exception cref="PuloniaTaskValidationException">值不符合设置项的值类型。</exception>
    internal void SetSetting(PuloniaTaskNodeViewModel node, string key, JToken value)
    {
        EnsureEditableNode(node);
        var candidate = new JObject { [key] = value.DeepClone() };
        PuloniaTaskValidator.ValidateSettings(candidate, node.Name + "/settings");
        if (node.Model.Settings.TryGetValue(key, out var current) && JToken.DeepEquals(current, value))
            return;
        ApplyMutation(() =>
        {
            node.Model.Settings[key] = value.DeepClone();
            node.NotifySharedSettingsChanged();
        }, null, "shared-setting|" + node.Id + "|" + key);
    }

    /// <summary>移除节点上的一个设置项覆盖，恢复继承。</summary>
    internal void ResetSetting(PuloniaTaskNodeViewModel node, string key)
    {
        EnsureEditableNode(node);
        if (!node.Model.Settings.ContainsKey(key))
            return;
        ApplyMutation(() =>
        {
            node.Model.Settings.Remove(key);
            node.NotifySharedSettingsChanged();
        }, null);
    }

    /// <summary>移除节点上的全部设置覆盖；所挂方案保留。</summary>
    internal void ResetAllSettings(PuloniaTaskNodeViewModel node)
    {
        EnsureEditableNode(node);
        if (node.Model.Settings.Count == 0)
            return;
        ApplyMutation(() =>
        {
            node.Model.Settings = new JObject();
            node.NotifySharedSettingsChanged();
        }, null);
    }

    /// <summary>移除与继承值相同的覆盖，作为一次可撤销编辑；返回移除的项数。</summary>
    internal int RemoveRedundantSettings(PuloniaTaskNodeViewModel node)
    {
        EnsureEditableNode(node);
        var inherited = ResolveInheritedSettings(node).Values;
        var redundant = node.Model.Settings.Properties()
            .Where(property => inherited.TryGetValue(property.Name, out var value) && JToken.DeepEquals(value, property.Value))
            .Select(property => property.Name)
            .ToArray();
        if (redundant.Length == 0)
            return 0;
        ApplyMutation(() =>
        {
            foreach (var key in redundant)
                node.Model.Settings.Remove(key);
            node.NotifySharedSettingsChanged();
        }, null);
        return redundant.Length;
    }

    /// <summary>为节点选择设置方案；为空表示不使用。选择未变化时不产生编辑。</summary>
    internal void SelectProfile(PuloniaTaskNodeViewModel node, string? profileId)
    {
        EnsureEditableNode(node);
        if (node.Model.ProfileId == profileId)
            return;
        ApplyMutation(() =>
        {
            node.Model.ProfileId = profileId;
            node.NotifySharedSettingsChanged();
        }, null);
    }

    /// <summary>
    /// 让节点改用新方案并移除自身覆盖，作为一次可撤销编辑；方案内容已包含原方案与自身覆盖，有效值不变。
    /// </summary>
    internal void UseProfileInsteadOfSettings(PuloniaTaskNodeViewModel node, string profileId)
    {
        EnsureEditableNode(node);
        ApplyMutation(() =>
        {
            node.Model.ProfileId = profileId;
            node.Model.Settings = new JObject();
            node.NotifySharedSettingsChanged();
        }, null);
    }

    /// <summary>
    /// 把设置来源转换为界面文本；<paramref name="viewer"/> 是正在查看的节点，其自身层显示为“本节点”。
    /// </summary>
    internal string DescribeSettingSource(PuloniaSettingSource source, PuloniaTaskNodeViewModel viewer) => source.Kind switch
    {
        PuloniaSettingSourceKind.Default => "默认",
        PuloniaSettingSourceKind.Node => DescribeLevel(source.Level!.Node, viewer),
        _ => $"方案「{source.Level!.Profile?.Name ?? source.Level.Node.ProfileId}」（{DescribeLevel(source.Level.Node, viewer)}）"
    };

    /// <summary>
    /// 列出设置项沿设置链的每一层取值，从默认值开始，最后生效的一层标记出来。
    /// </summary>
    internal IReadOnlyList<PuloniaSharedSettingHistoryEntry> GetSettingHistory(PuloniaTaskNodeViewModel node, string key)
    {
        var definition = PuloniaSettingCatalog.Get(key);
        var entries = new List<(string Location, JToken Value)> { ("默认", definition.DefaultValue) };
        foreach (var level in CreateNodeChain(node, includeOwnSettings: true))
        {
            if (level.Profile?.Settings.TryGetValue(key, out var profileValue) == true)
                entries.Add(($"方案「{level.Profile.Name}」（{DescribeLevel(level.Node, node)}）", profileValue));
            if (level.Node.Settings.TryGetValue(key, out var nodeValue))
                entries.Add((DescribeLevel(level.Node, node), nodeValue));
        }
        return entries.Select((entry, index) => new PuloniaSharedSettingHistoryEntry(entry.Location, entry.Value.DeepClone(),
            index == entries.Count - 1)).ToArray();
    }

    /// <summary>
    /// 移动节点，并比较移动前后子树中各任务实际使用的共享设置有效值。
    /// 同一父节点内排序不改变设置链；未移动或没有任务的有效设置变化时返回 null。
    /// </summary>
    /// <param name="node">被移动的节点。</param>
    /// <param name="targetParent">目标父节点。</param>
    /// <param name="targetIndex">目标父节点中的插入位置（移动前的索引）。</param>
    internal PuloniaMovedSettingsChange? MoveNodeTrackingSettings(PuloniaTaskNodeViewModel node,
        PuloniaTaskNodeViewModel targetParent, int targetIndex)
    {
        var sourceParent = node.Parent;
        if (sourceParent is null || ReferenceEquals(sourceParent, targetParent))
        {
            MoveNode(node, targetParent, targetIndex);
            return null;
        }

        // 1. 记录移动前外层设置与子树中各任务的有效值
        var outerBefore = CaptureOuterSettings(sourceParent);
        var before = CaptureSubtreeSettings(node);
        // 2. 用唯一的合并标识移动，之后的“保留原值”可并入同一步撤销
        var undoMergeKey = "move|" + node.Id + "|" + Guid.NewGuid().ToString("N");
        if (!MoveNode(node, targetParent, targetIndex, undoMergeKey))
            return null;

        // 3. 比较移动后的有效值，按目录顺序汇总变化的设置项
        var after = CaptureSubtreeSettings(node);
        var changedKeys = new HashSet<string>(StringComparer.Ordinal);
        var details = new List<string>();
        var changedTaskCount = 0;
        foreach (var (taskId, (name, beforeValues)) in before)
        {
            if (!after.TryGetValue(taskId, out var afterEntry))
                continue;
            var changed = false;
            foreach (var (key, oldValue) in beforeValues)
            {
                var newValue = afterEntry.Values.GetValueOrDefault(key);
                if (JToken.DeepEquals(oldValue, newValue))
                    continue;
                changed = true;
                changedKeys.Add(key);
                details.Add($"{name}：{PuloniaSettingCatalog.Get(key).DisplayName} "
                            + $"{PuloniaSharedSettingText.Format(key, oldValue)} → {PuloniaSharedSettingText.Format(key, newValue)}");
            }
            if (changed)
                changedTaskCount++;
        }
        if (changedTaskCount == 0)
            return null;
        var orderedKeys = PuloniaSettingCatalog.Definitions.Select(item => item.Key).Where(changedKeys.Contains).ToArray();
        return new PuloniaMovedSettingsChange(node, outerBefore, orderedKeys, changedTaskCount, details, undoMergeKey);
    }

    /// <summary>
    /// 移动之后是否还没有其他编辑或撤销，且子树仍在本文档中，可以保留移动前的设置。
    /// </summary>
    internal bool CanKeepMovedSettings(PuloniaMovedSettingsChange change)
        => _lastUndoMergeKey == change.UndoMergeKey && change.Node.Parent is not null
           && EnumerateNodes().Contains(change.Node);

    /// <summary>
    /// 保留移动前的有效设置：把移动后改变的设置项按移动前外层的值写到被移动的子树根上，与移动合为一步撤销。
    /// 子树根自身或其方案已设置的项由子树内部提供，移动不会改变，不写。
    /// 暂不处理：子树根是计划引用且被引用计划根自身设置了同一项时，写在引用节点上会覆盖被引用计划根的值。
    /// </summary>
    /// <returns>是否写入了设置；移动后已有其他编辑时返回 false。</returns>
    internal bool KeepMovedSettings(PuloniaMovedSettingsChange change)
    {
        if (!CanKeepMovedSettings(change))
            return false;
        var node = change.Node;
        var profile = node.Model.ProfileId is { } profileId ? FindProfile(profileId) : null;
        var keys = change.ChangedKeys
            .Where(key => change.OuterSettingsBefore[key] is not null && !node.Model.Settings.ContainsKey(key)
                          && profile?.Settings.ContainsKey(key) != true)
            .ToArray();
        if (keys.Length == 0)
            return false;
        ApplyMutation(() =>
        {
            foreach (var key in keys)
                node.Model.Settings[key] = change.OuterSettingsBefore[key]!.DeepClone();
            node.NotifySharedSettingsChanged();
        }, node, change.UndoMergeKey);
        return true;
    }

    /// <summary>
    /// 取得子树中每个任务实际使用的共享设置有效值：任务 ID → (显示名, 设置项 → 值)。
    /// 计划引用的内容不在本文档中，按全部设置项比较；分组本身不使用设置，不计入。
    /// </summary>
    private Dictionary<string, (string Name, Dictionary<string, JToken> Values)> CaptureSubtreeSettings(
        PuloniaTaskNodeViewModel subtree)
    {
        var result = new Dictionary<string, (string Name, Dictionary<string, JToken> Values)>(StringComparer.Ordinal);
        foreach (var node in Flatten(subtree))
        {
            var task = node.Model;
            IEnumerable<string>? keys = task.Source switch
            {
                { Kind: "plan" } => PuloniaSettingCatalog.Definitions.Select(item => item.Key),
                { Kind: "directory", TaskType: { } directoryType } => PuloniaParameterResolver
                    .FindDefinition(_definitions, new PuloniaTask { TaskType = directoryType })?.SettingBindings.Keys,
                _ => task.TaskType == "group" ? null : GetDefinition(task)?.SettingBindings.Keys
            };
            if (keys is null)
                continue;
            var values = ResolveNodeSettings(node).Values;
            var used = keys.Where(values.ContainsKey).ToDictionary(key => key, key => values[key], StringComparer.Ordinal);
            if (used.Count > 0)
                result[node.Id] = ($"任务「{node.Name}」", used);
        }
        return result;
    }

    /// <summary>节点自身是否为计划根。</summary>
    internal bool IsPlanRoot(PuloniaTaskNodeViewModel node) => node.Model.Id == _plan.RootTask.Id;

    /// <summary>
    /// 建立从计划根到节点（含）的设置链；不含自身设置时以只带方案的探针代替节点。
    /// </summary>
    private IReadOnlyList<PuloniaSettingLevel> CreateNodeChain(PuloniaTaskNodeViewModel node, bool includeOwnSettings)
    {
        var models = GetAncestorModels(node).ToList();
        if (!includeOwnSettings)
            models[^1] = new PuloniaTask
            {
                Id = node.Model.Id, Name = node.Model.Name, TaskType = node.Model.TaskType, ProfileId = node.Model.ProfileId
            };
        return CreateChain(models);
    }

    /// <summary>描述设置链中的一个节点。</summary>
    private string DescribeLevel(PuloniaTask task, PuloniaTaskNodeViewModel viewer)
        => task.Id == viewer.Id ? "本节点"
            : task.Id == _plan.RootTask.Id ? "计划"
            : task.Source?.Kind == "plan" ? $"引用「{task.Name}」"
            : task.TaskType == "group" ? $"分组「{task.Name}」"
            : $"任务「{task.Name}」";
}
