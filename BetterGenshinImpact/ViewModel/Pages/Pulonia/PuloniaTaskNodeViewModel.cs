using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 包装一个可编辑任务节点，负责树显示属性和节点级编辑命令。
/// </summary>
public partial class PuloniaTaskNodeViewModel : ObservableObject
{
    /// <summary>
    /// 节点所属的编辑文档。
    /// </summary>
    private readonly PuloniaTaskPlanDocumentViewModel _document;

    /// <summary>
    /// 当前包装的持久化模型。
    /// </summary>
    private readonly PuloniaTask _model;

    /// <summary>
    /// 父节点；根节点为 null。
    /// </summary>
    private PuloniaTaskNodeViewModel? _parent;

    /// <summary>
    /// 子节点包装集合，与模型 Children 保持相同顺序。
    /// </summary>
    public ObservableCollection<PuloniaTaskNodeViewModel> Children { get; } = [];

    /// <summary>
    /// 持久化节点模型，仅向同一编辑模块开放。
    /// </summary>
    internal PuloniaTask Model => _model;

    /// <summary>
    /// 节点所属的编辑文档。
    /// </summary>
    internal PuloniaTaskPlanDocumentViewModel Document => _document;

    /// <summary>
    /// 父节点；根节点为 null。
    /// </summary>
    public PuloniaTaskNodeViewModel? Parent => _parent;

    /// <summary>
    /// 稳定节点 ID。
    /// </summary>
    public string Id => _model.Id;

    /// <summary>
    /// 持久化任务类型。
    /// </summary>
    public string TaskType => _model.TaskType;

    /// <summary>
    /// 用户可见的任务类型名称。
    /// </summary>
    public string TypeDisplayName => _model.Source?.Kind switch
    {
        "plan" => "计划引用",
        "directory" => "目录引用",
        _ => _model.TaskType switch
        {
            "group" => "分组",
            "pathing" => "地图追踪",
            "javascript" => "JS 脚本",
            "keymouse" => "录制回放",
            "shell" => "Shell",
            "csharp" => "进程内 C#",
            _ when _model.TaskType.StartsWith("builtin.", System.StringComparison.Ordinal) => "内置任务",
            _ => _model.TaskType
        }
    };

    /// <summary>
    /// 当前节点是否可以从磁盘重新确认并固定资源版本。
    /// </summary>
    public bool CanUpdateResourceVersion => _model.TaskType is "pathing" or "javascript" or "keymouse"
                                            || _model is { TaskType: "group", Source.Kind: "directory" };

    /// <summary>
    /// 当前固定的资源版本摘要。
    /// </summary>
    public string ResourceVersionText
    {
        get
        {
            var version = _model.Source?.Kind == "directory"
                ? _model.Source.Version
                : _model.ResourceVersion;
            return string.IsNullOrWhiteSpace(version) ? "未固定" : version;
        }
    }

    /// <summary>
    /// 节点名称；失焦提交时作为一次可撤销编辑。
    /// </summary>
    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name == value)
                return;
            _document.ApplyMutation(() => _model.Name = value, this);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 节点资源定位；允许暂时无效并在保存时统一校验。
    /// </summary>
    public string? Path
    {
        get => _model.Path;
        set
        {
            if (_model.Path == value)
                return;
            _document.ApplyMutation(() => _model.Path = value, this);
            OnPropertyChanged();
            _document.RefreshSelectedEditor();
        }
    }

    /// <summary>
    /// 资源版本更新后通知侧栏和命令状态重新读取。
    /// </summary>
    internal void NotifyResourceVersionChanged()
    {
        OnPropertyChanged(nameof(ResourceVersionText));
        OnPropertyChanged(nameof(CanUpdateResourceVersion));
    }

    /// <summary>
    /// 节点自身启用状态；修改父组不会覆盖子节点保存的选择。
    /// </summary>
    public bool IsEnabled
    {
        get => _model.IsEnabled;
        set
        {
            if (_model.IsEnabled == value)
                return;
            _document.ApplyMutation(() => _model.IsEnabled = value, this);
            OnPropertyChanged();
            NotifyEffectiveEnabledChangedRecursively();
        }
    }

    /// <summary>
    /// 同时考虑全部父节点后的实际启用状态。
    /// </summary>
    public bool IsEffectivelyEnabled => _model.IsEnabled && (_parent?.IsEffectivelyEnabled ?? true);

    /// <summary>
    /// 节点是否因父级关闭而暂停，不改变自身选择。
    /// </summary>
    public bool IsDisabledByParent => _model.IsEnabled && !(_parent?.IsEffectivelyEnabled ?? true);

    /// <summary>
    /// 树视图展开状态。
    /// </summary>
    public bool IsExpanded
    {
        get => _model.IsExpanded;
        set
        {
            if (_model.IsExpanded == value)
                return;
            _document.ApplyViewStateMutation(() => _model.IsExpanded = value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否为不可删除、不可拖动的计划根节点。
    /// </summary>
    public bool IsRoot => _parent is null;

    /// <summary>
    /// 当前节点是否是允许配置执行次数的可见分组。
    /// </summary>
    public bool CanConfigureRepeat => _model.TaskType == "group" && !IsRoot;

    /// <summary>
    /// 分组完整执行子任务列表的次数；一次执行不写入冗余持久化字段。
    /// </summary>
    public int RepeatCount
    {
        get => _model.RepeatCount ?? 1;
        set
        {
            var constrainedValue = System.Math.Clamp(value, 1, PuloniaTaskValidator.MaxTreeNodes);
            int? storedValue = constrainedValue == 1 ? null : constrainedValue;
            if (_model.RepeatCount == storedValue)
                return;
            _document.ApplyMutation(() => _model.RepeatCount = storedValue, this);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否是具体能力节点。
    /// </summary>
    public bool IsLeaf => _model.TaskType != "group";

    /// <summary>
    /// 是否可以直接接收新子节点。
    /// </summary>
    public bool CanAcceptChildren => _model.TaskType == "group" && _model.Source is null;

    /// <summary>
    /// 当前节点是否包含可批量启用的直接或间接子节点。
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// 建立节点包装并递归包装已有子节点。
    /// </summary>
    internal PuloniaTaskNodeViewModel(PuloniaTaskPlanDocumentViewModel document, PuloniaTask model,
        PuloniaTaskNodeViewModel? parent)
    {
        _document = document;
        _model = model;
        _parent = parent;
        foreach (var child in model.Children)
            Children.Add(new PuloniaTaskNodeViewModel(document, child, this));
    }

    /// <summary>
    /// 更新父节点，仅供移动和恢复树结构时调用。
    /// </summary>
    internal void SetParent(PuloniaTaskNodeViewModel? parent)
    {
        _parent = parent;
        OnPropertyChanged(nameof(Parent));
        OnPropertyChanged(nameof(IsRoot));
        OnPropertyChanged(nameof(CanConfigureRepeat));
        NotifyEffectiveEnabledChangedRecursively();
    }

    /// <summary>
    /// 通知当前节点和全部后代重新计算继承后的启用状态。
    /// </summary>
    internal void NotifyEffectiveEnabledChangedRecursively()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsEffectivelyEnabled));
        OnPropertyChanged(nameof(IsDisabledByParent));
        foreach (var child in Children)
            child.NotifyEffectiveEnabledChangedRecursively();
    }

    /// <summary>
    /// 通知界面重新计算当前节点是否还能接收子节点。
    /// </summary>
    internal void NotifyCanAcceptChildrenChanged()
    {
        OnPropertyChanged(nameof(CanAcceptChildren));
        OnPropertyChanged(nameof(HasChildren));
    }

    /// <summary>
    /// 引用来源变化后刷新由来源种类决定的节点显示和容器能力。
    /// </summary>
    internal void NotifySourceChanged()
    {
        OnPropertyChanged(nameof(TypeDisplayName));
        OnPropertyChanged(nameof(CanAcceptChildren));
    }

    /// <summary>
    /// 通知界面刷新由计划重命名同步修改的根节点名称。
    /// </summary>
    internal void NotifyNameChanged()
    {
        OnPropertyChanged(nameof(Name));
    }

    /// <summary>
    /// 判断指定节点是否位于当前节点的后代中，用于阻止拖拽环路。
    /// </summary>
    internal bool ContainsDescendant(PuloniaTaskNodeViewModel candidate)
    {
        for (var current = candidate.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, this))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 复制当前子树到进程内剪贴板。
    /// </summary>
    [RelayCommand]
    private void Copy() => _document.CopyNode(this);

    /// <summary>
    /// 删除当前节点；根节点不会执行删除。
    /// </summary>
    [RelayCommand]
    private void Delete() => _document.RemoveNode(this);

    /// <summary>
    /// 将当前节点设为参数编辑器选中项。
    /// </summary>
    [RelayCommand]
    private void Select() => _document.SelectedNode = this;

    /// <summary>
    /// 显式启用当前分组的全部后代，与父组自身开关相互独立。
    /// </summary>
    [RelayCommand]
    private void EnableAllChildren() => _document.EnableAllChildren(this);
}
