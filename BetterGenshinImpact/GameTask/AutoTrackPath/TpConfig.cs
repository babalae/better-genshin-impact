using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public partial class TpConfig : ObservableValidator
{
    public const int DefaultTeleportOperationDelayMilliseconds = 20;

    public const int DefaultExperimentalTeleportDragStepIntervalMilliseconds = 3;
    public const double DefaultExperimentalTeleportDragDistanceCorrection = 0.95d;
    public const int DefaultExperimentalTeleportStateRecognitionIntervalMilliseconds = 50;
    public const int DefaultExperimentalTeleportStateRecognitionInitialDelayMilliseconds = 100;
    public const int DefaultExperimentalTeleportStateTransitionTimeoutMilliseconds = 500;
    public const int DefaultExperimentalTeleportMapOpenTimeoutMilliseconds = 5000;
    public const int DefaultExperimentalTeleportMapOpenRepressIntervalMilliseconds = 2000;
    public const int DefaultExperimentalTeleportDragStartDelayMilliseconds = 25;
    public const int DefaultExperimentalTeleportDragReleaseDelayMilliseconds = 150;
    public const int DefaultExperimentalTeleportMaxSingleStepDistancePixels = 50;

    [ObservableProperty]
    private bool _useExperimentalTeleport;

    [ObservableProperty]
    private bool _useExperimentalTeleportAdvancedParameters;

    partial void OnUseExperimentalTeleportChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExperimentalTeleportAdvancedSwitchVisible));
        OnPropertyChanged(nameof(IsExperimentalTeleportAdvancedParametersVisible));
    }

    partial void OnUseExperimentalTeleportAdvancedParametersChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExperimentalTeleportAdvancedParametersVisible));
    }

    [ObservableProperty]
    private bool _experimentalTeleportDetailedLogs = false;

    [ObservableProperty]
    private double _experimentalTeleportDragDistanceCorrection = DefaultExperimentalTeleportDragDistanceCorrection;

    partial void OnExperimentalTeleportDragDistanceCorrectionChanged(double value)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            ExperimentalTeleportDragDistanceCorrection = DefaultExperimentalTeleportDragDistanceCorrection;
        }
    }

    [ObservableProperty]
    private int _experimentalTeleportDragStepIntervalMilliseconds = DefaultExperimentalTeleportDragStepIntervalMilliseconds;

    partial void OnExperimentalTeleportDragStepIntervalMillisecondsChanged(int value)
    {
        if (value <= 0)
        {
            ExperimentalTeleportDragStepIntervalMilliseconds = DefaultExperimentalTeleportDragStepIntervalMilliseconds;
        }
    }

    [ObservableProperty]
    private int _experimentalTeleportStateRecognitionIntervalMilliseconds = DefaultExperimentalTeleportStateRecognitionIntervalMilliseconds;

    partial void OnExperimentalTeleportStateRecognitionIntervalMillisecondsChanged(int value)
    {
        if (value <= 0)
        {
            ExperimentalTeleportStateRecognitionIntervalMilliseconds = DefaultExperimentalTeleportStateRecognitionIntervalMilliseconds;
        }
    }

    [ObservableProperty]
    private int _experimentalTeleportStateRecognitionInitialDelayMilliseconds = DefaultExperimentalTeleportStateRecognitionInitialDelayMilliseconds; // 首次轮询前等待，单位：ms

    partial void OnExperimentalTeleportStateRecognitionInitialDelayMillisecondsChanged(int value)
    {
        if (value <= 0)
        {
            ExperimentalTeleportStateRecognitionInitialDelayMilliseconds = DefaultExperimentalTeleportStateRecognitionInitialDelayMilliseconds;
        }
    }

    [ObservableProperty]
    private int _experimentalTeleportStateTransitionTimeoutMilliseconds = DefaultExperimentalTeleportStateTransitionTimeoutMilliseconds;

    partial void OnExperimentalTeleportStateTransitionTimeoutMillisecondsChanged(int value)
    {
        if (value <= 0)
        {
            ExperimentalTeleportStateTransitionTimeoutMilliseconds = DefaultExperimentalTeleportStateTransitionTimeoutMilliseconds;
        }
    }

    [ObservableProperty]
    private int _experimentalTeleportMapOpenTimeoutMilliseconds = DefaultExperimentalTeleportMapOpenTimeoutMilliseconds;

    partial void OnExperimentalTeleportMapOpenTimeoutMillisecondsChanged(int value)
    {
        if (value <= 0)
        {
            ExperimentalTeleportMapOpenTimeoutMilliseconds = DefaultExperimentalTeleportMapOpenTimeoutMilliseconds;
        }
    }

    [ObservableProperty]
    private int _experimentalTeleportMapOpenRepressIntervalMilliseconds = DefaultExperimentalTeleportMapOpenRepressIntervalMilliseconds;

    partial void OnExperimentalTeleportMapOpenRepressIntervalMillisecondsChanged(int value)
    {
        if (value <= 0)
        {
            ExperimentalTeleportMapOpenRepressIntervalMilliseconds = DefaultExperimentalTeleportMapOpenRepressIntervalMilliseconds;
        }
    }

    [ObservableProperty]
    private int _experimentalTeleportDragStartDelayMilliseconds = DefaultExperimentalTeleportDragStartDelayMilliseconds;

    partial void OnExperimentalTeleportDragStartDelayMillisecondsChanged(int value)
    {
        if (value <= 0)
        {
            ExperimentalTeleportDragStartDelayMilliseconds = DefaultExperimentalTeleportDragStartDelayMilliseconds;
        }
    }

    [ObservableProperty]
    private int _experimentalTeleportDragReleaseDelayMilliseconds = DefaultExperimentalTeleportDragReleaseDelayMilliseconds;

    partial void OnExperimentalTeleportDragReleaseDelayMillisecondsChanged(int value)
    {
        if (value <= 0)
        {
            ExperimentalTeleportDragReleaseDelayMilliseconds = DefaultExperimentalTeleportDragReleaseDelayMilliseconds;
        }
    }

    [ObservableProperty]
    private int _experimentalTeleportMaxSingleStepDistancePixels = DefaultExperimentalTeleportMaxSingleStepDistancePixels;

    partial void OnExperimentalTeleportMaxSingleStepDistancePixelsChanged(int value)
    {
        if (value <= 0)
        {
            ExperimentalTeleportMaxSingleStepDistancePixels = DefaultExperimentalTeleportMaxSingleStepDistancePixels;
        }
    }

    [ObservableProperty]
    private bool _mapZoomEnabled = true; // 地图缩放开关

    [ObservableProperty]
    private bool _mapDragUseRelativeMove = false; // 大地图拖动使用相对鼠标移动

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
    private double _teleportOperationDelayMultiplier = 1.0d;

    [JsonIgnore]
    public int TeleportOperationDelayMilliseconds
    {
        get
        {
            if (!double.IsFinite(TeleportOperationDelayMultiplier) || TeleportOperationDelayMultiplier <= 0)
            {
                return 1;
            }

            var milliseconds = DefaultTeleportOperationDelayMilliseconds * TeleportOperationDelayMultiplier;
            return milliseconds >= int.MaxValue
                ? int.MaxValue
                : Math.Max(1, (int)Math.Round(milliseconds));
        }
    }

    [JsonIgnore]
    public double TeleportOperationDelayPercentage => TeleportOperationDelayMultiplier * 100d;

    partial void OnTeleportOperationDelayMultiplierChanged(double value)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            TeleportOperationDelayMultiplier = 1.0d;
            return;
        }

        OnPropertyChanged(nameof(TeleportOperationDelayMilliseconds));
        OnPropertyChanged(nameof(TeleportOperationDelayPercentage));
    }

    /// <summary>
    /// 旧配置迁移字段：原先保存的是传送操作间隔毫秒数，现在迁移为倍率。
    /// </summary>
    [JsonPropertyName("teleportOperationDelayMilliseconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int LegacyTeleportOperationDelayMilliseconds
    {
        get => 0;
        set
        {
            if (value > 0)
            {
                TeleportOperationDelayMultiplier = value / (double)DefaultTeleportOperationDelayMilliseconds;
            }
        }
    }

    /// <summary>
    /// 旧配置迁移字段：原先只控制鼠标拖图步进间隔，现在迁移到统一的传送操作间隔。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int StepIntervalMilliseconds
    {
        get => 0;
        set
        {
            if (value == 0)
            {
                return;
            }

            TeleportOperationDelayMultiplier = value / (double)DefaultTeleportOperationDelayMilliseconds;
        }
    }
    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(1.0, 6.0)]
    private double _maxZoomLevel = 5.0; // 最大缩放等级

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

    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(50, 500)]
    private double _tolerance = 200; // 允许的移动误差

    [ObservableProperty]
    [NotifyDataErrorInfo] 
    [Range(10, 500)]
    private int _maxIterations = 30; // 移动最大次数

    [ObservableProperty]
    private double _mapScaleFactor = 2.361;  // 游戏坐标和 mapZoomLevel=1 时的像素比例因子。
    
    [ObservableProperty]
    [property: JsonIgnore]
    private double _precisionThreshold = 0.05;

    [JsonIgnore]
    public bool IsExperimentalTeleportAdvancedSwitchVisible => UseExperimentalTeleport;

    [JsonIgnore]
    public bool IsExperimentalTeleportAdvancedParametersVisible =>
        UseExperimentalTeleport && UseExperimentalTeleportAdvancedParameters;

    public double GetEffectiveExperimentalTeleportDragDistanceCorrection()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportDragDistanceCorrection
            : DefaultExperimentalTeleportDragDistanceCorrection;
    }

    public int GetEffectiveExperimentalTeleportMaxSingleStepDistancePixels()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportMaxSingleStepDistancePixels
            : DefaultExperimentalTeleportMaxSingleStepDistancePixels;
    }

    public int GetEffectiveExperimentalTeleportDragStepIntervalMilliseconds()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportDragStepIntervalMilliseconds
            : ScaleExperimentalTeleportDelay(3d, 1);
    }

    public int GetEffectiveExperimentalTeleportStateRecognitionIntervalMilliseconds()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportStateRecognitionIntervalMilliseconds
            : ScaleExperimentalTeleportDelay(50d, 10);
    }

    public int GetEffectiveExperimentalTeleportStateRecognitionInitialDelayMilliseconds()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportStateRecognitionInitialDelayMilliseconds
            : ScaleExperimentalTeleportDelay(100d, 20);
    }

    public int GetEffectiveExperimentalTeleportStateTransitionTimeoutMilliseconds()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportStateTransitionTimeoutMilliseconds
            : ScaleExperimentalTeleportDelay(500d, 250);
    }

    public int GetEffectiveExperimentalTeleportMapOpenTimeoutMilliseconds()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportMapOpenTimeoutMilliseconds
            : ScaleExperimentalTeleportDelay(5000d, 2500);
    }

    public int GetEffectiveExperimentalTeleportMapOpenRepressIntervalMilliseconds()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportMapOpenRepressIntervalMilliseconds
            : ScaleExperimentalTeleportDelay(2000d, 750);
    }

    public int GetEffectiveExperimentalTeleportDragStartDelayMilliseconds()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportDragStartDelayMilliseconds
            : ScaleExperimentalTeleportDelay(25d, 15);
    }

    public int GetEffectiveExperimentalTeleportDragReleaseDelayMilliseconds()
    {
        return UseExperimentalTeleportAdvancedParameters
            ? ExperimentalTeleportDragReleaseDelayMilliseconds
            : ScaleExperimentalTeleportDelay(150d, 75);
    }

    public bool IsExperimentalTeleportDetailedLoggingEnabled =>
        UseExperimentalTeleportAdvancedParameters && ExperimentalTeleportDetailedLogs;

    private int ScaleExperimentalTeleportDelay(double baseMilliseconds, int minimumMilliseconds)
    {
        var multiplier = TeleportOperationDelayMultiplier;
        if (!double.IsFinite(multiplier))
        {
            multiplier = 1d;
        }

        var scaled = Math.Ceiling(baseMilliseconds * multiplier);
        if (!double.IsFinite(scaled) || scaled >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return Math.Max(minimumMilliseconds, (int)scaled);
    }
}
