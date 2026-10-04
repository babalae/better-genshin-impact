using System;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>新一条龙公共设置的独立草稿；确认后交由原编辑会话保存变化字段。</summary>
public partial class PuloniaOneDragonCommonSettingsViewModel : ObservableObject
{
    /// <summary>公共参数的类型约束，不包含具体命令或脚本 settings。</summary>
    private readonly JObject _schema;
    /// <summary>窗口标题。</summary>
    public string Title { get; }
    /// <summary>从有效值生成的公共参数字段。</summary>
    public ObservableCollection<PuloniaTaskParameterFieldViewModel> Fields { get; } = [];
    /// <summary>确认时的参数副本；取消时保持空。</summary>
    public JObject? Result { get; private set; }
    /// <summary>是否请求移除当前作用域的公共覆盖。</summary>
    public bool RestoreInheritance { get; private set; }
    /// <summary>表单校验提示，失败时保留当前编辑。</summary>
    [ObservableProperty] private string? _statusMessage;
    /// <summary>请求视图以指定结果关闭。</summary>
    public event EventHandler<bool>? RequestClose;

    /// <summary>仅复制指定任务类型的公共参数，打开窗口不会改写计划。</summary>
    public PuloniaOneDragonCommonSettingsViewModel(string taskType, JObject effective)
    {
        Title = taskType switch { "pathing" => "地图追踪公共设置", "javascript" => "JS 宿主公共设置", _ => "Shell 公共设置" };
        var properties = PuloniaTaskCommonSettings.CreateSchemaProperties(taskType);
        _schema = new JObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        var values = PuloniaTaskCommonSettings.ToParameters(taskType, PuloniaTaskCommonSettings.FromParameters(taskType, effective));
        foreach (var property in properties.Properties())
            Fields.Add(new PuloniaTaskParameterFieldViewModel(property.Name, (JObject)property.Value, values[property.Name],
                hasDefaultValue: values.ContainsKey(property.Name), isRequired: false));
    }

    /// <summary>完整校验所有字段，成功后才交付可应用的独立参数。</summary>
    [RelayCommand]
    private void Confirm()
    {
        try
        {
            var values = new JObject();
            foreach (var field in Fields) values[field.Name] = field.BuildValue();
            PuloniaTaskValidator.ValidateParameters(values, _schema, Title);
            Result = values;
            RequestClose?.Invoke(this, true);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or PuloniaTaskValidationException)
        { StatusMessage = "配置未应用：" + ex.Message; }
    }

    /// <summary>请求原编辑器恢复继承，不把本窗口显示的有效值写为覆盖。</summary>
    [RelayCommand]
    private void Restore()
    {
        RestoreInheritance = true;
        RequestClose?.Invoke(this, true);
    }

    /// <summary>取消独立草稿，原计划保持不变。</summary>
    [RelayCommand] private void Cancel() => RequestClose?.Invoke(this, false);
}
