using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;

[Serializable]
public partial class AutoLeyLineOutcropConfig : ObservableObject
{
    [ObservableProperty]
    private string _leyLineOutcropType = "启示之花";

    [ObservableProperty]
    private string _country = "蒙德";

    [ObservableProperty]
    private bool _isResinExhaustionMode = false;

    [ObservableProperty]
    private bool _openModeCountMin = false;

    [ObservableProperty]
    private int _count = 6;

    [ObservableProperty]
    private bool _useTransientResin = false;

    [ObservableProperty]
    private bool _useFragileResin = false;

    [ObservableProperty]
    private string _team = string.Empty;

    [ObservableProperty]
    private string _friendshipTeam = string.Empty;

    [ObservableProperty]
    private int _timeout = 120;

    [ObservableProperty]
    private bool _useAdventurerHandbook = false;

    [ObservableProperty]
    private bool _isNotification = false;

    [ObservableProperty]
    private bool _isGoToSynthesizer = false;
}
