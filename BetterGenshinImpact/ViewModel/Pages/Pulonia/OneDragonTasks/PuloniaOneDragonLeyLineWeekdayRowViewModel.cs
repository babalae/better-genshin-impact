using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia.OneDragonTasks;

/// <summary>自动地脉花每周安排中单日的输入行；类型与国家留空表示沿用独立任务默认设置。</summary>
public partial class PuloniaOneDragonLeyLineWeekdayRowViewModel : ObservableObject
{
    /// <summary>清单里展示的日期名称。</summary>
    public string DayName { get; }

    /// <summary>当天是否执行；全部日期都不勾选时视为每天执行。</summary>
    [ObservableProperty] private bool _enabled;
    /// <summary>本日刷取的地脉花类型，留空沿用默认。</summary>
    [ObservableProperty] private string _type;
    /// <summary>本日前往的国家，留空沿用默认。</summary>
    [ObservableProperty] private string _country;

    /// <summary>以日期名建立默认执行的空行，字段由地脉花视图模型填充。</summary>
    public PuloniaOneDragonLeyLineWeekdayRowViewModel(string dayName)
    {
        DayName = dayName;
        _enabled = true;
        _type = string.Empty;
        _country = string.Empty;
    }
}
