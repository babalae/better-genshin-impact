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

    /// <summary>枚举显示值对应的原始 JSON，避免整数枚举编辑后变成字符串。</summary>
    private readonly IReadOnlyDictionary<string, JToken> _choiceValues;

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
        _choiceValues = schema["enum"] is JArray choices
            ? choices.ToDictionary(choice => choice.Type == JTokenType.String ? choice.Value<string>()! : choice.ToString(Formatting.None), choice => choice.DeepClone(), StringComparer.Ordinal)
            : new Dictionary<string, JToken>();
        Choices = _choiceValues.Keys.ToArray();
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
            SelectedChoice = initialValue.Type == JTokenType.String ? initialValue.Value<string>() : initialValue.ToString(Formatting.None);
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
            return _choiceValues.TryGetValue(SelectedChoice, out var choice)
                ? choice.DeepClone() : throw new FormatException($"“{DisplayName}”不在允许的选项中。");
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

            // 可空集合的显式 null 与空数组、空对象不同，保留用户输入的 JSON 类型。
            if (_isNullable && value.Type == JTokenType.Null)
                return value;

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
    internal static string GetDisplayName(string name) => name switch
    {
        "domain_name" => "秘境", "boss_name" => "首领", "strategy_name" => "战斗策略",
        "team_name" or "team" or "fight_team_name" => "队伍", "run_count" or "count" or "round_count" => "次数",
        "specify_run_count" => "指定次数", "revive_retry_count" => "死亡重试次数", "timeout" => "战斗超时（秒）",
        "boss_num" => "幽境首领序号", "weekday_overrides" => "每周安排（0 周日至 6 周六，服务器四点日切）",
        "one_dragon_mode" => "地脉花一条龙模式", "specify_resin_use" => "指定树脂用量", "max_artifact_star" => "分解最大星级",
        "auto_artifact_salvage" => "结束后分解圣遗物", "reward_recognition_enabled" => "识别奖励",
        "use_transient_resin" => "使用须臾树脂", "use_fragile_resin" => "使用脆弱树脂",
        "resin_priority_list" => "树脂优先级", "is_resin_exhaustion_mode" => "刷至树脂耗尽",
        "sunday_selected_value" => "周日奖励序号", "ley_line_outcrop_type" => "地脉花类型",
        "fight_config" => "战斗设置", "return_to_statue_after_each_round" => "每轮结束回神像",
        "original_resin_use_count" => "原粹树脂次数", "original_resin20_use_count" => "20 树脂次数",
        "original_resin40_use_count" => "40 树脂次数", "condensed_resin_use_count" => "浓缩树脂次数",
        "transient_resin_use_count" => "须臾树脂次数", "fragile_resin_use_count" => "脆弱树脂次数",
        "friendship_team" => "好感队伍", "open_mode_count_min" => "次数取较小值",
        "is_go_to_synthesizer" => "前往合成台", "scan_drops_after_reward_enabled" => "领奖后扫描掉落物",
        "scan_drops_after_reward_seconds" => "扫描掉落物时长（秒）", "fight_end_delay" => "战斗结束后等待（秒）",
        "short_movement" => "短距离移动", "walk_to_f" => "走近交互", "left_right_move_times" => "左右移动次数",
        "auto_eat" => "自动吃药",
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
