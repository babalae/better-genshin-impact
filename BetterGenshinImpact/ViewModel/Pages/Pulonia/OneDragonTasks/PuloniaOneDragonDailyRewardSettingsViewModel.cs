using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia.OneDragonTasks;

/// <summary>领取每日奖励任务的专属设置草稿；对应 builtin.daily_rewards 的 country 与 party_name。</summary>
public partial class PuloniaOneDragonDailyRewardSettingsViewModel : PuloniaOneDragonFixedSettingsViewModel
{
    /// <summary>冒险者协会所在国家选项，与执行器 Schema 的枚举保持一致。</summary>
    public static readonly string[] CountryChoices = ["蒙德", "璃月", "稻妻", "须弥", "枫丹", "挪德卡莱"];

    /// <summary>载入时的国家快照。</summary>
    private readonly string _originalCountry;
    /// <summary>载入时的队伍名快照。</summary>
    private readonly string _originalPartyName;

    /// <summary>前往领取奖励的冒险者协会国家。</summary>
    [ObservableProperty] private string _country;
    /// <summary>领取前切换的游戏内队伍名称，留空使用当前队伍。</summary>
    [ObservableProperty] private string _partyName;

    /// <summary>从节点有效参数读取国家与队伍名。</summary>
    public PuloniaOneDragonDailyRewardSettingsViewModel(PuloniaTaskNodeViewModel node) : base(node)
    {
        _originalCountry = ReadString(Original, "country");
        _originalPartyName = ReadString(Original, "party_name");
        _country = _originalCountry;
        _partyName = _originalPartyName;
    }

    /// <summary>国家或队伍名变化时提交对应参数。</summary>
    public override void CollectChanges(JObject changes)
    {
        WriteString(changes, "country", _originalCountry, Country);
        WriteString(changes, "party_name", _originalPartyName, PartyName);
    }
}
