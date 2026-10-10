using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class NetworkConfig : ObservableObject
{
    public const string DefaultProxyUrl = "http://127.0.0.1:7890";

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _proxyUrl = DefaultProxyUrl;
}
