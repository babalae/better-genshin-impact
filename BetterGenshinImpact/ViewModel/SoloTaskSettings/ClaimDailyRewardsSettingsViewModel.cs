using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>领取每日奖励任务的专属设置草稿；对应 claim_daily_rewards 的 country 与 party_name。</summary>
public partial class ClaimDailyRewardsSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <summary>冒险家协会所在国家选项，对应路线文件“冒险家协会_{国家}.json”。</summary>
    public static readonly string[] CountryChoices = ["蒙德", "璃月", "稻妻", "须弥", "枫丹", "挪德卡莱"];

    /// <summary>载入时的国家快照。</summary>
    private readonly string _originalCountry;
    /// <summary>载入时的队伍名快照。</summary>
    private readonly string _originalPartyName;

    /// <summary>前往领取奖励的冒险家协会国家。</summary>
    [ObservableProperty] private string _country;
    /// <summary>领取前切换的游戏内队伍名称，留空使用当前队伍。</summary>
    [ObservableProperty] private string _partyName;

    /// <summary>从当前有效参数读取国家与队伍名。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public ClaimDailyRewardsSettingsViewModel(JObject original) : base(original)
    {
        _originalCountry = ReadString(Original, "country");
        _originalPartyName = ReadString(Original, "party_name");
        _country = _originalCountry;
        _partyName = _originalPartyName;
    }

    /// <summary>国家或队伍名变化时提交对应参数。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        WriteString(changes, "country", _originalCountry, Country);
        WriteString(changes, "party_name", _originalPartyName, PartyName);
    }
}
