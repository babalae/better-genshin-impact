using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 节点设置窗口打开期间按需计算的编辑状态：共享设置入口与摘要、JS 脚本设置入口，
/// 以及原始任务参数、共享设置、策略、来源 JSON 和有效参数来源。切换计划树选中节点不会刷新它；文档内容变化后重新读取。
/// </summary>
public partial class PuloniaTaskNodeAdvancedSettingsViewModel : ObservableObject, IDisposable
{
    /// <summary>共享设置、路线 CD 与 JS 脚本设置窗口入口；为空时这些窗口不可用（例如测试）。</summary>
    private readonly IPuloniaTaskSettingsDialogs? _dialogs;

    /// <summary>编辑会话，共享设置窗口用它选择和保存方案；为空时窗口不提供方案。</summary>
    private readonly PuloniaPlanEditingSession? _session;

    /// <summary>重新读取编辑器状态期间，阻止回写模型。</summary>
    private bool _isRefreshing;

    /// <summary>节点所属的计划文档。</summary>
    public PuloniaTaskPlanDocumentViewModel Document { get; }

    /// <summary>被编辑节点的稳定 ID；撤销等操作重建树包装后据此找回同一节点。</summary>
    private readonly string _nodeId;

    /// <summary>被编辑的节点；树包装被重建时切换为同一 ID 的新包装。</summary>
    [ObservableProperty] private PuloniaTaskNodeViewModel _node;

    /// <summary>任务参数的 JSON 编辑文本（仅叶子）。</summary>
    [ObservableProperty] private string _parameterJson = "{}";

    /// <summary>本节点共享设置的 JSON 编辑文本。</summary>
    [ObservableProperty] private string _settingsJson = "{}";

    /// <summary>执行策略的 JSON 编辑文本。</summary>
    [ObservableProperty] private string _policyJson = "{}";

    /// <summary>目录或计划引用来源的 JSON 编辑文本。</summary>
    [ObservableProperty] private string _sourceJson = string.Empty;

    /// <summary>最近一次解析或应用结果。</summary>
    [ObservableProperty] private string? _message;

    /// <summary>叶子节点的有效参数和来源预览。</summary>
    public ObservableCollection<PuloniaTaskEffectiveParameterViewModel> EffectiveParameters { get; } = [];

    /// <summary>当前节点是否有任务参数（叶子）。</summary>
    public bool CanEditParameters => Node.IsLeaf;

    /// <summary>当前节点是否允许编辑资源定位。</summary>
    public bool CanEditPath => Node.IsLeaf && Node.Model.Resource is null;

    /// <summary>当前节点是否存在来源配置。</summary>
    public bool HasSource => Node.Model.Source is not null;

    /// <summary>任务参数编辑框的标题。</summary>
    public string ParameterEditorTitle => "任务参数（JSON 对象，只属于本任务）";

    /// <summary>
    /// 当前节点是否提供共享设置：分组、计划根和引用总是提供；叶子只有能力使用共享设置时提供。
    /// </summary>
    public bool CanEditSharedSettings => Node.UsesSharedSettings;

    /// <summary>共享设置摘要：方案与本节点修改的项数。</summary>
    public string SharedSettingsSummary => Node.SharedSettingsSummary;

    /// <summary>JS 叶子节点提供按项目 settings_ui 生成的脚本自定义设置；分组不对应具体项目。</summary>
    public bool CanEditScriptSettings => Node.IsLeaf && Node.TaskType == "javascript";

    /// <summary>正在读取项目的 settings_ui 定义。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScriptSettingsSummary))]
    private bool _isLoadingScriptSettings;

    /// <summary>脚本自定义设置卡片的状态说明：读取中或当前有效 settings 的字段数量。</summary>
    public string ScriptSettingsSummary
    {
        get
        {
            if (!CanEditScriptSettings) return string.Empty;
            if (IsLoadingScriptSettings) return "正在读取脚本设置…";
            var count = (Document.GetOneDragonParameters(Node)["settings"] as JObject)?.Count ?? 0;
            return count > 0
                ? $"已保存 {count} 项设置，确认后立即生效"
                : "尚未保存设置，脚本按自身默认值运行；确认后立即生效";
        }
    }

    /// <summary>为指定节点建立设置状态，并在文档变化时重新读取。</summary>
    /// <param name="document">节点所属的计划文档。</param>
    /// <param name="node">被编辑的节点。</param>
    /// <param name="dialogs">窗口入口；为空时共享设置、路线 CD 与脚本设置窗口不可用。</param>
    /// <param name="session">编辑会话；为空时共享设置窗口不提供方案。</param>
    public PuloniaTaskNodeAdvancedSettingsViewModel(PuloniaTaskPlanDocumentViewModel document, PuloniaTaskNodeViewModel node,
        IPuloniaTaskSettingsDialogs? dialogs, PuloniaPlanEditingSession? session = null)
    {
        Document = document;
        _node = node;
        _nodeId = node.Id;
        _dialogs = dialogs;
        _session = session;
        Refresh();
        // 只在模型内容或方案变化时重读；自动保存完成等状态通知不能覆盖正在输入的 JSON。
        Document.ContentChanged += OnDocumentChanged;
        Document.ProfilesChanged += OnDocumentChanged;
    }

    /// <summary>从节点当前模型重新读取 JSON、共享设置摘要和有效参数来源。</summary>
    public void Refresh()
    {
        _isRefreshing = true;
        try
        {
            // 撤销会重建树包装，旧包装持有的是撤销前的模型；按 ID 换成当前树中的节点。
            if (!Document.EnumerateNodes().Contains(Node)
                && Document.EnumerateNodes().FirstOrDefault(item => item.Id == _nodeId) is { } current)
                Node = current;
            Message = null;
            ParameterJson = Node.Model.Parameters.ToString(Formatting.Indented);
            SettingsJson = Node.Model.Settings.ToString(Formatting.Indented);
            PolicyJson = JObject.FromObject(Node.Model.Policy).ToString(Formatting.Indented);
            SourceJson = Node.Model.Source is null
                ? string.Empty
                : JObject.FromObject(Node.Model.Source).ToString(Formatting.Indented);

            EffectiveParameters.Clear();
            foreach (var item in Document.GetEffectiveParameterSources(Node))
                EffectiveParameters.Add(item);

            OnPropertyChanged(nameof(CanEditParameters));
            OnPropertyChanged(nameof(CanEditPath));
            OnPropertyChanged(nameof(HasSource));
            OnPropertyChanged(nameof(CanEditSharedSettings));
            OnPropertyChanged(nameof(SharedSettingsSummary));
            OnPropertyChanged(nameof(CanEditScriptSettings));
            OnPropertyChanged(nameof(ScriptSettingsSummary));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>打开共享设置窗口；修改即时生效并进入计划撤销历史。</summary>
    [RelayCommand]
    private void EditSharedSettings()
    {
        if (_dialogs is null || !CanEditSharedSettings)
            return;
        _dialogs.EditSharedSettings(new PuloniaSharedSettingsEditorViewModel(new PuloniaNodeSharedSettingsTarget(Node), _session, _dialogs));
    }

    /// <summary>解析并应用任务参数 JSON；解析或校验失败时保留原模型和输入文本。</summary>
    [RelayCommand]
    private void ApplyParameters()
    {
        try
        {
            Document.ApplyRawParameters(Node, ParameterJson);
            Message = "任务参数已应用，将自动保存。";
        }
        catch (Exception ex) when (ex is JsonException or PuloniaTaskValidationException or InvalidOperationException)
        {
            Message = "参数未应用：" + ex.Message;
            ThemedMessageBox.Error(Message, "参数格式错误");
        }
    }

    /// <summary>解析并应用共享设置 JSON；设置项不存在或值无效时不修改模型。</summary>
    [RelayCommand]
    private void ApplySettings()
    {
        try
        {
            Document.ApplyRawSettings(Node, SettingsJson);
            Message = "共享设置已应用，将自动保存。";
        }
        catch (Exception ex) when (ex is JsonException or PuloniaTaskValidationException or InvalidOperationException)
        {
            Message = "共享设置未应用：" + ex.Message;
            ThemedMessageBox.Error(Message, "共享设置格式错误");
        }
    }

    /// <summary>解析并应用执行策略 JSON；失败时不修改原策略。</summary>
    [RelayCommand]
    private void ApplyPolicy()
    {
        try
        {
            Document.ApplyRawPolicy(Node, PolicyJson);
            Message = "执行策略已应用，将自动保存。";
        }
        catch (Exception ex) when (ex is JsonException or PuloniaTaskValidationException or InvalidOperationException)
        {
            Message = "执行策略未应用：" + ex.Message;
            ThemedMessageBox.Error(Message, "执行策略格式错误");
        }
    }

    /// <summary>解析并应用计划或目录引用来源；空文本会清除来源。</summary>
    [RelayCommand]
    private void ApplySource()
    {
        try
        {
            Document.ApplyRawSource(Node, SourceJson);
            Message = "引用来源已应用，将自动保存。";
        }
        catch (Exception ex) when (ex is JsonException or PuloniaTaskValidationException or InvalidOperationException)
        {
            Message = "引用来源未应用：" + ex.Message;
            ThemedMessageBox.Error(Message, "引用来源格式错误");
        }
    }

    /// <summary>移除本任务的任务参数，使能力默认值重新生效；共享设置不受影响。</summary>
    [RelayCommand]
    private void RestoreParameterInheritance()
    {
        try { Document.RestoreParameterInheritance(Node); }
        catch (InvalidOperationException ex) { Message = ex.Message; }
    }

    /// <summary>
    /// 按节点固定的项目读取 settings_ui，在独立窗口中编辑脚本自定义设置；确认后作为一次可撤销编辑立即生效，取消不写入。
    /// 本地项目按已保存的路径读取，基本区中尚未保存的路径修改不参与。
    /// </summary>
    [RelayCommand]
    private async Task EditScriptSettingsAsync()
    {
        if (_dialogs is null || !CanEditScriptSettings) return;
        var node = Node;
        IReadOnlyList<SettingItem> items;
        string? error;
        IsLoadingScriptSettings = true;
        try
        {
            // 1. 只需要设置定义：读取后立即释放仓库预览目录
            using var preview = await PuloniaTaskResourcePreviewLoader.LoadJavaScriptTaskAsync(node.Model,
                Document.GetResourceBaseDirectory(node));
            items = preview.JavaScriptSettingItems;
            error = preview.JavaScriptSettingsError;
        }
        catch (Exception ex)
        {
            // 清单缺失、仓库版本不可用或路径无效时只提示，不修改任务
            items = [];
            error = "无法读取脚本设置：" + ex.Message;
        }
        finally
        {
            IsLoadingScriptSettings = false;
        }
        if (error is not null)
        {
            ThemedMessageBox.Error(error, "脚本自定义设置");
            return;
        }
        if (items.Count == 0)
        {
            ThemedMessageBox.Information("此脚本未提供自定义设置。", "脚本自定义设置");
            return;
        }

        // 2. 以当前有效值为初值编辑草稿，确认后整体替换本任务的 settings
        try
        {
            var current = Document.GetOneDragonParameters(node)["settings"] as JObject;
            var viewModel = new PuloniaJsScriptSettingsDialogViewModel(node.Name, items, current, isCreation: false);
            if (_dialogs.EditJavaScriptSettings(viewModel) is not { } settings) return;
            Message = Document.ApplyJavaScriptSettings(node, settings) ? "脚本设置已应用，将自动保存。" : "脚本设置没有变化。";
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or InvalidOperationException)
        {
            ThemedMessageBox.Error("脚本设置未应用：" + ex.Message, "脚本自定义设置");
        }
    }

    /// <summary>文档内容或方案变化后重新读取，显示的始终是最新模型。</summary>
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (!_isRefreshing)
            Refresh();
    }

    /// <summary>窗口关闭时解除文档事件订阅。</summary>
    public void Dispose()
    {
        Document.ContentChanged -= OnDocumentChanged;
        Document.ProfilesChanged -= OnDocumentChanged;
    }
}
