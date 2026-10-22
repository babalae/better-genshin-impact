using System;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 新增独立任务弹窗视图模型：填写名称并通过任务设置编辑器配置参数，确认后生成待插入的任务节点。
/// 确认前不修改任务计划。
/// </summary>
public partial class PuloniaSoloTaskCreationDialogViewModel : ObservableObject
{
    /// <summary>要新增的独立任务能力定义。</summary>
    private readonly PuloniaTaskDefinition _definition;

    /// <summary>弹窗标题。</summary>
    public string WindowTitle => $"添加任务：{DisplayName}";

    /// <summary>任务显示名称。</summary>
    public string DisplayName => _definition.DisplayName;

    /// <summary>任务说明。</summary>
    public string Description => _definition.Description;

    /// <summary>是否允许自定义节点名称；一条龙中的独立任务保持内置名称。</summary>
    public bool CanEditName { get; }

    /// <summary>任务设置编辑器：优先使用独立任务的专属设置界面。</summary>
    public PuloniaTaskSettingsEditorViewModel Settings { get; }

    /// <summary>节点名称草稿。</summary>
    [ObservableProperty]
    private string _taskName;

    /// <summary>校验失败等提示信息。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>确认后生成的任务节点；取消时为空。</summary>
    public PuloniaTask? Result { get; private set; }

    /// <summary>请求宿主窗口按确认（true）或取消（false）关闭。</summary>
    public event EventHandler<bool>? RequestClose;

    /// <summary>
    /// 建立新增独立任务弹窗视图模型。
    /// </summary>
    /// <param name="definition">独立任务能力定义。</param>
    /// <param name="registry">独立任务注册表，用于查找专属设置界面。</param>
    /// <param name="inheritedParameters">插入位置的有效参数（默认值叠加分组覆盖）。</param>
    /// <param name="canEditName">是否允许修改节点名称。</param>
    public PuloniaSoloTaskCreationDialogViewModel(PuloniaTaskDefinition definition, SoloTaskRegistry? registry,
        JObject inheritedParameters, bool canEditName = true)
    {
        _definition = definition;
        _taskName = definition.DisplayName;
        CanEditName = canEditName;
        Settings = PuloniaTaskSettingsEditorViewModel.Create(registry, definition, inheritedParameters);
    }

    /// <summary>
    /// 校验名称与参数，生成只保存变化字段的任务节点并关闭弹窗。
    /// </summary>
    [RelayCommand]
    private void Confirm()
    {
        var name = CanEditName ? TaskName.Trim() : DisplayName;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "任务名称不能为空。";
            return;
        }
        try
        {
            // 只持久化相对继承值发生变化的字段，其余字段继续跟随默认值和分组设置
            Result = new PuloniaTask
            {
                Name = name,
                TaskType = _definition.TaskType,
                Parameters = Settings.CollectValidatedChanges()
            };
            RequestClose?.Invoke(this, true);
        }
        catch (Exception ex) when (ex is FormatException or Newtonsoft.Json.JsonException or PuloniaTaskValidationException)
        {
            StatusMessage = ex.Message;
        }
    }

    /// <summary>取消新增，不产生任何任务树变更。</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke(this, false);
    }
}
