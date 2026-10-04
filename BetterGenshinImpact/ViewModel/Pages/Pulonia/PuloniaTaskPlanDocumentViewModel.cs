using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
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

    /// <summary>
    /// 当前可编辑的持久化计划模型。
    /// </summary>
    private PuloniaTaskPlan _plan;

    /// <summary>
    /// 最近的变更前快照，末项最先撤销。
    /// </summary>
    private readonly List<string> _undoSnapshots = [];

    /// <summary>
    /// 最近一次成功读取或保存后的内容快照；新计划为 null。
    /// </summary>
    private string? _savedSnapshot;

    /// <summary>
    /// 刷新编辑器控件时阻止选项回写模型。
    /// </summary>
    private bool _isRefreshingEditor;

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
    /// 参数或分组公共覆盖的 JSON 编辑文本。
    /// </summary>
    [ObservableProperty]
    private string _parameterJson = "{}";

    /// <summary>
    /// 执行策略的 JSON 编辑文本。
    /// </summary>
    [ObservableProperty]
    private string _policyJson = "{}";

    /// <summary>
    /// 目录或计划引用来源的 JSON 编辑文本。
    /// </summary>
    [ObservableProperty]
    private string _sourceJson = string.Empty;

    /// <summary>
    /// 参数编辑器当前选择的共享预设。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskPresetOptionViewModel? _selectedPresetOption;

    /// <summary>
    /// 参数编辑区域最近一次解析或应用结果。
    /// </summary>
    [ObservableProperty]
    private string? _editorMessage;

    /// <summary>
    /// 计划的内部根节点；界面只展示其子节点，空白区域命令以该节点为插入目标。
    /// </summary>
    public PuloniaTaskNodeViewModel RootNode { get; private set; } = null!;

    /// <summary>
    /// 当前节点可选择的共享预设。
    /// </summary>
    public ObservableCollection<PuloniaTaskPresetOptionViewModel> PresetOptions { get; } = [];

    /// <summary>
    /// 当前叶子节点的有效参数和来源预览。
    /// </summary>
    public ObservableCollection<PuloniaTaskEffectiveParameterViewModel> EffectiveParameters { get; } = [];

    /// <summary>
    /// 文档内容或选择发生变化时通知页面刷新跨文档命令状态。
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 计划持久化内容发生变化时通知页面安排自动保存；纯选择变化不会触发。
    /// </summary>
    public event EventHandler? ContentChanged;

    /// <summary>
    /// 当前编辑模型，仅供保存服务使用。
    /// </summary>
    internal PuloniaTaskPlan Plan => _plan;

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
    /// 当前节点是否允许选择预设。
    /// </summary>
    public bool CanSelectPreset => SelectedNode?.IsLeaf == true;

    /// <summary>
    /// 当前节点是否允许编辑资源定位。
    /// </summary>
    public bool CanEditPath => SelectedNode?.IsLeaf == true;

    /// <summary>
    /// 当前节点是否存在来源配置。
    /// </summary>
    public bool HasSource => SelectedNode?.Model.Source is not null;

    /// <summary>
    /// 当前参数编辑框的内容说明。
    /// </summary>
    public string ParameterEditorTitle => SelectedNode?.TaskType == "group"
        ? "分组公共参数覆盖（JSON 数组）"
        : "节点参数（JSON 对象）";

    /// <summary>
    /// 建立一个已加载或新建的计划编辑会话。
    /// </summary>
    public PuloniaTaskPlanDocumentViewModel(PuloniaTaskPlan plan, IReadOnlyList<PuloniaTaskPreset> presets,
        PuloniaTaskClipboardService clipboard, bool isNew)
    {
        _plan = plan;
        _presets = presets;
        _clipboard = clipboard;
        _savedSnapshot = isNew ? null : SerializeForHistory(plan);
        RebuildTree(plan.RootTask.Id);
        IsDirty = isNew;
    }

    /// <summary>
    /// 在变更前记录快照并执行一次原子内存编辑。
    /// </summary>
    internal void ApplyMutation(Action mutation, PuloniaTaskNodeViewModel? selection)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        RecordUndoSnapshot();
        mutation();
        LastSaveError = null;
        UpdateDirtyState();
        if (selection is not null)
            SelectedNode = selection;
        RefreshSelectedEditor();
        Changed?.Invoke(this, EventArgs.Empty);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 保存不进入撤销栈的界面状态，例如树节点展开状态。
    /// </summary>
    internal void ApplyViewStateMutation(Action mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        mutation();
        LastSaveError = null;
        UpdateDirtyState();
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
        EditorMessage = $"已复制“{node.Name}”，可切换计划后粘贴。";
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
        RefreshSelectedEditor();
    }

    /// <summary>
    /// 设置保存失败信息而不丢弃当前内存草稿。
    /// </summary>
    internal void SetSaveError(string message)
    {
        LastSaveError = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 根据当前选中节点刷新 JSON、预设及有效参数预览。
    /// </summary>
    internal void RefreshSelectedEditor()
    {
        _isRefreshingEditor = true;
        try
        {
            PresetOptions.Clear();
            EffectiveParameters.Clear();
            EditorMessage = null;
            var node = SelectedNode;
            if (node is null)
            {
                ParameterJson = "{}";
                PolicyJson = "{}";
                SourceJson = string.Empty;
                SelectedPresetOption = null;
                NotifyEditorPropertiesChanged();
                return;
            }

            ParameterJson = node.TaskType == "group"
                ? JArray.FromObject(node.Model.ParameterOverrides).ToString(Formatting.Indented)
                : node.Model.Parameters.ToString(Formatting.Indented);
            PolicyJson = JObject.FromObject(node.Model.Policy).ToString(Formatting.Indented);
            SourceJson = node.Model.Source is null
                ? string.Empty
                : JObject.FromObject(node.Model.Source).ToString(Formatting.Indented);

            PresetOptions.Add(new PuloniaTaskPresetOptionViewModel(null, "不使用共享预设"));
            foreach (var preset in _presets.Where(preset => MatchesScope(preset.TaskType, preset.ResourceId, node.Model))
                         .OrderBy(preset => preset.Name, StringComparer.CurrentCulture))
                PresetOptions.Add(new PuloniaTaskPresetOptionViewModel(preset.Id, $"{preset.Name} · r{preset.Revision}"));
            if (node.Model.PresetId is { } selectedPresetId
                && PresetOptions.All(option => option.Id != selectedPresetId))
            {
                var knownPreset = _presets.FirstOrDefault(preset => preset.Id == selectedPresetId);
                PresetOptions.Add(new PuloniaTaskPresetOptionViewModel(selectedPresetId,
                    knownPreset is null
                        ? $"⚠ 缺失预设：{selectedPresetId}"
                        : $"⚠ 预设与节点不匹配：{knownPreset.Name}"));
                EditorMessage = knownPreset is null
                    ? "当前节点引用的共享预设不存在，保存草稿不会自动清除该引用。"
                    : "当前共享预设的任务类型或资源与节点不匹配，请重新选择。";
            }
            SelectedPresetOption = PresetOptions.FirstOrDefault(option => option.Id == node.Model.PresetId)
                                   ?? PresetOptions[0];

            if (node.IsLeaf)
            {
                var effective = ResolveEffectiveParameters(node.Model, GetAncestorModels(node.Parent), out var sources);
                foreach (var property in effective.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    EffectiveParameters.Add(new PuloniaTaskEffectiveParameterViewModel(property.Name,
                        property.Value.ToString(Formatting.None), sources[property.Name]));
                }
            }
            NotifyEditorPropertiesChanged();
        }
        finally
        {
            _isRefreshingEditor = false;
        }
    }

    /// <summary>
    /// 解析并应用参数 JSON；解析或校验失败时保留原模型和输入文本。
    /// </summary>
    [RelayCommand]
    private void ApplyParameters()
    {
        var node = SelectedNode;
        if (node is null)
            return;
        try
        {
            if (node.TaskType == "group")
            {
                var overrides = ReadParameterOverrides(ParameterJson);
                ApplyMutation(() => node.Model.ParameterOverrides = overrides, node);
            }
            else
            {
                var parameters = PuloniaTaskJson.Read<JObject>(ParameterJson);
                ApplyMutation(() => node.Model.Parameters = parameters, node);
            }
            EditorMessage = "参数已应用到草稿，保存计划后写入磁盘。";
        }
        catch (Exception ex) when (ex is JsonException or PuloniaTaskValidationException or InvalidOperationException)
        {
            EditorMessage = "参数未应用：" + ex.Message;
            ThemedMessageBox.Error(EditorMessage, "参数格式错误");
        }
    }

    /// <summary>
    /// 解析并应用执行策略 JSON；失败时不修改原策略。
    /// </summary>
    [RelayCommand]
    private void ApplyPolicy()
    {
        var node = SelectedNode;
        if (node is null)
            return;
        try
        {
            var policy = PuloniaTaskJson.Read<PuloniaTaskPolicy>(PolicyJson);
            PuloniaTaskValidator.ValidatePolicy(policy, node.Id + "/policy");
            ApplyMutation(() => node.Model.Policy = policy, node);
            EditorMessage = "执行策略已应用到草稿。";
        }
        catch (Exception ex) when (ex is JsonException or PuloniaTaskValidationException)
        {
            EditorMessage = "执行策略未应用：" + ex.Message;
            ThemedMessageBox.Error(EditorMessage, "执行策略格式错误");
        }
    }

    /// <summary>
    /// 解析并应用计划或目录引用来源；空文本会清除来源。
    /// </summary>
    [RelayCommand]
    private void ApplySource()
    {
        var node = SelectedNode;
        if (node is null)
            return;
        try
        {
            var source = string.IsNullOrWhiteSpace(SourceJson)
                ? null
                : PuloniaTaskJson.Read<PuloniaTaskSource>(SourceJson);
            ApplyMutation(() =>
            {
                node.Model.Source = source;
                node.NotifySourceChanged();
            }, node);
            EditorMessage = "引用来源已应用到草稿。";
        }
        catch (Exception ex) when (ex is JsonException or PuloniaTaskValidationException)
        {
            EditorMessage = "引用来源未应用：" + ex.Message;
            ThemedMessageBox.Error(EditorMessage, "引用来源格式错误");
        }
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
    /// 在预设下拉框变化时立即更新模型和有效参数来源预览。
    /// </summary>
    partial void OnSelectedPresetOptionChanged(PuloniaTaskPresetOptionViewModel? value)
    {
        if (_isRefreshingEditor || SelectedNode is not { IsLeaf: true } node)
            return;
        if (node.Model.PresetId == value?.Id)
            return;
        ApplyMutation(() => node.Model.PresetId = value?.Id, node);
    }

    /// <summary>
    /// 在树选中项变化时刷新右侧统一参数编辑器。
    /// </summary>
    partial void OnSelectedNodeChanged(PuloniaTaskNodeViewModel? value)
    {
        RefreshSelectedEditor();
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
    /// 通知所有依赖当前选中节点的编辑器属性刷新绑定。
    /// </summary>
    private void NotifyEditorPropertiesChanged()
    {
        OnPropertyChanged(nameof(CanSelectPreset));
        OnPropertyChanged(nameof(CanEditPath));
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(ParameterEditorTitle));
        OnPropertyChanged(nameof(CanEditPathingSettings));
        OnPropertyChanged(nameof(CanEditJavaScriptSettings));
        OnPropertyChanged(nameof(CanEditShellSettings));
        OnPropertyChanged(nameof(CanEditCommonSettings));
    }

    /// <summary>
    /// 计算叶子节点的有效参数及每个字段的最后来源。
    /// </summary>
    private JObject ResolveEffectiveParameters(PuloniaTask task, IReadOnlyList<PuloniaTask> ancestors,
        out Dictionary<string, string> sources)
    {
        var result = new JObject();
        sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var selectedPreset = task.PresetId is null
            ? null
            : _presets.FirstOrDefault(preset => preset.Id == task.PresetId);
        var schemaVersion = selectedPreset?.SchemaVersion ?? 1;
        if (selectedPreset is not null && MatchesScope(selectedPreset.TaskType, selectedPreset.ResourceId, task))
            ApplyValues(result, sources, selectedPreset.Values, $"预设“{selectedPreset.Name}”");

        foreach (var ancestor in ancestors)
        {
            foreach (var item in ancestor.ParameterOverrides
                         .Where(item => item.SchemaVersion == schemaVersion && MatchesScope(item.TaskType, item.ResourceId, task))
                         .OrderBy(item => item.ResourceId is null ? 0 : 1))
                ApplyValues(result, sources, item.Values, $"分组“{ancestor.Name}”");
        }
        ApplyValues(result, sources, task.Parameters, $"节点“{task.Name}”");
        return result;
    }

    /// <summary>
    /// 以顶层字段整体替换方式合并参数，并同步记录字段来源。
    /// </summary>
    private static void ApplyValues(JObject result, IDictionary<string, string> sources, JObject values, string source)
    {
        foreach (var property in values.Properties())
        {
            result[property.Name] = property.Value.DeepClone();
            sources[property.Name] = source;
        }
    }

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
    /// 判断预设或公共覆盖是否与具体叶子的类型和资源一致。
    /// </summary>
    private static bool MatchesScope(string taskType, string? resourceId, PuloniaTask task)
        => taskType == task.TaskType && (resourceId is null || resourceId == task.Path);

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
