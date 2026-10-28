using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.ViewModel.Resin;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>合成浓缩树脂任务的专属设置草稿；对应 craft_condensed_resin 的 country 参数。</summary>
public partial class CraftCondensedResinSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    /// <remarks>craft_count 由体力计划按预算计算，界面不提供编辑；列在这里使其不显示为通用卡片，并保持原值。</remarks>
    public override string[] EditedParameterNames => ["country", "min_resin_to_keep", "craft_count"];

    /// <summary>可合成的国家选项，对应路线文件“合成台_{国家}.json”。</summary>
    public static readonly string[] CountryChoices = ["蒙德", "璃月", "稻妻", "枫丹"];

    /// <summary>载入时的国家快照。</summary>
    private readonly string _originalCountry;

    /// <summary>合成浓缩树脂的合成台所在国家。</summary>
    [ObservableProperty] private string _country;

    /// <summary>合成后保留的原粹树脂数量；不大于零时按默认方式合成。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResinSummary))]
    private int _minResinToKeep;
    /// <summary>可选弹窗服务；隔离环境仅显示摘要。</summary>
    private readonly IResinSettingsDialogs? _dialogs;
    /// <summary>入口卡片的统一合成摘要。</summary>
    public string ResinSummary => ResinUsageText.CraftSummary(MinResinToKeep);
    /// <summary>是否可以打开编辑器。</summary>
    public bool CanEditResin => _dialogs is not null;

    /// <summary>从当前有效参数读取国家。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public CraftCondensedResinSettingsViewModel(JObject original) : this(original, null) { }

    /// <summary>注入可选的树脂弹窗服务，保留快照中的 -1 表示以免产生无操作修改。</summary>
    [ActivatorUtilitiesConstructor]
    public CraftCondensedResinSettingsViewModel(JObject original, IResinSettingsDialogs? dialogs = null) : base(original)
    {
        _dialogs = dialogs;
        _originalCountry = ReadString(Original, "country");
        _country = _originalCountry;
        _minResinToKeep = ReadInt(Original, "min_resin_to_keep");
    }

    /// <summary>确定后只更新本页面草稿，取消不改值；提交仍由宿主负责。</summary>
    [RelayCommand(CanExecute = nameof(CanEditResin))]
    private void EditResin()
    {
        if (_dialogs?.EditCraftResin(MinResinToKeep) is { } value) MinResinToKeep = value;
    }

    /// <summary>国家变化时提交 country。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        WriteString(changes, "country", _originalCountry, Country);
        WriteInt(changes, "min_resin_to_keep", ReadInt(Original, "min_resin_to_keep"), MinResinToKeep);
    }
}
