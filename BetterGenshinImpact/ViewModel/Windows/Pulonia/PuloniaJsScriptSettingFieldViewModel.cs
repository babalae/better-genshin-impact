using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 将现有 JS 脚本 settings_ui 配置项转换为可绑定的新增任务表单字段。
/// </summary>
public partial class PuloniaJsScriptSettingFieldViewModel : ObservableObject
{
    /// <summary>
    /// 脚本读取的设置键名。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 面向用户展示的字段名称。
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// settings_ui 中声明的控件类型。
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// 单选字段的候选值。
    /// </summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>
    /// 级联选择字段的两级候选值。
    /// </summary>
    public Dictionary<string, List<string>>? CascadeOptions { get; }

    /// <summary>
    /// 多选字段的候选项。
    /// </summary>
    public ObservableCollection<PuloniaJsScriptSettingOptionViewModel> MultiOptions { get; } = [];

    /// <summary>
    /// 当前字段是否仅用于分隔内容。
    /// </summary>
    public bool IsSeparator => Type == "separator";

    /// <summary>
    /// 当前字段是否使用文本输入框。
    /// </summary>
    public bool IsTextInput => Type == "input-text";

    /// <summary>
    /// 当前字段是否使用单选下拉框。
    /// </summary>
    public bool IsSelect => Type == "select";

    /// <summary>
    /// 当前字段是否使用布尔开关。
    /// </summary>
    public bool IsCheckbox => Type == "checkbox";

    /// <summary>
    /// 当前字段是否使用多选列表。
    /// </summary>
    public bool IsMultiCheckbox => Type == "multi-checkbox";

    /// <summary>
    /// 当前字段是否使用项目现有的级联选择控件。
    /// </summary>
    public bool IsCascadeSelect => Type == "cascade-select";

    /// <summary>
    /// 当前字段是否是现有脚本设置协议支持的类型。
    /// </summary>
    public bool IsSupported => IsSeparator || IsTextInput || IsSelect || IsCheckbox || IsMultiCheckbox
                               || IsCascadeSelect;

    /// <summary>
    /// 文本输入值。
    /// </summary>
    [ObservableProperty]
    private string _textValue = string.Empty;

    /// <summary>
    /// 单选或级联选择值。
    /// </summary>
    [ObservableProperty]
    private string? _selectedValue;

    /// <summary>
    /// 布尔开关值。
    /// </summary>
    [ObservableProperty]
    private bool _booleanValue;

    /// <summary>
    /// 字段是否已经具有默认值或用户输入值；无值字段不会被无故写入 settings。
    /// </summary>
    private bool _hasValue;

    /// <summary>
    /// 初始化默认值时避免将控件初值误判为用户输入。
    /// </summary>
    private bool _isInitializing = true;

    /// <summary>
    /// 从项目既有的 SettingItem 模型建立一个可编辑字段。
    /// </summary>
    public PuloniaJsScriptSettingFieldViewModel(SettingItem item)
    {
        Name = item.Name;
        Label = item.Label;
        Type = item.Type;
        Options = item.Options ?? [];
        CascadeOptions = item.CascadeOptions;

        var defaultValue = item.Default;
        _hasValue = defaultValue is not null || IsMultiCheckbox;
        if (IsTextInput)
            _textValue = ConvertDefaultToString(defaultValue);
        else if (IsSelect || IsCascadeSelect)
            _selectedValue = defaultValue is null ? null : ConvertDefaultToString(defaultValue);
        else if (IsCheckbox)
            _booleanValue = bool.TryParse(ConvertDefaultToString(defaultValue), out var value) && value;
        else if (IsMultiCheckbox)
        {
            var selectedValues = ReadStringList(defaultValue);
            foreach (var option in Options)
                MultiOptions.Add(new PuloniaJsScriptSettingOptionViewModel(option, selectedValues.Contains(option)));
        }
        _isInitializing = false;
    }

    /// <summary>
    /// 文本被编辑后记录该设置需要写入结果。
    /// </summary>
    partial void OnTextValueChanged(string value)
    {
        if (!_isInitializing)
            _hasValue = true;
    }

    /// <summary>
    /// 选择项变化后记录该设置需要写入结果。
    /// </summary>
    partial void OnSelectedValueChanged(string? value)
    {
        if (!_isInitializing)
            _hasValue = value is not null;
    }

    /// <summary>
    /// 开关变化后记录该设置需要写入结果。
    /// </summary>
    partial void OnBooleanValueChanged(bool value)
    {
        if (!_isInitializing)
            _hasValue = true;
    }

    /// <summary>
    /// 将当前表单值转换为脚本运行时 settings 中的 JSON 值；无值或分隔项返回空。
    /// </summary>
    public JToken? BuildValue()
    {
        if (!IsSupported || IsSeparator || string.IsNullOrWhiteSpace(Name))
            return null;
        if (IsMultiCheckbox)
            return new JArray(MultiOptions.Where(option => option.IsSelected).Select(option => option.Value));
        if (!_hasValue)
            return null;
        if (IsCheckbox)
            return new JValue(BooleanValue);
        if (IsSelect || IsCascadeSelect)
            return SelectedValue is null ? null : new JValue(SelectedValue);
        return new JValue(TextValue);
    }

    /// <summary>
    /// 将 System.Text.Json 反序列化产生的默认值稳定转换为字符串。
    /// </summary>
    private static string ConvertDefaultToString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonElement element => element.ToString(),
            _ => Convert.ToString(value) ?? string.Empty
        };
    }

    /// <summary>
    /// 读取多选字段的字符串数组默认值。
    /// </summary>
    private static HashSet<string> ReadStringList(object? value)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => item is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
        }
        if (value is IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>()
                .Select(ConvertDefaultToString)
                .ToHashSet(StringComparer.Ordinal);
        }
        return [];
    }
}
