using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia.OneDragonTasks;

/// <summary>自动秘境每周安排中单日的输入行；字段留空表示沿用每日配置。</summary>
public partial class PuloniaOneDragonDomainWeekdayRowViewModel : ObservableObject
{
    /// <summary>清单里展示的日期名称。</summary>
    public string DayName { get; }

    /// <summary>本日进入秘境切换的队伍名称，留空沿用每日配置。</summary>
    [ObservableProperty] private string _partyName;
    /// <summary>本日刷取的秘境名称，留空沿用每日配置。</summary>
    [ObservableProperty] private string _domainName;
    /// <summary>本日的周日奖励序号，留空沿用每日配置。</summary>
    [ObservableProperty] private string _sundayValue;

    /// <summary>以日期名建立空行，字段由秘境视图模型填充。</summary>
    public PuloniaOneDragonDomainWeekdayRowViewModel(string dayName)
    {
        DayName = dayName;
        _partyName = string.Empty;
        _domainName = string.Empty;
        _sundayValue = string.Empty;
    }
}
