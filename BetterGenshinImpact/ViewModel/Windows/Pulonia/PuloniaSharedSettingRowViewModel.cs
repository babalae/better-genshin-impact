using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 共享设置编辑器中的一行：名称与说明、按编辑提示选择的控件、来源和恢复继承。
/// 用户输入立即交给编辑器写入；从模型刷新时不回写。
/// </summary>
public partial class PuloniaSharedSettingRowViewModel : ObservableObject
{
    /// <summary>所属编辑器。</summary>
    private readonly PuloniaSharedSettingsEditorViewModel _owner;

    /// <summary>正在从模型刷新显示值，期间的属性变化不回写。</summary>
    private bool _isLoading;

    /// <summary>最近一次从模型读到的值，用于避免重复写入和打断输入。</summary>
    private JToken _loadedValue = JValue.CreateNull();

    /// <summary>设置项定义。</summary>
    public PuloniaSettingDefinition Definition { get; }

    /// <summary>设置项 ID。</summary>
    public string Key => Definition.Key;

    /// <summary>名称。</summary>
    public string DisplayName => Definition.DisplayName;

    /// <summary>说明。</summary>
    public string Description => Definition.Description;

    /// <summary>是否有说明。</summary>
    public bool HasDescription => !string.IsNullOrEmpty(Definition.Description);

    /// <summary>数值单位。</summary>
    public string Unit => Definition.Unit ?? string.Empty;

    /// <summary>是否显示单位。</summary>
    public bool HasUnit => !string.IsNullOrEmpty(Definition.Unit);

    /// <summary>所属类别名称，搜索结果中显示。</summary>
    public string CategoryName { get; }

    /// <summary>是否为高级设置项。</summary>
    public bool IsAdvanced => Definition.IsAdvanced;

    /// <summary>开关控件。</summary>
    public bool IsBoolean => Definition.Editor == PuloniaSettingEditorKind.Boolean;

    /// <summary>文本控件。</summary>
    public bool IsText => Definition.Editor == PuloniaSettingEditorKind.Text;

    /// <summary>数字控件（整数或小数）。</summary>
    public bool IsNumber => Definition.Editor is PuloniaSettingEditorKind.Integer or PuloniaSettingEditorKind.Number;

    /// <summary>数字控件允许的小数位数。</summary>
    public int MaxDecimalPlaces => Definition.Editor == PuloniaSettingEditorKind.Integer ? 0 : 3;

    /// <summary>下拉选项控件。</summary>
    public bool IsChoice => Definition.Editor == PuloniaSettingEditorKind.Choice;

    /// <summary>战斗策略选择器。</summary>
    public bool IsCombatStrategy => Definition.Editor == PuloniaSettingEditorKind.CombatStrategy;

    /// <summary>路线 CD 专用编辑入口。</summary>
    public bool IsCollectionCooldown => Definition.Editor == PuloniaSettingEditorKind.CollectionCooldown;

    /// <summary>固定选项。</summary>
    public IReadOnlyList<PuloniaSettingChoice> Choices => Definition.Choices;

    /// <summary>开关值。</summary>
    [ObservableProperty] private bool _boolValue;

    /// <summary>文本值。</summary>
    [ObservableProperty] private string _textValue = string.Empty;

    /// <summary>数字值；为空表示未填写（仅可空数字有效）。</summary>
    [ObservableProperty] private double? _numberValue;

    /// <summary>选中的选项。</summary>
    [ObservableProperty] private PuloniaSettingChoice? _selectedChoice;

    /// <summary>战斗策略名。</summary>
    [ObservableProperty] private string _strategyValue = string.Empty;

    /// <summary>当前值的简短文字，供路线 CD 等不内嵌编辑的项显示（路线 CD 显示规则摘要，如“自动识别”“每日 04:00”）。</summary>
    [ObservableProperty] private string _valueText = string.Empty;

    /// <summary>来源文字；界面只显示来源图标，文字放在悬停说明中，路线 CD 窗口也用它说明配置来源。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceToolTip))]
    private string _sourceText = string.Empty;

    /// <summary>来源种类，决定来源图标。</summary>
    [ObservableProperty] private PuloniaSharedSettingSourceKind _sourceKind;

    /// <summary>值的来历（悬停说明）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistory), nameof(SourceToolTip))]
    private string _historyText = string.Empty;

    /// <summary>来源图标的悬停说明：来源文字，节点上另附沿设置链的每一层取值。</summary>
    public string SourceToolTip => string.IsNullOrEmpty(HistoryText)
        ? "来源：" + SourceText
        : "来源：" + SourceText + "\n\n值的来历：\n" + HistoryText;

    /// <summary>编辑对象自身覆盖了这一项。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReset))]
    private bool _isOverridden;

    /// <summary>覆盖值与恢复继承后的值相同。</summary>
    [ObservableProperty] private bool _isSameAsInherited;

    /// <summary>按依赖条件可编辑。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReset))]
    private bool _isEditable = true;

    /// <summary>不可编辑的原因。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisabledReason))]
    private string? _disabledReason;

    /// <summary>是否显示不可编辑的原因。</summary>
    public bool HasDisabledReason => !string.IsNullOrEmpty(DisabledReason);

    /// <summary>是否有可悬停查看的值来历。</summary>
    public bool HasHistory => !string.IsNullOrEmpty(HistoryText);

    /// <summary>恢复按钮的说明：节点上为恢复继承，方案中为移除。</summary>
    public string ResetToolTip => _owner.ShowsInheritance ? "恢复继承（移除本节点的这项修改）" : "从方案中移除这一项";

    /// <summary>跨项约束或格式提示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    private string? _warning;

    /// <summary>是否有提示。</summary>
    public bool HasWarning => !string.IsNullOrEmpty(Warning);

    /// <summary>是否可以恢复继承。</summary>
    public bool CanReset => IsOverridden && _owner.IsEditable;

    /// <summary>建立一行。</summary>
    internal PuloniaSharedSettingRowViewModel(PuloniaSharedSettingsEditorViewModel owner, PuloniaSettingDefinition definition,
        string categoryName)
    {
        _owner = owner;
        Definition = definition;
        CategoryName = categoryName;
    }

    /// <summary>当前从模型读到的值。</summary>
    internal JToken LoadedValue => _loadedValue;

    /// <summary>
    /// 从模型刷新显示；值与上次相同时不改动控件，避免打断正在进行的输入。
    /// </summary>
    internal void Load(JToken value, string source, PuloniaSharedSettingSourceKind sourceKind, bool overridden,
        bool sameAsInherited, string history)
    {
        SourceText = source;
        SourceKind = sourceKind;
        IsOverridden = overridden;
        IsSameAsInherited = overridden && sameAsInherited;
        HistoryText = history;
        OnPropertyChanged(nameof(CanReset));
        if (JToken.DeepEquals(_loadedValue, value))
            return;
        _loadedValue = value.DeepClone();
        _isLoading = true;
        try
        {
            ValueText = PuloniaSharedSettingText.Format(Key, value);
            switch (Definition.Editor)
            {
                case PuloniaSettingEditorKind.Boolean:
                    BoolValue = value.Type == JTokenType.Boolean && value.Value<bool>();
                    break;
                case PuloniaSettingEditorKind.Integer or PuloniaSettingEditorKind.Number:
                    NumberValue = value.Type is JTokenType.Integer or JTokenType.Float ? value.Value<double>() : null;
                    break;
                case PuloniaSettingEditorKind.Choice:
                    SelectedChoice = Choices.FirstOrDefault(choice => JToken.DeepEquals(choice.Value, value));
                    break;
                case PuloniaSettingEditorKind.CombatStrategy:
                    StrategyValue = value.Type == JTokenType.String ? value.Value<string>()! : string.Empty;
                    break;
                case PuloniaSettingEditorKind.Text:
                    TextValue = value.Type == JTokenType.String ? value.Value<string>()! : string.Empty;
                    break;
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>开关变化即写入。</summary>
    partial void OnBoolValueChanged(bool value) => Commit(new JValue(value));

    /// <summary>选项变化即写入。</summary>
    partial void OnSelectedChoiceChanged(PuloniaSettingChoice? value)
    {
        if (value is not null)
            Commit(value.Value);
    }

    /// <summary>战斗策略变化即写入。</summary>
    partial void OnStrategyValueChanged(string value) => Commit(new JValue(value ?? string.Empty));

    /// <summary>文本逐字写入；允许 null 的项清空时写 null。</summary>
    partial void OnTextValueChanged(string value)
        => Commit(string.IsNullOrEmpty(value) && AllowsNull() ? JValue.CreateNull() : new JValue(value ?? string.Empty));

    /// <summary>数字变化即写入；未填写时只有可空项写 null，其余项保持原值并提示。</summary>
    partial void OnNumberValueChanged(double? value)
    {
        if (_isLoading)
            return;
        if (value is null)
        {
            if (AllowsNull())
                Commit(JValue.CreateNull());
            else
                Warning = "请填写数值，空值不会生效。";
            return;
        }
        Commit(Definition.Editor == PuloniaSettingEditorKind.Integer
            ? new JValue((long)Math.Round(value.Value))
            : new JValue(value.Value));
    }

    /// <summary>恢复继承（方案中为移除该项）。</summary>
    [RelayCommand]
    private void Reset() => _owner.Reset(this);

    /// <summary>打开路线 CD 编辑窗口。</summary>
    [RelayCommand]
    private void EditCollectionCooldown() => _owner.EditCollectionCooldown(this);

    /// <summary>值类型是否允许 null。</summary>
    private bool AllowsNull()
        => Definition.ValueSchema["type"] is JArray types && types.Values<string>().Contains("null");

    /// <summary>把用户输入交给编辑器；刷新期间或值未变化时不写。</summary>
    private void Commit(JToken value)
    {
        if (_isLoading || JToken.DeepEquals(_loadedValue, value))
            return;
        _loadedValue = value.DeepClone();
        ValueText = PuloniaSharedSettingText.Format(Key, value);
        _owner.Commit(this, value);
    }
}
