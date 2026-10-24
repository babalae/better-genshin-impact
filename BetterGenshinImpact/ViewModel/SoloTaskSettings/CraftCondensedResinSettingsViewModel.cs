using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>合成浓缩树脂任务的专属设置草稿；对应 craft_condensed_resin 的 country 参数。</summary>
public partial class CraftCondensedResinSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    public override string[] EditedParameterNames => ["country", "min_resin_to_keep"];

    /// <summary>可合成的国家选项，对应路线文件“合成台_{国家}.json”。</summary>
    public static readonly string[] CountryChoices = ["蒙德", "璃月", "稻妻", "枫丹"];

    /// <summary>载入时的国家快照。</summary>
    private readonly string _originalCountry;

    /// <summary>合成浓缩树脂的合成台所在国家。</summary>
    [ObservableProperty] private string _country;

    /// <summary>合成后保留的原粹树脂数量；不大于零时按默认方式合成。</summary>
    [ObservableProperty] private int _minResinToKeep;

    /// <summary>从当前有效参数读取国家。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public CraftCondensedResinSettingsViewModel(JObject original) : base(original)
    {
        _originalCountry = ReadString(Original, "country");
        _country = _originalCountry;
        _minResinToKeep = ReadInt(Original, "min_resin_to_keep");
    }

    /// <summary>国家变化时提交 country。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        WriteString(changes, "country", _originalCountry, Country);
        WriteInt(changes, "min_resin_to_keep", ReadInt(Original, "min_resin_to_keep"), MinResinToKeep);
    }
}
