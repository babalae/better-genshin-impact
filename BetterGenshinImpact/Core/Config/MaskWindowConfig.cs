using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     遮罩窗口配置
/// </summary>
[Serializable]
public partial class MaskWindowConfig : ObservableObject
{
    public const string DefaultOverlayWindowBackgroundColor = "#01000000";
    public const string DefaultWineOverlayBackgroundColor = "#11000000";
    public const string DefaultTransparentColor = "#00000000";
    public const string DefaultPanelBorderColor = "#33000000";
    public const string DefaultLogTextColor = "LightGray";
    public const string DefaultStatusDisabledTextColor = "LightGray";
    public const string DefaultStatusEnabledTextColor = "LightGreen";
    public const string DefaultMetricsTextColor = "LightGray";
    public const string DefaultDirectionTextColor = "White";
    public const string DefaultShadowColor = "#FF000000";
    public const string DefaultStatusShadowColor = "LightGray";
    public const string DefaultRecognitionStrokeColor = "Red";
    public const string DefaultRecognitionTextColor = "Black";
    public const string DefaultOverlayMonoFontFamily = "Cascadia Mono, Consolas, Courier New, monospace, /Resources/Fonts/MiSans-Regular.ttf#MiSans";

    // 指标栏布局和遮罩里其它元素一样按 1920x1080 折算比例保存，默认放在状态栏/日志上方以避开游戏底部 UI。
    public const double DefaultMetricsLeftRatio = 20.0 / 1920;
    public const double DefaultMetricsTopRatio = 744.0 / 1080;
    public const double DefaultMetricsWidthRatio = 477.0 / 1920;
    public const double DefaultMetricsHeightRatio = 58.0 / 1080;

    // 这些是开发评审过程中曾下发过的默认布局；用户没有手动调整时迁移到最新默认值，避免旧默认继续挡住游戏 UI。
    private static readonly (double Left, double Top, double Width, double Height)[] LegacyMetricsLayouts =
    [
        (4.0 / 1920, 4.0 / 1080, 720.0 / 1920, 42.0 / 1080),
        (600.0 / 1920, 16.0 / 1080, 720.0 / 1920, 42.0 / 1080),
        (20.0 / 1920, 724.0 / 1080, 760.0 / 1920, 58.0 / 1080),
        (20.0 / 1920, 724.0 / 1080, 760.0 / 1920, 42.0 / 1080),
        (20.0 / 1920, 760.0 / 1080, 477.0 / 1920, 42.0 / 1080),
        (20.0 / 1920, 760.0 / 1080, 477.0 / 1920, 58.0 / 1080)
    ];

    /// <summary>
    ///     方位提示是否启用
    /// </summary>
    [ObservableProperty]
    private bool _directionsEnabled;

    /// <summary>
    ///     是否在遮罩窗口上显示识别结果
    /// </summary>
    [ObservableProperty]
    private bool _displayRecognitionResultsOnMask = true;

    /// <summary>
    ///     是否启用遮罩窗口
    /// </summary>
    [ObservableProperty]
    private bool _maskEnabled = true;

    ///// <summary>
    ///// 显示遮罩窗口边框
    ///// </summary>
    //[ObservableProperty] private bool _showMaskBorder = false;

    /// <summary>
    ///     显示日志窗口
    /// </summary>
    [ObservableProperty]
    private bool _showLogBox = true;

    /// <summary>
    ///     显示状态指示
    /// </summary>
    [ObservableProperty]
    private bool _showStatus = true;

    /// <summary>
    ///     UID遮盖是否启用
    /// </summary>
    [ObservableProperty]
    private bool _uidCoverEnabled;

    /// <summary>
    ///     1080p下UID遮盖的位置与大小
    /// </summary>
    [NonSerialized]
    public static readonly Rect UidCoverRightBottomRect = new(1920 - 1685, 1080 - 1053, 178, 22);

    /// <summary>
    /// 显示FPS
    /// </summary>
    [ObservableProperty]
    private bool _showFps = false;

    /// <summary>
    /// 显示遮罩指标栏
    /// </summary>
    [ObservableProperty]
    private bool _showOverlayMetrics = false;

    // 配置文件里使用 string key 便于兼容旧版本，读取后由 EnsureOverlayMetricItems 约束回固定枚举集合。
    public Dictionary<string, bool> OverlayMetricItems { get; set; } = OverlayMetricItemDefaults.CreateDefaultItems();

    /// <summary>
    /// 遮罩文本透明度 (0.0-1.0)
    /// </summary>
    [ObservableProperty]
    private double _textOpacity = 1.0;

    [ObservableProperty]
    private string _overlayWindowBackgroundColor = DefaultOverlayWindowBackgroundColor;

    [ObservableProperty]
    private string _wineOverlayBackgroundColor = DefaultWineOverlayBackgroundColor;

    [ObservableProperty]
    private string _logPanelBackgroundColor = DefaultTransparentColor;

    [ObservableProperty]
    private string _logPanelBorderColor = DefaultPanelBorderColor;

    [ObservableProperty]
    private double _logPanelBorderThickness = 0;

    [ObservableProperty]
    private string _logTextColor = DefaultLogTextColor;

    [ObservableProperty]
    private string _logFontFamily = DefaultOverlayMonoFontFamily;

    [ObservableProperty]
    private double _logFontSize = 12;

    [ObservableProperty]
    private bool _logShadowEnabled = true;

    [ObservableProperty]
    private string _logShadowColor = DefaultShadowColor;

    [ObservableProperty]
    private double _logShadowOpacity = 0.4;

    [ObservableProperty]
    private double _logShadowBlurRadius = 4;

    [ObservableProperty]
    private string _statusPanelBackgroundColor = DefaultTransparentColor;

    [ObservableProperty]
    private string _statusPanelBorderColor = DefaultPanelBorderColor;

    [ObservableProperty]
    private double _statusPanelBorderThickness = 0;

    [ObservableProperty]
    private string _statusDisabledTextColor = DefaultStatusDisabledTextColor;

    [ObservableProperty]
    private string _statusEnabledTextColor = DefaultStatusEnabledTextColor;

    [ObservableProperty]
    private double _statusFontSize = 12;

    [ObservableProperty]
    private bool _statusShadowEnabled = true;

    [ObservableProperty]
    private string _statusShadowColor = DefaultStatusShadowColor;

    [ObservableProperty]
    private double _statusShadowOpacity = 0.4;

    [ObservableProperty]
    private double _statusShadowBlurRadius = 4;

    [ObservableProperty]
    private string _metricsPanelBackgroundColor = DefaultTransparentColor;

    [ObservableProperty]
    private string _metricsPanelBorderColor = DefaultTransparentColor;

    [ObservableProperty]
    private double _metricsPanelBorderThickness = 0;

    [ObservableProperty]
    private string _metricsTextColor = DefaultMetricsTextColor;

    [ObservableProperty]
    private string _metricsFontFamily = DefaultOverlayMonoFontFamily;

    [ObservableProperty]
    private double _metricsFontSize = 12;

    [ObservableProperty]
    private double _metricsLineHeight = 15;

    [ObservableProperty]
    private double _metricsItemWidth = 116;

    [ObservableProperty]
    private double _metricsNameColumnWidth = 68;

    [ObservableProperty]
    private bool _metricsShadowEnabled = true;

    [ObservableProperty]
    private string _metricsShadowColor = DefaultShadowColor;

    [ObservableProperty]
    private double _metricsShadowOpacity = 0.4;

    [ObservableProperty]
    private double _metricsShadowBlurRadius = 4;

    [ObservableProperty]
    private string _directionTextColor = DefaultDirectionTextColor;

    [ObservableProperty]
    private double _directionFontSize = 34;

    [ObservableProperty]
    private bool _directionShadowEnabled = true;

    [ObservableProperty]
    private string _directionShadowColor = DefaultShadowColor;

    [ObservableProperty]
    private double _directionShadowOpacity = 0.4;

    [ObservableProperty]
    private double _directionShadowBlurRadius = 8;

    [ObservableProperty]
    private bool _recognitionUseDrawableStyle = false;

    [ObservableProperty]
    private string _recognitionRectStrokeColor = DefaultRecognitionStrokeColor;

    [ObservableProperty]
    private double _recognitionRectStrokeThickness = 2;

    [ObservableProperty]
    private string _recognitionLineStrokeColor = DefaultRecognitionStrokeColor;

    [ObservableProperty]
    private double _recognitionLineStrokeThickness = 2;

    [ObservableProperty]
    private string _recognitionTextColor = DefaultRecognitionTextColor;

    [ObservableProperty]
    private double _recognitionTextFontSize = 36;

    [ObservableProperty]
    private bool _customHtmlMaskEnabled = false;

    [ObservableProperty]
    private bool _customHtmlMaskClickThrough = true;

    [ObservableProperty]
    private bool _customHtmlMaskAutoReloadOnSave = true;

    [ObservableProperty]
    private bool _overlayLayoutEditEnabled = false;

    [ObservableProperty]
    private double _logTextBoxLeftRatio = 20.0 / 1920;

    [ObservableProperty]
    private double _logTextBoxTopRatio = 822.0 / 1080;

    [ObservableProperty]
    private double _logTextBoxWidthRatio = 480.0 / 1920;

    [ObservableProperty]
    private double _logTextBoxHeightRatio = 188.0 / 1080;

    [ObservableProperty]
    private double _statusListLeftRatio = 20.0 / 1920;

    [ObservableProperty]
    private double _statusListTopRatio = 790.0 / 1080;

    [ObservableProperty]
    private double _statusListWidthRatio = 480.0 / 1920;

    [ObservableProperty]
    private double _statusListHeightRatio = 24.0 / 1080;

    [ObservableProperty]
    private double _metricsLeftRatio = DefaultMetricsLeftRatio;

    [ObservableProperty]
    private double _metricsTopRatio = DefaultMetricsTopRatio;

    [ObservableProperty]
    private double _metricsWidthRatio = DefaultMetricsWidthRatio;

    [ObservableProperty]
    private double _metricsHeightRatio = DefaultMetricsHeightRatio;

    public void ResetOverlayMetricsLayout()
    {
        MetricsLeftRatio = DefaultMetricsLeftRatio;
        MetricsTopRatio = DefaultMetricsTopRatio;
        MetricsWidthRatio = DefaultMetricsWidthRatio;
        MetricsHeightRatio = DefaultMetricsHeightRatio;
    }

    public void ResetOverlayStyle()
    {
        TextOpacity = 1.0;
        OverlayWindowBackgroundColor = DefaultOverlayWindowBackgroundColor;
        WineOverlayBackgroundColor = DefaultWineOverlayBackgroundColor;

        LogPanelBackgroundColor = DefaultTransparentColor;
        LogPanelBorderColor = DefaultPanelBorderColor;
        LogPanelBorderThickness = 0;
        LogTextColor = DefaultLogTextColor;
        LogFontFamily = DefaultOverlayMonoFontFamily;
        LogFontSize = 12;
        LogShadowEnabled = true;
        LogShadowColor = DefaultShadowColor;
        LogShadowOpacity = 0.4;
        LogShadowBlurRadius = 4;

        StatusPanelBackgroundColor = DefaultTransparentColor;
        StatusPanelBorderColor = DefaultPanelBorderColor;
        StatusPanelBorderThickness = 0;
        StatusDisabledTextColor = DefaultStatusDisabledTextColor;
        StatusEnabledTextColor = DefaultStatusEnabledTextColor;
        StatusFontSize = 12;
        StatusShadowEnabled = true;
        StatusShadowColor = DefaultStatusShadowColor;
        StatusShadowOpacity = 0.4;
        StatusShadowBlurRadius = 4;

        MetricsPanelBackgroundColor = DefaultTransparentColor;
        MetricsPanelBorderColor = DefaultTransparentColor;
        MetricsPanelBorderThickness = 0;
        MetricsTextColor = DefaultMetricsTextColor;
        MetricsFontFamily = DefaultOverlayMonoFontFamily;
        MetricsFontSize = 12;
        MetricsLineHeight = 15;
        MetricsItemWidth = 116;
        MetricsNameColumnWidth = 68;
        MetricsShadowEnabled = true;
        MetricsShadowColor = DefaultShadowColor;
        MetricsShadowOpacity = 0.4;
        MetricsShadowBlurRadius = 4;

        DirectionTextColor = DefaultDirectionTextColor;
        DirectionFontSize = 34;
        DirectionShadowEnabled = true;
        DirectionShadowColor = DefaultShadowColor;
        DirectionShadowOpacity = 0.4;
        DirectionShadowBlurRadius = 8;

        RecognitionUseDrawableStyle = false;
        RecognitionRectStrokeColor = DefaultRecognitionStrokeColor;
        RecognitionRectStrokeThickness = 2;
        RecognitionLineStrokeColor = DefaultRecognitionStrokeColor;
        RecognitionLineStrokeThickness = 2;
        RecognitionTextColor = DefaultRecognitionTextColor;
        RecognitionTextFontSize = 36;
    }

    public void MigrateLegacyOverlayMetricsLayout()
    {
        if (LegacyMetricsLayouts.Any(layout =>
                IsSameRatio(MetricsLeftRatio, layout.Left)
                && IsSameRatio(MetricsTopRatio, layout.Top)
                && IsSameRatio(MetricsWidthRatio, layout.Width)
                && IsSameRatio(MetricsHeightRatio, layout.Height)))
        {
            ResetOverlayMetricsLayout();
        }
    }

    private static bool IsSameRatio(double left, double right)
    {
        return Math.Abs(left - right) < 0.0000001;
    }

    public void EnsureOverlayMetricItems()
    {
        // 旧配置可能缺少新指标或残留废弃指标，这里统一补默认项并移除非法 key，避免 UI 渲染任意字符串。
        OverlayMetricItems ??= [];

        // TriggerInterval 第一版展示的是配置值，现已替换为 PeakProcessingCost；保留用户原来的勾选状态。
        const string legacyTriggerIntervalKey = "TriggerInterval";
        var peakProcessingCostKey = OverlayMetricItem.PeakProcessingCost.ToString();
        if (OverlayMetricItems.TryGetValue(legacyTriggerIntervalKey, out var legacyEnabled)
            && !OverlayMetricItems.ContainsKey(peakProcessingCostKey))
        {
            OverlayMetricItems[peakProcessingCostKey] = legacyEnabled;
        }

        foreach (var item in OverlayMetricItemDefaults.AllItems)
        {
            var key = item.ToString();
            if (!OverlayMetricItems.ContainsKey(key))
            {
                OverlayMetricItems[key] = OverlayMetricItemDefaults.IsEnabledByDefault(item);
            }
        }

        var validKeys = OverlayMetricItemDefaults.AllItems.Select(item => item.ToString()).ToHashSet();
        foreach (var key in OverlayMetricItems.Keys.Where(key => !validKeys.Contains(key)).ToList())
        {
            OverlayMetricItems.Remove(key);
        }

        if (ShowFps)
        {
            ShowOverlayMetrics = true;
            foreach (var item in OverlayMetricItemDefaults.AllItems)
            {
                OverlayMetricItems[item.ToString()] = item == OverlayMetricItem.GameFps;
            }

            ShowFps = false;
        }
    }

    public bool IsOverlayMetricEnabled(OverlayMetricItem item)
    {
        return OverlayMetricItems != null && OverlayMetricItems.TryGetValue(item.ToString(), out var enabled)
            ? enabled
            : OverlayMetricItemDefaults.IsEnabledByDefault(item);
    }

    public void SetOverlayMetricEnabled(OverlayMetricItem item, bool enabled)
    {
        EnsureOverlayMetricItems();
        OverlayMetricItems[item.ToString()] = enabled;
        OnPropertyChanged(nameof(OverlayMetricItems));
    }
}
