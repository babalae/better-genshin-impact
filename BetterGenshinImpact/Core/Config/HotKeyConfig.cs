using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Model;
using System.Windows.Input;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class HotKeyConfig : ObservableObject
{
    [ObservableProperty] private string _bgiEnabledHotkey = "F11";

    [ObservableProperty] private string _autoPickEnabledHotkey = "F1";

    [ObservableProperty] private string _autoSkipEnabledHotkey = "F2";

    [ObservableProperty] private string _autoFishingEnabledHotkey = "";

    [ObservableProperty] private string _turnAroundHotkey = "F3";

    // [ObservableProperty] private string _enhanceArtifactHotkey = "F4";

    [ObservableProperty] private string _quickBuyHotkey = "";

    [ObservableProperty] private string _autoGeniusInvokation = "";

    [ObservableProperty] private string _autoWoodHotkey = "";
}