using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.LogParse;

public partial class LogParseConfig : ObservableObject
{
    [ObservableProperty] string _cookie = "";
    [ObservableProperty] private Dictionary<string, GameInfo> _cookieDictionary = new();
    [ObservableProperty] private Dictionary<string, ScriptGroupLogParseConfig> _scriptGroupLogDictionary = new();

    public partial class ScriptGroupLogParseConfig : ObservableObject
    {
        [ObservableProperty] private string _rangeValue = "CurrentConfig";
        [ObservableProperty] private string _dayRangeValue = "7";
        [ObservableProperty] private bool _hoeingStatsSwitch;
        [ObservableProperty] private bool _faultStatsSwitch;
        [ObservableProperty] private bool _generateFarmingPlanData;
        [ObservableProperty] private string _hoeingDelay= "0";
        [ObservableProperty] private bool _mergerStatsSwitch;
    }
}