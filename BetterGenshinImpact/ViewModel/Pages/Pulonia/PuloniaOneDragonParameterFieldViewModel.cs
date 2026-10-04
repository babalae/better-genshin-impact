using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>一条龙卡片设置的独立编辑字段；保留类型与原值，不写入全局配置。</summary>
public partial class PuloniaOneDragonParameterFieldViewModel : ObservableObject
{
    /// <summary>持久化参数键。</summary>
    public string Name { get; }
    /// <summary>卡片标题。</summary>
    public string DisplayName { get; }
    /// <summary>卡片说明。</summary>
    public string Description { get; }
    /// <summary>原始有效值；只保存用户实际改变的字段。</summary>
    private readonly JToken? _original;
    /// <summary>底层标量编辑器，负责保留枚举和数字类型。</summary>
    private readonly PuloniaTaskParameterFieldViewModel _scalar;
    /// <summary>数字输入初始值，避免打开界面把 null 转成 0。</summary>
    private readonly double _initialNumber;
    /// <summary>初始文本，用于区分未编辑的空值与显式空文本。</summary>
    private readonly string _initialText;
    /// <summary>初始开关值，未编辑的可空布尔值仍保持 null。</summary>
    private readonly bool _initialBoolean;
    /// <summary>初始选项，未知或空选项不会仅因打开设置被改写。</summary>
    private readonly string? _initialChoice;
    /// <summary>树脂优先级以有序子字段展示，保存时恢复数组类型。</summary>
    private readonly bool _isArray;
    /// <summary>对象的子设置。</summary>
    public IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> Children { get; }
    /// <summary>下拉框选项。</summary>
    public IReadOnlyList<string> Choices => _scalar.Choices;
    /// <summary>是否为对象卡片。</summary>
    public bool IsObject => Children.Count > 0;
    /// <summary>是否为高级 JSON 设置。</summary>
    public bool IsAdvancedJson => !IsObject && _scalar.IsJsonEditor;
    /// <summary>是否为开关。</summary>
    public bool IsBoolean => _scalar.IsBooleanEditor;
    /// <summary>是否为下拉框。</summary>
    public bool IsChoice => _scalar.IsChoiceEditor;
    /// <summary>是否为数字输入框。</summary>
    public bool IsNumber { get; }
    /// <summary>是否为文本输入框。</summary>
    public bool IsText => _scalar.IsTextEditor && !IsNumber;
    /// <summary>数字下限；最终以能力 Schema 校验为准。</summary>
    public double Minimum { get; }
    /// <summary>数字上限；最终以能力 Schema 校验为准。</summary>
    public double Maximum { get; }
    /// <summary>数值编辑草稿。</summary>
    [ObservableProperty] private double _numberValue;
    /// <summary>文本或高级 JSON 草稿。</summary>
    public string TextValue { get => _scalar.TextValue; set { _scalar.TextValue = value; OnPropertyChanged(); } }
    /// <summary>开关草稿。</summary>
    public bool BooleanValue { get => _scalar.BooleanValue; set { _scalar.BooleanValue = value; OnPropertyChanged(); } }
    /// <summary>下拉选项草稿。</summary>
    public string? SelectedChoice { get => _scalar.SelectedChoice; set { _scalar.SelectedChoice = value; OnPropertyChanged(); } }

    /// <summary>不解析草稿即可判断是否输入过，切换行不会因无效 JSON 丢失草稿。</summary>
    internal bool HasInputChanges => IsObject ? Children.Any(child => child.HasInputChanges)
        : IsNumber ? !NumberValue.Equals(_initialNumber)
        : TextValue != _initialText || BooleanValue != _initialBoolean || SelectedChoice != _initialChoice;

    /// <summary>检查参数来源是否仍与打开草稿时一致，避免覆盖后来修改的继承值。</summary>
    internal bool MatchesOriginalValue(JToken? value) => JToken.DeepEquals(_original, value);

    /// <summary>按能力参数生成老一条龙样式所需的字段，复杂对象递归展示。</summary>
    public PuloniaOneDragonParameterFieldViewModel(string name, JObject schema, JToken? value,
        bool required = false)
    {
        Name = name;
        var displayKey = System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
        DisplayName = schema.Value<string>("title") ?? displayKey switch
        {
            "team_names" => "战斗队伍", "fight_finish_detect_enabled" => "检测战斗结束",
            "finish_detect_config" => "战斗结束检测", "action_scheduler_by_cd" => "按冷却调度动作",
            "only_pick_elite_drops_mode" => "精英掉落拾取模式", "pick_drops_after_fight_enabled" => "战斗后拾取掉落物",
            "pick_drops_after_fight_seconds" => "战斗后拾取时长（秒）", "fast_check_enabled" => "快速结束检测",
            "rotate_find_enemy_enabled" => "旋转寻找敌人", "fast_check_params" => "快速检测参数",
            "check_after_switch_avatar" => "切换角色后检测", "check_end_delay" => "结束检测延时",
            "before_detect_delay" => "检测前延时", "rotary_factor" => "旋转幅度",
            "is_first_check" => "首次检测", "check_before_burst" => "施放元素爆发前检测",
            "skip_fight_end_check_when_enemy_visible" => "敌人可见时跳过结束检测",
            "block_check_before_battle_seconds" => "战斗开始后暂缓检测（秒）",
            "paimon_end_check_enabled" => "派蒙图标辅助结束检测", "paimon_end_check_delay" => "派蒙检测延时（秒）",
            "battle_threshold_for_loot" => "拾取前战斗次数", "kazuha_pickup_enabled" => "万叶辅助拾取",
            "qin_double_pick_up" => "琴二次拾取", "guardian_avatar" => "保护角色",
            "guardian_combat_skip" => "保护角色跳过战斗", "guardian_avatar_hold" => "保护角色长按技能",
            "burst_enabled" => "使用元素爆发", "kazuha_party_name" => "万叶拾取队伍",
            "swimming_enabled" => "游泳辅助", "exp_based_pickup_enabled" => "根据经验识别拾取",
            "enable_combat_targeting" => "战斗目标锁定", "lock_lost_wait_time" => "目标丢失等待（秒）",
            _ => PuloniaTaskParameterFieldViewModel.GetDisplayName(displayKey)
        };
        _original = value?.DeepClone();
        _scalar = new(name, schema, value, value is not null, required);
        Description = schema.Value<string>("description") ?? (required ? "请选择或填写此项设置。" : "未修改的设置继续使用参数来源中的值。");
        _isArray = schema["type"]?.Type == JTokenType.String && schema.Value<string>("type") == "array";
        Children = schema["properties"] is JObject properties
            ? properties.Properties().Select(p => new PuloniaOneDragonParameterFieldViewModel(p.Name,
                (JObject)p.Value, value is JArray array && int.TryParse(p.Name, out var index) && index < array.Count
                    ? array[index] : (value as JObject)?[p.Name] ?? p.Value["default"])).ToArray() : [];
        var type = schema["type"] is JArray types ? types.Values<string>().FirstOrDefault(t => t != "null") : schema.Value<string>("type");
        IsNumber = _scalar.IsTextEditor && type is "integer" or "number";
        Minimum = schema.Value<double?>("minimum") ?? -double.MaxValue;
        Maximum = schema.Value<double?>("maximum") ?? double.MaxValue;
        NumberValue = _initialNumber = value?.Type is JTokenType.Integer or JTokenType.Float ? value.Value<double>() : 0;
        _initialText = TextValue;
        _initialBoolean = BooleanValue;
        _initialChoice = SelectedChoice;
    }

    /// <summary>返回变化后的值；未编辑字段保留原始 null、缺失字段和 JSON 类型。</summary>
    public JToken? BuildValue()
    {
        if (IsObject)
        {
            if (Children.All(child => !child.HasChanges())) return _original?.DeepClone();
            if (_isArray) return new JArray(Children.Select(child => child.BuildValue() ?? JValue.CreateNull()));
            var result = _original is JObject original ? (JObject)original.DeepClone() : new JObject();
            foreach (var child in Children)
            {
                // 展示时补出的默认值只用于输入提示，不能因兄弟字段变化而固化成覆盖。
                if (!child.HasChanges()) continue;
                var value = child.BuildValue();
                if (value is not null) result[child.Name] = value;
            }
            return result;
        }
        if (IsNumber)
        {
            if (NumberValue.Equals(_initialNumber)) return _original?.DeepClone();
            if (!double.IsFinite(NumberValue)) throw new FormatException($"“{DisplayName}”必须是有效数字。");
            _scalar.TextValue = NumberValue.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (TextValue == _initialText && BooleanValue == _initialBoolean && SelectedChoice == _initialChoice)
            return _original?.DeepClone();
        // 默认缺失的可选选项与数字不应仅因打开卡片就产生覆盖。
        if (_original is null && (IsChoice && SelectedChoice is null || IsText && TextValue.Length == 0)) return null;
        return _scalar.BuildValue();
    }

    /// <summary>判断字段是否改变，供一次性提交变化字段使用。</summary>
    public bool HasChanges() => !JToken.DeepEquals(_original, BuildValue());
}
