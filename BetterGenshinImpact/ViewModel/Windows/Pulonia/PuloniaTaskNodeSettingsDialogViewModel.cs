using System;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 编辑任务节点弹窗视图模型：名称、启用状态、执行次数、资源路径与任务设置均为草稿，点击“保存”后一次性提交；
/// 高级设置区由窗口专属的 <see cref="Advanced"/> 按需计算，点击各自的“应用”后即时生效。
/// </summary>
public partial class PuloniaTaskNodeSettingsDialogViewModel : ObservableObject
{
    /// <summary>独立任务注册表，用于查找专属设置界面。</summary>
    private readonly SoloTaskRegistry? _registry;

    /// <summary>节点对应的能力定义；分组或未注册能力为空。</summary>
    private readonly PuloniaTaskDefinition? _definition;

    /// <summary>编辑会话，提供更新资源、设置方案等会话级命令。</summary>
    public PuloniaPlanEditingSession Session { get; }

    /// <summary>节点所属的计划文档。</summary>
    public PuloniaTaskPlanDocumentViewModel Document { get; }

    /// <summary>共享设置入口、脚本设置入口与高级区的原始 JSON，只在本窗口打开期间存在。</summary>
    public PuloniaTaskNodeAdvancedSettingsViewModel Advanced { get; }

    /// <summary>被编辑的节点。</summary>
    public PuloniaTaskNodeViewModel Node { get; }

    /// <summary>名称草稿。</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>启用状态草稿。</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>分组执行次数草稿。</summary>
    [ObservableProperty]
    private int _repeatCount;

    /// <summary>资源路径草稿。</summary>
    [ObservableProperty]
    private string? _path;

    /// <summary>任务设置编辑器；只有独立任务提供，其余节点为空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettings), nameof(HasTaskTools))]
    private PuloniaTaskSettingsEditorViewModel? _settings;

    /// <summary>保存失败等提示信息。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>弹窗标题。</summary>
    public string WindowTitle => $"编辑任务：{Node.Name}";

    /// <summary>是否展示任务设置区。</summary>
    public bool HasSettings => Settings is not null;

    /// <summary>是否允许编辑分组执行次数。</summary>
    public bool CanConfigureRepeat => Node.CanConfigureRepeat;

    /// <summary>是否允许编辑资源路径：只有未绑定仓库资源的地图追踪、JS 与录制回放任务使用本地路径。</summary>
    public bool CanEditPath => Node.Model.Resource is null && Node.TaskType is "pathing" or "javascript" or "keymouse";

    /// <summary>是否展示资源版本更新。</summary>
    public bool CanUpdateResourceVersion => Node.CanUpdateResourceVersion;

    /// <summary>是否展示“基本”区中的执行或资源设置。</summary>
    public bool HasBasicOptions => CanConfigureRepeat || CanEditPath || CanUpdateResourceVersion;

    /// <summary>非独立任务是否提供脚本自定义设置入口。</summary>
    public bool HasTaskTools => !HasSettings && Advanced.CanEditScriptSettings;

    /// <summary>节点是否为独立任务。</summary>
    private bool IsSoloTask => PuloniaSoloTaskExecutor.IsSoloTaskType(Node.TaskType);

    /// <summary>请求宿主窗口按保存（true）或取消（false）关闭。</summary>
    public event EventHandler<bool>? RequestClose;

    /// <summary>
    /// 建立编辑弹窗视图模型；高级设置区按被编辑节点计算，不依赖文档的当前选中节点。
    /// </summary>
    /// <param name="session">编辑会话。</param>
    /// <param name="node">被编辑的节点。</param>
    /// <param name="definition">节点能力定义。</param>
    /// <param name="registry">独立任务注册表。</param>
    /// <param name="dialogs">共享设置、路线 CD 与脚本设置窗口入口；为空时这些窗口不可用。</param>
    public PuloniaTaskNodeSettingsDialogViewModel(PuloniaPlanEditingSession session, PuloniaTaskNodeViewModel node,
        PuloniaTaskDefinition? definition, SoloTaskRegistry? registry, IPuloniaTaskSettingsDialogs? dialogs = null)
    {
        Session = session;
        Node = node;
        Document = node.Document;
        Advanced = new PuloniaTaskNodeAdvancedSettingsViewModel(node.Document, node, dialogs, session);
        _definition = definition;
        _registry = registry;
        _name = node.Name;
        _isEnabled = node.IsEnabled;
        _repeatCount = node.RepeatCount;
        _path = node.Model.Path;
        _settings = CreateSettings();
        // 高级设置即时修改参数后同步刷新任务设置区，保证显示的是最新有效值
        Document.ContentChanged += OnDocumentContentChanged;
    }

    /// <summary>
    /// 校验并一次性提交全部草稿，成功后关闭弹窗。
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        var name = Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "任务名称不能为空。";
            return;
        }
        try
        {
            // 1. 收集任务设置变化，并确认这些字段没有在打开弹窗后被高级设置改写
            var changes = Settings?.CollectChanges() ?? new JObject();
            if (Settings is not null && !Settings.IsSourceUnchanged(Document.GetOneDragonParameters(Node), changes))
                throw new InvalidOperationException("部分设置已在高级设置中被修改，请取消后重新打开再编辑。");

            // 2. 名称、启用、次数、路径与参数作为一次可撤销编辑提交
            Document.ApplyNodeSettings(Node, name, IsEnabled, RepeatCount, CanEditPath ? Path : Node.Model.Path, changes);
            RequestClose?.Invoke(this, true);
        }
        catch (Exception ex) when (ex is FormatException or Newtonsoft.Json.JsonException
                                       or PuloniaTaskValidationException or InvalidOperationException)
        {
            StatusMessage = ex.Message;
        }
    }

    /// <summary>放弃草稿并关闭弹窗；高级设置中已应用的修改不受影响。</summary>
    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, false);

    /// <summary>弹窗关闭时解除文档事件订阅。</summary>
    public void Close()
    {
        Document.ContentChanged -= OnDocumentContentChanged;
        Advanced.Dispose();
    }

    /// <summary>独立任务按当前有效参数建立设置编辑器，其余节点不提供。</summary>
    /// <returns>设置编辑器或 null。</returns>
    private PuloniaTaskSettingsEditorViewModel? CreateSettings()
    {
        var settings = _definition is not null && Node.IsLeaf && IsSoloTask
            ? PuloniaTaskSettingsEditorViewModel.Create(_registry, _definition, Document.GetOneDragonParameters(Node))
            : null;
        if (settings?.DedicatedSettings is AutoBossSettingsViewModel boss)
            _ = boss.SetProgressScopeAsync(Document.Id, Node.Id);
        return settings;
    }

    /// <summary>参数在高级设置中被即时修改后，若任务设置没有未保存输入则按最新值重建。</summary>
    private void OnDocumentContentChanged(object? sender, EventArgs e)
    {
        if (Settings is null || Settings.HasPendingChanges())
            return;
        Settings = CreateSettings();
    }
}
