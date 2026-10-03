using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 参数字段使用的编辑器类型。
/// </summary>
public enum PuloniaTaskParameterEditorKind
{
    /// <summary>
    /// 单行文本编辑器。
    /// </summary>
    Text,

    /// <summary>
    /// 布尔开关编辑器。
    /// </summary>
    Boolean,

    /// <summary>
    /// 固定选项编辑器。
    /// </summary>
    Choice,

    /// <summary>
    /// JSON 数组或对象编辑器。
    /// </summary>
    Json
}

/// <summary>
/// 把 Pulonia 参数 Schema 的一个属性转换为可绑定、可校验的编辑字段。
/// </summary>
public partial class PuloniaTaskParameterFieldViewModel : ObservableObject
{
    /// <summary>
    /// Schema 中的参数名。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 面向用户展示的参数名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 面向用户展示的参数说明。
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 当前字段使用的编辑器类型。
    /// </summary>
    public PuloniaTaskParameterEditorKind EditorKind { get; }

    /// <summary>
    /// 固定选项列表。
    /// </summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>
    /// 字段是否为必填项。
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>
    /// 展示名称及必填标记。
    /// </summary>
    public string DisplayLabel => IsRequired ? DisplayName + " *" : DisplayName;

    /// <summary>
    /// 是否显示普通文本编辑器。
    /// </summary>
    public bool IsTextEditor => EditorKind == PuloniaTaskParameterEditorKind.Text;

    /// <summary>
    /// 是否显示布尔开关编辑器。
    /// </summary>
    public bool IsBooleanEditor => EditorKind == PuloniaTaskParameterEditorKind.Boolean;

    /// <summary>
    /// 是否显示固定选项编辑器。
    /// </summary>
    public bool IsChoiceEditor => EditorKind == PuloniaTaskParameterEditorKind.Choice;

    /// <summary>
    /// 是否显示多行 JSON 编辑器。
    /// </summary>
    public bool IsJsonEditor => EditorKind == PuloniaTaskParameterEditorKind.Json;

    /// <summary>
    /// 字段允许的主要 JSON 类型。
    /// </summary>
    private readonly string _valueType;

    /// <summary>
    /// 字段是否允许显式 null。
    /// </summary>
    private readonly bool _isNullable;

    /// <summary>
    /// 能力定义是否提供了该字段的默认值。
    /// </summary>
    private readonly bool _hasDefaultValue;

    /// <summary>
    /// 能力定义中的字段默认值，用于只保存真实覆盖。
    /// </summary>
    private readonly JToken? _defaultValue;

    /// <summary>
    /// 文本、数值或 JSON 编辑内容。
    /// </summary>
    [ObservableProperty]
    private string _textValue = string.Empty;

    /// <summary>
    /// 布尔编辑内容。
    /// </summary>
    [ObservableProperty]
    private bool _booleanValue;

    /// <summary>
    /// 固定选项编辑内容。
    /// </summary>
    [ObservableProperty]
    private string? _selectedChoice;

    /// <summary>
    /// 根据字段 Schema 和能力默认值建立编辑字段。
    /// </summary>
    public PuloniaTaskParameterFieldViewModel(string name, JObject schema, JToken? defaultValue,
        bool hasDefaultValue, bool isRequired)
    {
        Name = name;
        DisplayName = GetDisplayName(name);
        Description = GetDescription(name);
        IsRequired = isRequired;
        _hasDefaultValue = hasDefaultValue;
        _defaultValue = defaultValue?.DeepClone();

        var types = schema["type"] is JArray typeArray
            ? typeArray.Values<string>().Where(type => type is not null).Cast<string>().ToList()
            : [schema.Value<string>("type") ?? "string"];
        _isNullable = types.Contains("null", StringComparer.Ordinal);
        _valueType = types.FirstOrDefault(type => type != "null") ?? "string";
        Choices = schema["enum"] is JArray choices
            ? choices.Values<string>().Where(choice => choice is not null).Cast<string>().ToList()
            : [];
        EditorKind = Choices.Count > 0
            ? PuloniaTaskParameterEditorKind.Choice
            : _valueType switch
            {
                "boolean" => PuloniaTaskParameterEditorKind.Boolean,
                "array" or "object" => PuloniaTaskParameterEditorKind.Json,
                _ => PuloniaTaskParameterEditorKind.Text
            };

        var initialValue = defaultValue ?? JValue.CreateNull();
        if (EditorKind == PuloniaTaskParameterEditorKind.Boolean)
            BooleanValue = initialValue.Value<bool?>() ?? false;
        else if (EditorKind == PuloniaTaskParameterEditorKind.Choice)
            SelectedChoice = initialValue.Type == JTokenType.Null
                ? Choices.FirstOrDefault()
                : initialValue.Value<string>();
        else if (EditorKind == PuloniaTaskParameterEditorKind.Json)
            TextValue = initialValue.Type == JTokenType.Null
                ? (_valueType == "array" ? "[]" : "{}")
                : initialValue.ToString(Formatting.Indented);
        else
            TextValue = FormatScalar(initialValue);
    }

    /// <summary>
    /// 把当前编辑内容转换为保留 JSON 类型的参数值。
    /// </summary>
    public JToken BuildValue()
    {
        if (EditorKind == PuloniaTaskParameterEditorKind.Boolean)
            return new JValue(BooleanValue);
        if (EditorKind == PuloniaTaskParameterEditorKind.Choice)
        {
            if (SelectedChoice is null)
                throw new FormatException($"请选择“{DisplayName}”。");
            return new JValue(SelectedChoice);
        }
        if (EditorKind == PuloniaTaskParameterEditorKind.Json)
        {
            JToken value;
            try
            {
                value = JToken.Parse(TextValue);
            }
            catch (JsonReaderException ex)
            {
                throw new FormatException($"“{DisplayName}”不是有效 JSON：{ex.Message}", ex);
            }

            if (_valueType == "array" && value is not JArray || _valueType == "object" && value is not JObject)
                throw new FormatException($"“{DisplayName}”必须是 JSON {_valueType}。");
            return value;
        }

        if (_isNullable && string.IsNullOrWhiteSpace(TextValue))
            return JValue.CreateNull();
        if (_valueType == "integer")
        {
            if (!long.TryParse(TextValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                throw new FormatException($"“{DisplayName}”必须是整数。");
            return new JValue(integer);
        }
        if (_valueType == "number")
        {
            if (!decimal.TryParse(TextValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw new FormatException($"“{DisplayName}”必须是数字。");
            return new JValue(number);
        }
        if (IsRequired && string.IsNullOrWhiteSpace(TextValue))
            throw new FormatException($"“{DisplayName}”不能为空。");
        return new JValue(TextValue);
    }

    /// <summary>
    /// 判断当前值是否需要作为节点显式覆盖保存。
    /// </summary>
    public bool ShouldPersist(JToken value)
    {
        if (_hasDefaultValue)
            return !JToken.DeepEquals(_defaultValue, value);
        return IsRequired || value.Type != JTokenType.Null;
    }

    /// <summary>
    /// 把标量默认值格式化为不受当前区域设置影响的文本。
    /// </summary>
    private static string FormatScalar(JToken value)
    {
        if (value.Type == JTokenType.Null)
            return string.Empty;
        if (value.Type is JTokenType.Integer or JTokenType.Float)
            return Convert.ToString(((JValue)value).Value, CultureInfo.InvariantCulture) ?? string.Empty;
        return value.Value<string>() ?? string.Empty;
    }

    /// <summary>
    /// 返回当前已接入参数的中文名称；未知字段仍保留稳定字段名。
    /// </summary>
    private static string GetDisplayName(string name) => name switch
    {
        "settings" => "脚本设置",
        "party_name" => "队伍名称",
        "skip_party_switch" => "跳过队伍切换",
        "auto_pick_enabled" => "自动拾取",
        "auto_fight_enabled" => "自动战斗",
        "auto_skip_enabled" => "自动跳过剧情",
        "with_delay" => "按录制延时回放",
        "file_name" => "程序或命令",
        "arguments" => "参数数组",
        "working_directory" => "工作目录",
        "operation" => "C# 操作名",
        "values" => "数值数组",
        "delay_milliseconds" => "延时（毫秒）",
        "country" => "国家",
        _ => name
    };

    /// <summary>
    /// 返回当前已接入参数的简短说明。
    /// </summary>
    private static string GetDescription(string name) => name switch
    {
        "settings" => "传给 JS 项目的 settings JSON 对象。",
        "party_name" => "留空表示不指定固定队伍。",
        "skip_party_switch" => "开启后不执行自动切换队伍。",
        "auto_pick_enabled" => "运行期间启用自动拾取触发器。",
        "auto_fight_enabled" => "路线遇敌时允许自动战斗。",
        "auto_skip_enabled" => "路线过程中允许自动跳过对话。",
        "with_delay" => "按照录制文件中的延时节奏回放。",
        "file_name" => "例如 cmd.exe、powershell.exe 或一个可执行文件路径。",
        "arguments" => "填写 JSON 字符串数组，例如 [\"/d\", \"/c\", \"echo hello\"]。",
        "working_directory" => "可选；留空时使用 BetterGI 程序目录。",
        "operation" => "必须是当前进程中显式注册的稳定操作名。",
        "values" => "填写 JSON 数字数组。",
        "delay_milliseconds" => "用于延迟执行；当前样例允许 0—600000。",
        "country" => "选择任务要前往的国家。",
        _ => "参数键：" + name
    };
}
