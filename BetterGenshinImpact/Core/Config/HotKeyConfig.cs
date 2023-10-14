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
    [ObservableProperty] private string _autoPickEnabledHotkey = "";

    [ObservableProperty] private string _autoSkipEnabledHotkey = "Alt + F2";

    [ObservableProperty] private string _autoFishingEnabledHotkey = "";

    [ObservableProperty] private string _turnAroundHotkey = "Alt + F3";

    [ObservableProperty] private string _enhanceArtifactHotkey = "";

    [ObservableProperty] private string _autoGeniusInvokation = "";
}