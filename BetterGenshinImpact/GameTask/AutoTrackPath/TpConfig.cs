using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public partial class TpConfig : ObservableObject
{
    [ObservableProperty]
    private bool _mapZoomEnabled = true; // 地图缩放开关

    [ObservableProperty]
    private int _mapZoomOutDistance = 1000; // 地图缩小的最小距离，单位：像素

    [ObservableProperty]
    private int _mapZoomInDistance = 400; // 地图放大的最大距离，单位：像素

    [ObservableProperty]
    private int _stepIntervalMilliseconds = 20; // 鼠标移动时间间隔，单位：ms

    [ObservableProperty]
    private double _maxZoomLevel = 5.0; // 最大缩放等级

    [ObservableProperty]
    private double _minZoomLevel = 2.0; // 最小缩放等级

    [ObservableProperty]
    private double _reviveStatueOfTheSevenPointX = 2296.4; // 七天神像点位X坐标

    [ObservableProperty]
    private double _reviveStatueOfTheSevenPointY = -824.4; // 七天神像点位Y坐标
    
    // 地图缩放程度的按钮 间隔 35 和截图有关的，按钮高2
    
    /// <summary>
    /// 缩放比例按钮的最上 y 坐标（地图最大化时、且坐标是按钮中心）
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private int _zoomStartY = 468; // y-coordinate for zoom start

    /// <summary>
    /// 缩放比例按钮的最下 y 坐标（地图最小化时、且坐标是按钮中心）
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private int _zoomEndY = 612; // y-coordinate for zoom end

    /// <summary>
    /// 缩放比例按钮的 x 坐标
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private int _zoomButtonX = 47; // x-coordinate for zoom button

    [ObservableProperty]
    private double _tolerance = 200; // 允许的移动误差

    [ObservableProperty]
    private int _maxIterations = 30; // 移动最大次数

    [ObservableProperty]
    private int _maxMouseMove = 300; // 单次移动最大距离
    
    [ObservableProperty]
    private double _mapScaleFactor = 2.661;  // 游戏坐标和 mapZoomLevel=1 时的像素比例因子。
    
    [ObservableProperty]
    [property: JsonIgnore]
    private double _precisionThreshold = 0.05;
    
}