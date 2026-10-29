using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using BetterGenshinImpact.ViewModel.Resin;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Wpf.Ui.Violeta.Controls;

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
    /// <summary>数组以有序子字段展示，保存时恢复数组类型。</summary>
    private readonly bool _isArray;
    /// <summary>对象的子设置。</summary>
    public IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> Children { get; }
    /// <summary>下拉框选项。</summary>
    public IReadOnlyList<string> Choices => _scalar.Choices;
    /// <summary>树脂使用整体编辑形态，不递归展开原始嵌套字段。</summary>
    public bool IsResinUsage => _scalar.IsResinUsageEditor;
    /// <summary>公共树脂入口，其他形态为 null。</summary>
    public ResinUsageEntryViewModel? Resin => _scalar.Resin;
    /// <summary>是否为对象卡片。</summary>
    public bool IsObject => Children.Count > 0;
    /// <summary>是否为高级 JSON 设置。</summary>
    public bool IsAdvancedJson => !IsObject && _scalar.IsJsonEditor;
    /// <summary>是否为开关。</summary>
    public bool IsBoolean => _scalar.IsBooleanEditor;
    /// <summary>是否为下拉框。</summary>
    public bool IsChoice => _scalar.IsChoiceEditor;
    /// <summary>是否为战斗策略选择器。</summary>
    public bool IsCombatStrategy => _scalar.IsCombatStrategyEditor;
    /// <summary>是否为秘境选择框。</summary>
    public bool IsDomain => _scalar.IsDomainEditor;
    /// <summary>秘境选择框的选项，按字段类型筛选（备选秘境、盛材移涌、祝圣移涌）；非秘境字段为空。</summary>
    public IReadOnlyList<ICascadingItem> DomainItems { get; }
    /// <summary>
    /// 秘境选择框的选中项，与 <see cref="TextValue"/> 互转：移涌活动秘境的空值显示为“跟随培养计划”，选中它时保存为空字符串。
    /// 选择框给出空选项时不改写，清空由选择框的清空按钮直接写回 <see cref="TextValue"/>。
    /// </summary>
    public ICascadingItem? SelectedDomainItem
    {
        get => IsDomain ? TrainingGuideDomainOptions.Find(DomainItems, TextValue) : null;
        set
        {
            if (value?.Tag is not string tag || string.IsNullOrWhiteSpace(tag)) return;
            TextValue = tag == TrainingGuideDomainOptions.FollowTrainingPlanOption ? string.Empty : tag;
        }
    }
    /// <summary>当前任务允许的策略类型。</summary>
    public CombatStrategyKind AllowedStrategyKinds => _scalar.AllowedStrategyKinds;
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
    public string TextValue
    {
        get => _scalar.TextValue;
        set
        {
            _scalar.TextValue = value;
            OnPropertyChanged();
            // 秘境选择框的选中项由文本换算，清空或选择后同步刷新
            if (IsDomain) OnPropertyChanged(nameof(SelectedDomainItem));
        }
    }
    /// <summary>开关草稿。</summary>
    public bool BooleanValue { get => _scalar.BooleanValue; set { _scalar.BooleanValue = value; OnPropertyChanged(); } }
    /// <summary>下拉选项草稿。</summary>
    public string? SelectedChoice { get => _scalar.SelectedChoice; set { _scalar.SelectedChoice = value; OnPropertyChanged(); } }

    /// <summary>不解析草稿即可判断是否输入过，切换行不会因无效 JSON 丢失草稿。</summary>
    internal bool HasInputChanges => IsResinUsage ? _scalar.HasResinChanges : IsObject ? Children.Any(child => child.HasInputChanges)
        : IsNumber ? !NumberValue.Equals(_initialNumber)
        : TextValue != _initialText || BooleanValue != _initialBoolean || SelectedChoice != _initialChoice;

    /// <summary>检查参数来源是否仍与打开草稿时一致，避免覆盖后来修改的继承值。</summary>
    internal bool MatchesOriginalValue(JToken? value) => JToken.DeepEquals(_original, value);

    /// <summary>按能力参数生成老一条龙样式所需的字段，复杂对象递归展示。</summary>
    public PuloniaOneDragonParameterFieldViewModel(string name, JObject schema, JToken? value,
        bool required = false, IResinSettingsDialogs? dialogs = null)
    {
        Name = name;
        // 战斗对象等子设置来自强类型配置，键可能是帕斯卡命名，先转为蛇形再查共用中文名。
        var displayKey = System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
        DisplayName = schema.Value<string>("title") ?? PuloniaTaskFieldNames.GetDisplayName(displayKey);
        _original = value?.DeepClone();
        _scalar = new(name, schema, value, value is not null, required, dialogs);
        DomainItems = IsDomain ? TrainingGuideDomainOptions.ForPicker(_scalar.DomainPicker) : [];
        if (Resin is not null)
            Resin.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Resin.Value)) OnPropertyChanged(nameof(Resin)); };
        Description = schema.Value<string>("description") ?? (required ? "请选择或填写此项设置。" : "未修改的设置继续使用参数来源中的值。");
        _isArray = schema["type"] is JArray allowedTypes
            ? allowedTypes.Values<string>().Contains("array")
            : schema.Value<string>("type") == "array";
        Children = !IsResinUsage && schema["properties"] is JObject properties
            ? properties.Properties().Select(p => new PuloniaOneDragonParameterFieldViewModel(p.Name,
                (JObject)p.Value, value is JArray array && int.TryParse(p.Name, out var index) && index < array.Count
                    ? array[index] : (value as JObject)?[p.Name] ?? p.Value["default"], dialogs: dialogs)).ToArray() : [];
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
        if (IsResinUsage) return _scalar.HasResinChanges ? _scalar.BuildValue() : _original?.DeepClone();
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
