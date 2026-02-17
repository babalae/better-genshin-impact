using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// 自动吃药配置
/// </summary>
[Serializable]
public partial class MapMaskConfig : ObservableObject
{
    /// <summary>
    /// 是否启用
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;

    private MapPointApiProvider _mapPointApiProvider = MapPointApiProvider.MihoyoMap;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MapPointApiProvider MapPointApiProvider
    {
        get => _mapPointApiProvider;
        set => SetProperty(ref _mapPointApiProvider, value);
    }
}
