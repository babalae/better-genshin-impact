using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// JS 脚本自定义设置弹窗视图模型：按项目 manifest.settings_ui 生成表单，确认后返回完整的 settings 对象。
/// 新增任务和编辑配置共用；弹窗只编辑草稿，由调用方决定写入新任务还是提交到计划文档。
/// </summary>
public partial class PuloniaJsScriptSettingsDialogViewModel : ObservableObject
{
    /// <summary>打开弹窗时任务中已保存的 settings 副本；确认时在其上覆盖表单字段，保留 settings_ui 未声明的键。</summary>
    private readonly JObject _original;

    /// <summary>是否为新增任务流程；决定标题、说明和确认按钮文本。</summary>
    private readonly bool _isCreation;

    /// <summary>脚本或任务名称。</summary>
    public string ScriptName { get; }

    /// <summary>弹窗标题。</summary>
    public string WindowTitle => _isCreation ? $"添加 JS 脚本任务：{ScriptName}" : $"脚本自定义设置：{ScriptName}";

    /// <summary>表单说明。</summary>
    public string Description => _isCreation
        ? "以下设置由脚本声明，仅对这个任务生效；添加后仍可在“编辑配置”中修改。"
        : "以下设置由脚本声明，仅对这个任务生效；确认后立即保存。";

    /// <summary>确认按钮文本。</summary>
    public string ConfirmText => _isCreation ? "添加任务" : "确定";

    /// <summary>settings_ui 声明的设置字段。</summary>
    public ObservableCollection<PuloniaJsScriptSettingFieldViewModel> Fields { get; } = [];

    /// <summary>确认后的完整 settings 对象；取消时为空。</summary>
    public JObject? Result { get; private set; }

    /// <summary>请求宿主窗口按确认（true）或取消（false）关闭。</summary>
    public event EventHandler<bool>? RequestClose;

    /// <summary>
    /// 建立脚本自定义设置弹窗视图模型。
    /// </summary>
    /// <param name="scriptName">脚本或任务名称。</param>
    /// <param name="items">项目 settings_ui 声明的设置项。</param>
    /// <param name="currentSettings">任务当前的 settings；为空时全部使用声明的默认值。</param>
    /// <param name="isCreation">是否为新增任务流程。</param>
    public PuloniaJsScriptSettingsDialogViewModel(string scriptName, IReadOnlyList<SettingItem> items,
        JObject? currentSettings, bool isCreation)
    {
        ScriptName = scriptName;
        _isCreation = isCreation;
        _original = currentSettings is null ? new JObject() : (JObject)currentSettings.DeepClone();
        foreach (var item in items)
            Fields.Add(new PuloniaJsScriptSettingFieldViewModel(item,
                string.IsNullOrWhiteSpace(item.Name) ? null : _original[item.Name]));
    }

    /// <summary>
    /// 汇总表单为 settings 对象并关闭弹窗。
    /// </summary>
    [RelayCommand]
    private void Confirm()
    {
        Result = BuildSettings();
        RequestClose?.Invoke(this, true);
    }

    /// <summary>取消编辑，不返回任何设置。</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke(this, false);
    }

    /// <summary>
    /// 在原 settings 副本上覆盖表单字段：有值写入，无值移除；分隔项和不支持的类型保留原值。
    /// </summary>
    private JObject BuildSettings()
    {
        var settings = (JObject)_original.DeepClone();
        foreach (var field in Fields)
        {
            // 新增流程已拒绝含不支持类型的脚本；编辑时脚本可能已更新，不支持的字段不阻止其他字段保存。
            if (field.IsSeparator || !field.IsSupported || string.IsNullOrWhiteSpace(field.Name))
                continue;
            if (field.BuildValue() is { } value)
                settings[field.Name] = value;
            else
                settings.Remove(field.Name);
        }
        return settings;
    }
}
