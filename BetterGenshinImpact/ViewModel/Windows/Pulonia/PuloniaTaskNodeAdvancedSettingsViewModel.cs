using System;
using System.Collections.ObjectModel;
using System.Linq;
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
/// 高级设置窗口打开期间按需计算的节点编辑状态：原始参数、策略、来源 JSON，共享预设，有效参数来源，
/// 以及公共设置与采集 CD 表单入口。切换计划树选中节点不会刷新它；文档内容变化后重新读取。
/// </summary>
public partial class PuloniaTaskNodeAdvancedSettingsViewModel : ObservableObject, IDisposable
{
    /// <summary>公共设置与采集 CD 表单入口；为空时这两类表单不可用（例如测试）。</summary>
    private readonly IPuloniaTaskSettingsDialogs? _dialogs;

    /// <summary>重新读取编辑器状态期间，阻止预设下拉框回写模型。</summary>
    private bool _isRefreshing;

    /// <summary>节点所属的计划文档。</summary>
    public PuloniaTaskPlanDocumentViewModel Document { get; }

    /// <summary>被编辑节点的稳定 ID；撤销等操作重建树包装后据此找回同一节点。</summary>
    private readonly string _nodeId;

    /// <summary>被编辑的节点；树包装被重建时切换为同一 ID 的新包装。</summary>
    [ObservableProperty] private PuloniaTaskNodeViewModel _node;

    /// <summary>参数或分组公共覆盖的 JSON 编辑文本。</summary>
    [ObservableProperty] private string _parameterJson = "{}";

    /// <summary>执行策略的 JSON 编辑文本。</summary>
    [ObservableProperty] private string _policyJson = "{}";

    /// <summary>目录或计划引用来源的 JSON 编辑文本。</summary>
    [ObservableProperty] private string _sourceJson = string.Empty;

    /// <summary>当前选择的共享预设。</summary>
    [ObservableProperty] private PuloniaTaskPresetOptionViewModel? _selectedPresetOption;

    /// <summary>最近一次解析、应用结果或预设提示。</summary>
    [ObservableProperty] private string? _message;

    /// <summary>当前节点可选择的共享预设。</summary>
    public ObservableCollection<PuloniaTaskPresetOptionViewModel> PresetOptions { get; } = [];

    /// <summary>叶子节点的有效参数和来源预览。</summary>
    public ObservableCollection<PuloniaTaskEffectiveParameterViewModel> EffectiveParameters { get; } = [];

    /// <summary>当前节点是否允许选择预设。</summary>
    public bool CanSelectPreset => Node.IsLeaf;

    /// <summary>当前节点是否允许编辑资源定位。</summary>
    public bool CanEditPath => Node.IsLeaf && Node.Model.Resource is null;

    /// <summary>当前节点是否存在来源配置。</summary>
    public bool HasSource => Node.Model.Source is not null;

    /// <summary>参数编辑框的内容说明。</summary>
    public string ParameterEditorTitle => Node.TaskType == "group"
        ? "分组公共参数覆盖（JSON 数组）"
        : "节点参数（JSON 对象）";

    /// <summary>当前节点是否能够配置地图追踪公共设置。</summary>
    public bool CanEditPathingSettings => Node.TaskType is "group" or "pathing";

    /// <summary>当前节点是否能够配置 JS 使用的行走、战斗和食物设置。</summary>
    public bool CanEditJavaScriptSettings => Node.TaskType is "group" or "javascript";

    /// <summary>当前节点是否能够配置 Shell 公共设置。</summary>
    public bool CanEditShellSettings => Node.TaskType is "group" or "shell";

    /// <summary>是否展示原配置组设置的可视化编辑入口。</summary>
    public bool CanEditCommonSettings => CanEditPathingSettings || CanEditJavaScriptSettings || CanEditShellSettings;

    /// <summary>地图追踪叶子或分组提供采集 CD 设置。</summary>
    public bool CanEditCollectionCooldown => Node.TaskType is "pathing" or "group";

    /// <summary>当前材料、规则和配置来源的简要预览。</summary>
    public string CollectionCooldownSummary
    {
        get
        {
            if (!CanEditCollectionCooldown) return string.Empty;
            try
            {
                var draft = Document.CreateCollectionCooldownDraft(Node);
                return draft.Preview + "\n配置来源：" + draft.ConfigurationSource;
            }
            catch (Exception ex) when (ex is PuloniaTaskValidationException or ArgumentException) { return "配置无效：" + ex.Message; }
        }
    }

    /// <summary>为指定节点建立高级设置状态，并在文档变化时重新读取。</summary>
    /// <param name="document">节点所属的计划文档。</param>
    /// <param name="node">被编辑的节点。</param>
    /// <param name="dialogs">公共设置与采集 CD 表单入口；为空时这两类表单不可用。</param>
    public PuloniaTaskNodeAdvancedSettingsViewModel(PuloniaTaskPlanDocumentViewModel document, PuloniaTaskNodeViewModel node,
        IPuloniaTaskSettingsDialogs? dialogs)
    {
        Document = document;
        _node = node;
        _nodeId = node.Id;
        _dialogs = dialogs;
        Refresh();
        // 只在模型内容或预设变化时重读；自动保存完成等状态通知不能覆盖正在输入的 JSON。
        Document.ContentChanged += OnDocumentChanged;
        Document.PresetsChanged += OnDocumentChanged;
    }

    /// <summary>从节点当前模型重新读取 JSON、预设选项和有效参数来源。</summary>
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
            ParameterJson = Node.TaskType == "group"
                ? JArray.FromObject(Node.Model.ParameterOverrides).ToString(Formatting.Indented)
                : Node.Model.Parameters.ToString(Formatting.Indented);
            PolicyJson = JObject.FromObject(Node.Model.Policy).ToString(Formatting.Indented);
            SourceJson = Node.Model.Source is null
                ? string.Empty
                : JObject.FromObject(Node.Model.Source).ToString(Formatting.Indented);

            PresetOptions.Clear();
            var options = Document.GetPresetOptions(Node, out var warning);
            foreach (var option in options)
                PresetOptions.Add(option);
            Message = warning;
            SelectedPresetOption = PresetOptions.FirstOrDefault(option => option.Id == Node.Model.PresetId)
                                   ?? PresetOptions[0];

            EffectiveParameters.Clear();
            foreach (var item in Document.GetEffectiveParameterSources(Node))
                EffectiveParameters.Add(item);

            OnPropertyChanged(nameof(CanSelectPreset));
            OnPropertyChanged(nameof(CanEditPath));
            OnPropertyChanged(nameof(HasSource));
            OnPropertyChanged(nameof(ParameterEditorTitle));
            OnPropertyChanged(nameof(CanEditPathingSettings));
            OnPropertyChanged(nameof(CanEditJavaScriptSettings));
            OnPropertyChanged(nameof(CanEditShellSettings));
            OnPropertyChanged(nameof(CanEditCommonSettings));
            OnPropertyChanged(nameof(CanEditCollectionCooldown));
            OnPropertyChanged(nameof(CollectionCooldownSummary));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>解析并应用参数 JSON；解析或校验失败时保留原模型和输入文本。</summary>
    [RelayCommand]
    private void ApplyParameters()
    {
        try
        {
            Document.ApplyRawParameters(Node, ParameterJson);
            Message = "参数已应用，将自动保存。";
        }
        catch (Exception ex) when (ex is JsonException or PuloniaTaskValidationException or InvalidOperationException)
        {
            Message = "参数未应用：" + ex.Message;
            ThemedMessageBox.Error(Message, "参数格式错误");
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

    /// <summary>移除本任务的显式参数，使能力默认值、预设及父组配置重新生效。</summary>
    [RelayCommand]
    private void RestoreParameterInheritance()
    {
        try { Document.RestoreParameterInheritance(Node); }
        catch (InvalidOperationException ex) { Message = ex.Message; }
    }

    /// <summary>
    /// 在独立副本上编辑类型公共设置，确认时只提交变化字段；取消不会污染计划或全局配置。
    /// </summary>
    /// <param name="taskType">pathing、javascript 或 shell。</param>
    [RelayCommand]
    private void EditCommonSettings(string taskType)
    {
        if (_dialogs is null || taskType is not ("pathing" or "javascript" or "shell")
            || Node.TaskType != "group" && Node.TaskType != taskType)
            return;
        try
        {
            var effective = Document.GetCommonSettingsParameters(Node, taskType);
            var original = PuloniaTaskCommonSettings.ToParameters(taskType, PuloniaTaskCommonSettings.FromParameters(taskType, effective));
            if (_dialogs.EditCommonSettings(taskType, Node.TaskType == "group", effective) is not { } result)
                return;
            Document.ApplyCommonSettings(Node, taskType, original, result.Value ?? original, result.RestoreInheritance);
            Message = result.RestoreInheritance ? "已恢复继承的任务设置。" : "任务设置已应用，将自动保存。";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or JsonException
                                   or ArgumentException or InvalidOperationException or OverflowException)
        {
            ThemedMessageBox.Error("任务设置未应用：" + ex.Message, "任务设置");
        }
    }

    /// <summary>在模态草稿表单中编辑采集 CD；取消不写入，确认和恢复继承均作为一次文档变更。</summary>
    [RelayCommand]
    private void EditCollectionCooldown()
    {
        if (_dialogs is null || !CanEditCollectionCooldown) return;
        try
        {
            var draft = Document.CreateCollectionCooldownDraft(Node);
            var original = draft.BuildValue();
            if (_dialogs.EditCollectionCooldown(draft, Node.IsLeaf) is not { } result) return;
            Document.ApplyCollectionCooldown(Node, original, result.RestoreInheritance ? null : draft.BuildValue(), result.RestoreInheritance);
            Message = result.RestoreInheritance ? "已恢复继承的采集 CD。" : "采集 CD 已应用，将自动保存。";
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or FormatException or ArgumentException or OverflowException)
        {
            ThemedMessageBox.Error("采集 CD 设置未应用：" + ex.Message, "采集 CD");
        }
    }

    /// <summary>预设下拉框变化时立即更新模型；读取状态期间的赋值不回写。</summary>
    partial void OnSelectedPresetOptionChanged(PuloniaTaskPresetOptionViewModel? value)
    {
        if (_isRefreshing)
            return;
        Document.SelectPreset(Node, value?.Id);
    }

    /// <summary>文档内容或预设变化后重新读取，显示的始终是最新模型。</summary>
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (!_isRefreshing)
            Refresh();
    }

    /// <summary>窗口关闭时解除文档事件订阅。</summary>
    public void Dispose()
    {
        Document.ContentChanged -= OnDocumentChanged;
        Document.PresetsChanged -= OnDocumentChanged;
    }
}
