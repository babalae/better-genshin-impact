using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia.OneDragonTasks;

/// <summary>合成树脂任务的专属设置草稿；对应 builtin.craft_condensed_resin 的 country 参数。</summary>
public partial class PuloniaOneDragonCraftSettingsViewModel : PuloniaOneDragonFixedSettingsViewModel
{
    /// <summary>可合成的国家选项，与执行器 Schema 的枚举保持一致。</summary>
    public static readonly string[] CountryChoices = ["蒙德", "璃月", "稻妻", "枫丹"];

    /// <summary>载入时的国家快照。</summary>
    private readonly string _originalCountry;

    /// <summary>合成树脂合成台所在国家。</summary>
    [ObservableProperty] private string _country;

    /// <summary>从节点有效参数读取国家。</summary>
    public PuloniaOneDragonCraftSettingsViewModel(PuloniaTaskNodeViewModel node) : base(node)
    {
        _originalCountry = ReadString(Original, "country");
        _country = _originalCountry;
    }

    /// <summary>国家变化时提交 country。</summary>
    public override void CollectChanges(JObject changes)
        => WriteString(changes, "country", _originalCountry, Country);
}
