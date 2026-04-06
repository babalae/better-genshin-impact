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
    public const string HoYoLabLanguageEnUs = "en-us";
    public const string HoYoLabLanguagePtPt = "pt-pt";
    public const string HoYoLabLanguageEsEs = "es-es";

    /// <summary>
    /// 是否启用
    /// </summary>
    [ObservableProperty]
    private bool _enabled = false;
    
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

    private string _hoYoLabLanguage = HoYoLabLanguageEnUs;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MapPointApiProvider MapPointApiProvider
    {
        get => _mapPointApiProvider;
        set => SetProperty(ref _mapPointApiProvider, value);
    }

    public string HoYoLabLanguage
    {
        get => _hoYoLabLanguage;
        set => SetProperty(ref _hoYoLabLanguage, value);
    }
}
