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
    
    /// <summary>
    /// 小地图遮罩是否启用
    /// </summary>
    [ObservableProperty]
    private bool _miniMapMaskEnabled = false;
    
    /// <summary>
    /// 自动记录路径功能是否启用
    /// </summary>
    [ObservableProperty]
    private bool _pathAutoRecordEnabled = false;

    private MapPointApiProvider _mapPointApiProvider = MapPointApiProvider.MihoyoMap;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MapPointApiProvider MapPointApiProvider
    {
        get => _mapPointApiProvider;
        set => SetProperty(ref _mapPointApiProvider, value);
    }
}
