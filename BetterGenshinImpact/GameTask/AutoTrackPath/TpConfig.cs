using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using Range = OpenCvSharp.Range;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public partial class TpConfig : ObservableValidator
{
    [ObservableProperty]
    private bool _mapZoomEnabled = true; // 地图缩放开关

    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(600, Int32.MaxValue, ErrorMessage = "恰当的地图缩小的最小距离：>= 600")]
    private int _mapZoomOutDistance = 1000; // 地图缩小的最小距离，单位：像素
    partial void OnMapZoomOutDistanceChanged(int value)
    {
        // 如果验证失败且当前值不是默认值
        if (value < 600)
        {
            MapZoomOutDistance = 1000;
        }
    }
    
    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(200, 600, ErrorMessage = "恰当的地图缩小的最小距离:200-600")]
    private int _mapZoomInDistance = 400; // 地图放大的最大距离，单位：像素
    partial void OnMapZoomInDistanceChanged(int value)
    {
        if (value is < 200 or > 600)
        {
            MapZoomInDistance = 400;
        }
    }
    
    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(2, 100, ErrorMessage = "恰当的鼠标移动时间间隔:2-100")]
    private int _stepIntervalMilliseconds = 20; // 鼠标移动时间间隔，单位：ms
    partial void OnStepIntervalMillisecondsChanged(int value)
    {
        if (value is < 2 or > 100)
        {
            StepIntervalMilliseconds = 20;
        }
    }
    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(1.0, 6.0)]
    private double _maxZoomLevel = 5.0; // 最大缩放等级

    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(1.0, 6.0)]
    private double _minZoomLevel = 2.0; // 最小缩放等级

    [ObservableProperty]
    private double _reviveStatueOfTheSevenPointX = 2296.4; // 七天神像点位X坐标

    [ObservableProperty]
    private double _reviveStatueOfTheSevenPointY = -824.4; // 七天神像点位Y坐标
    
    [ObservableProperty] 
    private string _reviveStatueOfTheSevenArea = "道成林";  // 七天神像所在区域

    [ObservableProperty] 
    private string _reviveStatueOfTheSevenCountry = "须弥";  // 七天神像所在国家
    
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isReviveInNearestStatueOfTheSeven = false; // 是否就近回复

    [ObservableProperty] 
    private GiTpPosition? _reviveStatueOfTheSeven;
    
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _shouldMove = false;  // 回血前是否需要移动

    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(1.0, 30.0, ErrorMessage = "恰当的回血等待时间：1.0-30.0")]
    private double _hpRestoreDuration = 5.0;  // 回血等待时间

    partial void OnHpRestoreDurationChanged(double value)
    {
        if (value is < 1.0 or > 30.0)
        {
            HpRestoreDuration = 5.0;
        }
    }
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
    [NotifyDataErrorInfo] 
    [Range(50, 500)]
    private double _tolerance = 200; // 允许的移动误差

    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(10, 500)]
    private int _maxIterations = 30; // 移动最大次数

    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(100, 2000)]
    private int _maxMouseMove = 300; // 单次移动最大距离

    partial void OnMaxMouseMoveChanged(int value)
    {
        if (value is < 100 or > 2000)
        {
            MaxMouseMove = 300;
        }
    }
    
    [ObservableProperty]
    private double _mapScaleFactor = 2.361;  // 游戏坐标和 mapZoomLevel=1 时的像素比例因子。
    
    [ObservableProperty]
    [property: JsonIgnore]
    private double _precisionThreshold = 0.05;
}