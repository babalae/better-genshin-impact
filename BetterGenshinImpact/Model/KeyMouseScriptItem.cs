using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Model;

public partial class KeyMouseScriptItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _createTimeStr = string.Empty;

    public DateTime CreateTime { get; set; }
}
