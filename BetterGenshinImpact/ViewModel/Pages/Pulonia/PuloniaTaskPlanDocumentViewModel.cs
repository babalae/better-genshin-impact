using System;
using BetterGenshinImpact.Core.Script.Repositories;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 表示一个计划的内存编辑会话，集中管理树包装、撤销、参数预览和保存检查点。
/// </summary>
public partial class PuloniaTaskPlanDocumentViewModel : ObservableObject
{
    /// <summary>
    /// 单个编辑会话最多保留的结构变更快照数。
    /// </summary>
    private const int MaxUndoCount = 30;

    /// <summary>
    /// 跨计划复制使用的进程内剪贴板。
    /// </summary>
    private readonly PuloniaTaskClipboardService _clipboard;

    /// <summary>
    /// 当前可用共享预设的只读副本。
    /// </summary>
    private IReadOnlyList<PuloniaTaskPreset> _presets;

    /// <summary>执行器的能力定义，用于与运行时一致的默认参数预览。</summary>
    private IReadOnlyList<PuloniaTaskDefinition> _definitions = [];

    /// <summary>
    /// 当前可编辑的持久化计划模型。
    /// </summary>
    private PuloniaTaskPlan _plan;

    /// <summary>
    /// 最近的变更前快照，末项最先撤销。
    /// </summary>
    private readonly List<string> _undoSnapshots = [];

    /// <summary>
    /// 最近一次可合并编辑的标识；连续提交相同标识的编辑（例如逐字输入同一设置）只保留一次撤销快照。
    /// </summary>
    private string? _lastUndoMergeKey;

    /// <summary>
    /// 最近一次成功读取或保存后的内容快照；新计划为 null。
    /// </summary>
    private string? _savedSnapshot;

    /// <summary>
    /// 已折叠的节点 ID；属于编辑器界面状态，不写入计划、不进入撤销或运行快照。
    /// </summary>
    private readonly HashSet<string> _collapsedNodeIds = new(StringComparer.Ordinal);

    /// <summary>当前计划（含计划引用）的待更新资源数，由页面检查结果更新，不持久化提示状态。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResourceUpdates), nameof(ResourceUpdateSummary))]
    private int _resourceUpdateCount;

    /// <summary>当前计划（含计划引用）的资源读取错误数，避免把缺失资源确认为新版本。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResourceUpdates), nameof(ResourceUpdateSummary))]
    private int _resourceErrorCount;

    /// <summary>计划卡片是否有可以更新的资源；其他资源检查失败不隐藏可用项。</summary>
    public bool HasResourceUpdates => ResourceUpdateCount > 0;

    /// <summary>计划卡片的资源检查摘要与批量更新按钮说明。</summary>
    public string ResourceUpdateSummary => ResourceUpdateCount > 0 && ResourceErrorCount > 0
        ? $"更新此计划的 {ResourceUpdateCount} 项资源；另有 {ResourceErrorCount} 项检查失败"
        : ResourceUpdateCount > 0 ? $"更新此计划的 {ResourceUpdateCount} 项任务资源"
        : ResourceErrorCount > 0 ? $"{ResourceErrorCount} 项资源检查失败，请查看详情"
        : "任务资源已是本机仓库当前版本";

    /// <summary>按稳定树顺序枚举节点，供资源检查和卡片批量确认使用。</summary>
    public IEnumerable<PuloniaTaskNodeViewModel> EnumerateNodes() => Flatten(RootNode);

    /// <summary>
    /// 以一次可撤销编辑更新本计划全部指定资源。
    /// 保存失败时不自动回滚：新引用留在草稿中并显示 LastSaveError，用户可以撤销本次更新或稍后重试保存。
    /// </summary>
    public void ApplyConfirmedResourceVersions(IReadOnlyDictionary<string, string> versions,
        IReadOnlyDictionary<string, ScriptResourceReference?>? references = null)
    {
        // 节点被删除、改了路径或来源（会清空检查结果）时拒绝整批，不部分更新同一计划。
        var nodes = EnumerateNodes().Where(node => versions.ContainsKey(node.Id)).ToArray();
        if (nodes.Length != versions.Count || nodes.Any(node => !node.CanUpdateResourceVersion
            || node.ResourceCheckError is not null || node.CurrentResourceVersion != versions[node.Id]))
            throw new InvalidOperationException("资源检查结果已经变化，请重新检查并确认。");
        if (nodes.Length == 0)
            return;
        // 全部校验通过后才一起更新模型，保留任务参数、开关、目录引用和原执行历史。
        ApplyMutation(() =>
        {
            foreach (var node in nodes)
            {
                if (node.Model.Source?.Kind == "directory")
                {
                    node.Model.Source.Version = versions[node.Id];
                    if (references?.TryGetValue(node.Id, out var reference) == true) node.Model.Source.Resource = reference;
                }
                else
                {
                    node.Model.ResourceVersion = versions[node.Id];
                    if (references?.TryGetValue(node.Id, out var reference) == true) node.Model.Resource = reference;
                }
            }
        }, SelectedNode);
        foreach (var node in nodes)
            node.NotifyResourceVersionChanged();
    }

    /// <summary>
    /// 当前树选中的节点。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskNodeViewModel? _selectedNode;

    /// <summary>
    /// 当前计划是否包含尚未成功保存的更改。
    /// </summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// 最近一次保存失败信息；成功保存后清空。
    /// </summary>
    [ObservableProperty]
    private string? _lastSaveError;

    /// <summary>
    /// 计划的内部根节点；界面只展示其子节点，空白区域命令以该节点为插入目标。
    /// </summary>
    public PuloniaTaskNodeViewModel RootNode { get; private set; } = null!;

    /// <summary>
    /// 文档内容、选择或保存状态发生变化时通知页面刷新跨文档命令状态。
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 计划持久化内容发生变化时通知页面安排自动保存；纯选择变化不会触发。
    /// </summary>
    public event EventHandler? ContentChanged;

    /// <summary>
    /// 节点展开或折叠后通知编辑会话保存界面状态；不改变计划内容。
    /// </summary>
    internal event EventHandler? ViewStateChanged;

    /// <summary>
    /// 共享预设列表刷新后通知打开中的高级设置窗口重新读取预设选项与来源。
    /// </summary>
    internal event EventHandler? PresetsChanged;

    /// <summary>
    /// 本计划独立的保存门，确保同一计划的修订始终按顺序写入；删除后不释放，排队的保存通过 <see cref="IsDeleted"/> 退出。
    /// </summary>
    internal SemaphoreSlim SaveGate { get; } = new(1, 1);

    /// <summary>
    /// 当前编辑模型，仅供保存服务使用。
    /// </summary>
    internal PuloniaTaskPlan Plan => _plan;

    /// <summary>计划的菜单归属；不参与执行树构建或运行策略。</summary>
    public PuloniaTaskPlanPurpose Purpose => _plan.Purpose;

    /// <summary>
    /// 计划已成功删除；尚未结束的异步编辑操作不能重新将其写入磁盘。
    /// </summary>
    internal bool IsDeleted { get; set; }

    /// <summary>
    /// 计划稳定 ID。
    /// </summary>
    public string Id => _plan.Id;

    /// <summary>
    /// 当前文件修订号。
    /// </summary>
    public long Revision => _plan.Revision;

    /// <summary>
    /// 计划名称；失焦提交时形成一次撤销记录。
    /// </summary>
    public string Name
    {
        get => _plan.Name;
        set
        {
            if (_plan.Name == value)
                return;
            var previousName = _plan.Name;
            var renameRoot = _plan.RootTask.Name == previousName;
            ApplyMutation(() =>
            {
                _plan.Name = value;
                // 默认根节点跟随计划名；用户单独改过根节点名称后不再强制覆盖。
                if (renameRoot)
                    _plan.RootTask.Name = value;
            }, SelectedNode);
            OnPropertyChanged();
            if (renameRoot)
                RootNode.NotifyNameChanged();
        }
    }

    /// <summary>
    /// 计划说明；失焦提交时形成一次撤销记录。
    /// </summary>
    public string? Description
    {
        get => _plan.Description;
        set
        {
            if (_plan.Description == value)
                return;
            ApplyMutation(() => _plan.Description = value, SelectedNode);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 当前是否可以撤销最近一次结构或配置编辑。
    /// </summary>
    public bool CanUndo => _undoSnapshots.Count > 0;

    /// <summary>
    /// 建立一个已加载或新建的计划编辑会话。
    /// </summary>
    /// <param name="plan">计划模型。</param>
    /// <param name="presets">可用共享预设。</param>
    /// <param name="clipboard">跨计划剪贴板。</param>
    /// <param name="isNew">是否为尚未保存的新计划。</param>
    /// <param name="collapsedNodeIds">编辑器记录的已折叠节点；为空时全部展开。</param>
    public PuloniaTaskPlanDocumentViewModel(PuloniaTaskPlan plan, IReadOnlyList<PuloniaTaskPreset> presets,
        PuloniaTaskClipboardService clipboard, bool isNew, IEnumerable<string>? collapsedNodeIds = null)
    {
        _plan = plan;
        _presets = presets;
        _clipboard = clipboard;
        _savedSnapshot = isNew ? null : SerializeForHistory(plan);
        if (collapsedNodeIds is not null)
            _collapsedNodeIds.UnionWith(collapsedNodeIds);
        RebuildTree(plan.RootTask.Id);
        IsDirty = isNew;
    }

    /// <summary>
    /// 指定节点在任务树中是否展开；未记录的节点默认展开。
    /// </summary>
    /// <param name="nodeId">节点 ID。</param>
    internal bool IsNodeExpanded(string nodeId) => !_collapsedNodeIds.Contains(nodeId);

    /// <summary>
    /// 记录节点展开或折叠；只改变界面状态，不标记计划为已修改。
    /// </summary>
    /// <param name="nodeId">节点 ID。</param>
    /// <param name="isExpanded">是否展开。</param>
    internal void SetNodeExpanded(string nodeId, bool isExpanded)
    {
        var changed = isExpanded ? _collapsedNodeIds.Remove(nodeId) : _collapsedNodeIds.Add(nodeId);
        if (changed)
            ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 取得仍存在于当前树中的折叠节点 ID，已删除节点的旧记录随之丢弃。
    /// </summary>
    internal IReadOnlyCollection<string> GetCollapsedNodeIds()
        => EnumerateNodes().Select(node => node.Id).Where(_collapsedNodeIds.Contains).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 在变更前记录快照并执行一次原子内存编辑。
    /// </summary>
    /// <param name="mutation">要执行的编辑。</param>
    /// <param name="selection">编辑后选中的节点；为空时保持当前选择。</param>
    /// <param name="undoMergeKey">可合并编辑的标识；与上一次编辑相同时不新增撤销快照，为空表示独立编辑。</param>
    internal void ApplyMutation(Action mutation, PuloniaTaskNodeViewModel? selection, string? undoMergeKey = null)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (undoMergeKey is null || undoMergeKey != _lastUndoMergeKey || _undoSnapshots.Count == 0)
            RecordUndoSnapshot();
        _lastUndoMergeKey = undoMergeKey;
        mutation();
        LastSaveError = null;
        UpdateDirtyState();
        if (selection is not null)
            SelectedNode = selection;
        RefreshConfigurationSummaries();
        Changed?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 在目标父节点中插入子树，并作为一次可撤销编辑处理。
    /// </summary>
    internal PuloniaTaskNodeViewModel InsertNode(PuloniaTask task, PuloniaTaskNodeViewModel parent, int index)
    {
        var wrapper = new PuloniaTaskNodeViewModel(this, task, parent);
        var targetIndex = Math.Clamp(index, 0, parent.Children.Count);
        ApplyMutation(() =>
        {
            parent.Model.Children.Insert(targetIndex, task);
            parent.Children.Insert(targetIndex, wrapper);
            parent.NotifyCanAcceptChildrenChanged();
        }, wrapper);
        return wrapper;
    }

    /// <summary>
    /// 移动已有节点，保持节点 ID 和全部参数不变，并禁止形成树环路。
    /// </summary>
    internal void MoveNode(PuloniaTaskNodeViewModel node, PuloniaTaskNodeViewModel targetParent, int targetIndex)
    {
        var sourceParent = node.Parent;
        if (sourceParent is null || ReferenceEquals(node, targetParent) || node.ContainsDescendant(targetParent))
            return;
        if (!targetParent.CanAcceptChildren && !ReferenceEquals(sourceParent, targetParent))
            return;

        var sourceIndex = sourceParent.Children.IndexOf(node);
        if (sourceIndex < 0)
            return;
        var adjustedIndex = Math.Clamp(targetIndex, 0, targetParent.Children.Count);
        if (ReferenceEquals(sourceParent, targetParent) && sourceIndex < adjustedIndex)
            adjustedIndex--;
        if (ReferenceEquals(sourceParent, targetParent) && sourceIndex == adjustedIndex)
            return;

        ApplyMutation(() =>
        {
            sourceParent.Children.RemoveAt(sourceIndex);
            sourceParent.Model.Children.RemoveAt(sourceIndex);
            adjustedIndex = Math.Clamp(adjustedIndex, 0, targetParent.Children.Count);
            targetParent.Children.Insert(adjustedIndex, node);
            targetParent.Model.Children.Insert(adjustedIndex, node.Model);
            node.SetParent(targetParent);
            sourceParent.NotifyCanAcceptChildrenChanged();
            targetParent.NotifyCanAcceptChildrenChanged();
        }, node);
    }

    /// <summary>
    /// 删除一个非根节点，删除前的完整计划可通过撤销恢复。
    /// </summary>
    internal void RemoveNode(PuloniaTaskNodeViewModel node)
    {
        var parent = node.Parent;
        if (parent is null)
            return;
        var index = parent.Children.IndexOf(node);
        if (index < 0)
            return;
        var removedIds = Flatten(node).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        ApplyMutation(() =>
        {
            parent.Children.RemoveAt(index);
            parent.Model.Children.RemoveAt(index);
            // 账号预设选择属于计划内部引用，删除子树时同步清理，避免留下悬空节点 ID。
            foreach (var account in _plan.Accounts)
            foreach (var taskId in account.PresetSelections.Keys.Where(removedIds.Contains).ToArray())
                account.PresetSelections.Remove(taskId);
            parent.NotifyCanAcceptChildrenChanged();
        }, parent);
    }

    /// <summary>
    /// 将子树及复制时每个叶子的有效参数存入跨计划剪贴板。
    /// </summary>
    internal void CopyNode(PuloniaTaskNodeViewModel node)
    {
        _clipboard.Set(Name, node.Model, CaptureEffectiveLeafParameters(node));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 显式启用指定节点的全部后代，不把父组的临时关闭状态写入子项。
    /// </summary>
    internal void EnableAllChildren(PuloniaTaskNodeViewModel node)
    {
        if (!node.Children.SelectMany(Flatten).Any(item => !item.IsEnabled))
            return;
        ApplyMutation(() =>
        {
            foreach (var child in node.Children.SelectMany(Flatten))
                child.Model.IsEnabled = true;
            node.NotifyEffectiveEnabledChangedRecursively();
        }, node);
    }

    /// <summary>
    /// 收集指定子树中叶子节点复制当时的有效参数。
    /// </summary>
    internal IReadOnlyDictionary<string, JObject> CaptureEffectiveLeafParameters(PuloniaTaskNodeViewModel subtree)
    {
        var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var node in Flatten(subtree))
        {
            if (node.IsLeaf)
                result[node.Id] = ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out _);
        }
        return result;
    }

    /// <summary>
    /// 比较复制位置改变后各叶子的有效参数，判断是否需要提示继承差异。
    /// </summary>
    internal bool HasPasteInheritanceDifference(PuloniaTask sourceSubtree, PuloniaTask pastedSubtree,
        IReadOnlyDictionary<string, JObject> sourceEffectiveParameters, PuloniaTaskNodeViewModel targetParent)
    {
        var outerAncestors = GetAncestorModels(targetParent);
        return HasDifference(sourceSubtree, pastedSubtree, outerAncestors);

        bool HasDifference(PuloniaTask source, PuloniaTask pasted, IReadOnlyList<PuloniaTask> ancestors)
        {
            if (IsLeaf(pasted))
            {
                var targetEffective = ResolveEffectiveParameters(pasted, ancestors, out _);
                return !sourceEffectiveParameters.TryGetValue(source.Id, out var sourceEffective)
                       || !JToken.DeepEquals(sourceEffective, targetEffective);
            }

            var nestedAncestors = ancestors.Concat([pasted]).ToArray();
            for (var i = 0; i < Math.Min(source.Children.Count, pasted.Children.Count); i++)
            {
                if (HasDifference(source.Children[i], pasted.Children[i], nestedAncestors))
                    return true;
            }
            return source.Children.Count != pasted.Children.Count;
        }
    }

    /// <summary>
    /// 把复制来源的有效参数写成新叶子的显式参数，尽量隔离目标父组的继承差异。
    /// </summary>
    internal static void PreserveCopiedEffectiveParameters(PuloniaTask sourceSubtree, PuloniaTask pastedSubtree,
        IReadOnlyDictionary<string, JObject> sourceEffectiveParameters)
    {
        if (IsLeaf(pastedSubtree)
            && sourceEffectiveParameters.TryGetValue(sourceSubtree.Id, out var effectiveParameters))
            pastedSubtree.Parameters = (JObject)effectiveParameters.DeepClone();

        for (var i = 0; i < Math.Min(sourceSubtree.Children.Count, pastedSubtree.Children.Count); i++)
            PreserveCopiedEffectiveParameters(sourceSubtree.Children[i], pastedSubtree.Children[i], sourceEffectiveParameters);
    }

    /// <summary>
    /// 接受自动保存返回的磁盘修订，并保留保存期间产生的新编辑及现有撤销历史。
    /// </summary>
    internal void AcceptAutoSaved(PuloniaTaskPlan savedPlan)
    {
        // 只推进当前模型的修订号，不能用较早的保存副本覆盖等待期间产生的新编辑。
        _plan.Revision = savedPlan.Revision;
        _savedSnapshot = SerializeForHistory(savedPlan);
        LastSaveError = null;
        UpdateDirtyState();
        OnPropertyChanged(nameof(Revision));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 用磁盘版本替换当前草稿；只由用户明确确认的重新加载操作调用。
    /// </summary>
    internal void ReplaceFromDisk(PuloniaTaskPlan plan)
    {
        _plan = plan;
        _savedSnapshot = SerializeForHistory(plan);
        _undoSnapshots.Clear();
        _lastUndoMergeKey = null;
        LastSaveError = null;
        RebuildTree(plan.RootTask.Id);
        UpdateDirtyState();
        OnPropertyChanged(nameof(Revision));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        UndoCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 更新所有节点可用的共享预设，并立即刷新当前有效参数来源。
    /// </summary>
    internal void SetPresets(IReadOnlyList<PuloniaTaskPreset> presets)
    {
        _presets = presets;
        RefreshConfigurationSummaries();
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>更新能力定义并刷新有效设置，默认值不写入持久化草稿。</summary>
    internal void SetDefinitions(IReadOnlyList<PuloniaTaskDefinition> definitions)
    {
        _definitions = definitions;
        RefreshConfigurationSummaries();
    }

    /// <summary>按资源优先、通用类型其次选择能力，与运行构建共用同一查找规则。</summary>
    private PuloniaTaskDefinition? GetDefinition(PuloniaTask task) => PuloniaParameterResolver.FindDefinition(_definitions, task);

    /// <summary>
    /// 设置保存失败信息而不丢弃当前内存草稿。
    /// </summary>
    internal void SetSaveError(string message)
    {
        LastSaveError = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 恢复最近一次变更前快照，撤销仅作用于配置草稿。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undoSnapshots.Count == 0)
            return;
        var selectedId = SelectedNode?.Id;
        var snapshot = _undoSnapshots[^1];
        _undoSnapshots.RemoveAt(_undoSnapshots.Count - 1);
        // 撤销后的下一次编辑必须重新记录快照，不能并入已经撤销的编辑。
        _lastUndoMergeKey = null;
        var currentRevision = _plan.Revision;
        _plan = DeserializeHistory(snapshot);
        // 撤销恢复的是内容而不是磁盘版本；沿用最新修订，避免自动保存被误判为旧版本覆盖。
        _plan.Revision = currentRevision;
        LastSaveError = null;
        RebuildTree(selectedId);
        UpdateDirtyState();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Revision));
        UndoCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 树选中项变化时通知页面刷新命令状态；高级设置等编辑器状态由打开的窗口按需计算。
    /// </summary>
    partial void OnSelectedNodeChanged(PuloniaTaskNodeViewModel? value)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 重建树包装并尽量恢复原选中节点。
    /// </summary>
    private void RebuildTree(string? selectedId)
    {
        RootNode = new PuloniaTaskNodeViewModel(this, _plan.RootTask, null);
        OnPropertyChanged(nameof(RootNode));
        SelectedNode = Flatten(RootNode).FirstOrDefault(node => node.Id == selectedId) ?? RootNode;
    }

    /// <summary>
    /// 在历史栈未重复时加入变更前快照，并限制会话内存占用。
    /// </summary>
    private void RecordUndoSnapshot()
    {
        var snapshot = SerializeForHistory(_plan);
        if (_undoSnapshots.Count == 0 || _undoSnapshots[^1] != snapshot)
            _undoSnapshots.Add(snapshot);
        if (_undoSnapshots.Count > MaxUndoCount)
            _undoSnapshots.RemoveAt(0);
        UndoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 根据当前内容与保存检查点更新脏状态。
    /// </summary>
    private void UpdateDirtyState()
    {
        IsDirty = _savedSnapshot is null || SerializeForHistory(_plan) != _savedSnapshot;
    }


    /// <summary>
    /// 计算叶子节点的有效参数及每个字段的最后来源（界面展示文本）。
    /// </summary>
    private JObject ResolveEffectiveParameters(PuloniaTask task, IReadOnlyList<PuloniaTask> ancestors,
        out Dictionary<string, string> sources)
    {
        var resolution = ResolveParameters(task, ancestors);
        sources = resolution.Sources.ToDictionary(item => item.Key,
            item => DescribeParameterSource(item.Value, task, ancestors, resolution.Preset), StringComparer.Ordinal);
        return resolution.Parameters;
    }

    /// <summary>
    /// 与运行准备共用 <see cref="PuloniaParameterResolver"/> 解析有效参数；预设缺失或不兼容时跳过该层而不抛异常，
    /// 由结果中的预设判定提示。编辑界面不知道本次运行的账号和调用覆盖，这两层不参与预览。
    /// </summary>
    /// <param name="task">要预览的具体任务。</param>
    /// <param name="ancestors">由根到父节点的模型链。</param>
    private PuloniaParameterResolution ResolveParameters(PuloniaTask task, IReadOnlyList<PuloniaTask> ancestors)
    {
        var preset = task.PresetId is { } presetId ? _presets.FirstOrDefault(item => item.Id == presetId) : null;
        return PuloniaParameterResolver.Resolve(task, GetDefinition(task), task.PresetId, preset,
            ancestors.Select(ancestor => ancestor.ParameterOverrides));
    }

    /// <summary>
    /// 把解析来源转换为“有效参数与来源”中展示的文本。
    /// </summary>
    private static string DescribeParameterSource(PuloniaParameterSource source, PuloniaTask task,
        IReadOnlyList<PuloniaTask> ancestors, PuloniaTaskPreset? preset) => source.Layer switch
    {
        PuloniaParameterLayer.Default => "能力默认值",
        PuloniaParameterLayer.Preset => $"预设“{preset?.Name}”",
        PuloniaParameterLayer.Group => $"分组“{ancestors.FirstOrDefault(ancestor => ancestor.ParameterOverrides.Contains(source.Override!))?.Name}”",
        PuloniaParameterLayer.Node => $"节点“{task.Name}”",
        PuloniaParameterLayer.ReferencePublic => "计划引用公共参数",
        _ => "本次调用覆盖"
    };

    /// <summary>
    /// 取得从根节点到指定父节点的模型链。
    /// </summary>
    private static IReadOnlyList<PuloniaTask> GetAncestorModels(PuloniaTaskNodeViewModel? parent)
    {
        var ancestors = new List<PuloniaTask>();
        for (var current = parent; current is not null; current = current.Parent)
            ancestors.Add(current.Model);
        ancestors.Reverse();
        return ancestors;
    }

    /// <summary>
    /// 判断模型节点是否是具体能力节点。
    /// </summary>
    private static bool IsLeaf(PuloniaTask task) => task.TaskType != "group";

    /// <summary>
    /// 深度优先枚举节点及其全部后代。
    /// </summary>
    private static IEnumerable<PuloniaTaskNodeViewModel> Flatten(PuloniaTaskNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    /// <summary>
    /// 读取分组公共参数数组，并拒绝重复键、尾随根值和未知字段。
    /// </summary>
    private static List<PuloniaTaskParameterOverride> ReadParameterOverrides(string json)
    {
        using var input = new StringReader(json);
        using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None, MaxDepth = 256 };
        var array = JArray.Load(reader,
            new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read())
            throw new JsonSerializationException("JSON 根数组后不能包含其他内容。");
        var serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None,
            MissingMemberHandling = MissingMemberHandling.Error,
            MaxDepth = 256
        });
        return array.ToObject<List<PuloniaTaskParameterOverride>>(serializer)
               ?? throw new JsonSerializationException("公共参数覆盖不能为 null。");
    }

    /// <summary>
    /// 序列化编辑历史；历史允许暂时无效的草稿，正式保存仍走严格校验。
    /// </summary>
    private static string SerializeForHistory(PuloniaTaskPlan plan)
        => JsonConvert.SerializeObject(plan, Formatting.None);

    /// <summary>
    /// 从编辑历史恢复计划，不绕过后续正式保存校验。
    /// </summary>
    private static PuloniaTaskPlan DeserializeHistory(string json)
        => JsonConvert.DeserializeObject<PuloniaTaskPlan>(json, new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None,
            MissingMemberHandling = MissingMemberHandling.Error,
            MaxDepth = 256
        }) ?? throw new JsonSerializationException("撤销快照为空。");
}
