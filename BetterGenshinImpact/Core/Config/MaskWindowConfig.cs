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
    public const string DefaultLogColorTheme = "Literate";
    public const string DefaultStatusDisabledTextColor = "LightGray";
    public const string DefaultStatusEnabledTextColor = "LightGreen";
    public const string DefaultMetricsTextColor = "LightGray";
    public const string DefaultDirectionTextColor = "White";
    public const string DefaultShadowColor = "#FF000000";
    public const string DefaultStatusShadowColor = "LightGray";
    public const string DefaultRecognitionStrokeColor = "Red";
    public const string DefaultRecognitionTextColor = "Black";
    public const string DefaultOverlayMonoFontFamily = "Cascadia Mono, Consolas, Courier New, monospace, /Resources/Fonts/MiSans-Regular.ttf#MiSans";
    public const int MinCrosshairLineWidth = 1;
    public const int MaxCrosshairLineWidth = 100;
    public const int MinCrosshairSize = 1;
    public const int MaxCrosshairSize = 1000;
    public const int MinCrosshairGap = 0;
    public const int MaxCrosshairGap = 1000;

    // 快捷键速查条（HotkeyBar）：与状态栏同高、右下角与状态栏对称。
    // 状态栏 TopRatio 上移至 766/1080 以预留换行空间，速查条与之对齐。
    public const string DefaultHotkeyBarTextColor = "LightGray";
    public const double DefaultHotkeyBarLeftRatio = 1420.0 / 1920;
    public const double DefaultHotkeyBarTopRatio = 766.0 / 1080;
    public const double DefaultHotkeyBarWidthRatio = 480.0 / 1920;
    public const double DefaultHotkeyBarHeightRatio = 58.0 / 1080;

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
    ///     准星是否启用
    /// </summary>
    [ObservableProperty]
    private bool _crosshairEnabled;

    /// <summary>
    ///     准星类型
    /// </summary>
    [ObservableProperty]
    private CrosshairType _crosshairType = CrosshairType.Crosshair;

    /// <summary>
    ///     准星颜色（十六进制）
    /// </summary>
    [ObservableProperty]
    private string _crosshairColor = "#FFFFFF";

    /// <summary>
    ///     准星线宽
    /// </summary>
    [ObservableProperty]
    private int _crosshairLineWidth = 4;

    /// <summary>
    ///     准星大小
    /// </summary>
    [ObservableProperty]
    private int _crosshairSize = 30;

    /// <summary>
    ///     中心点与十字线的间隔（仅 DotCrosshair 类型）
    /// </summary>
    [ObservableProperty]
    private int _crosshairGap = 10;

    partial void OnCrosshairLineWidthChanged(int value)
    {
        var clampedValue = Math.Clamp(value, MinCrosshairLineWidth, MaxCrosshairLineWidth);
        if (value != clampedValue)
        {
            CrosshairLineWidth = clampedValue;
        }
    }

    partial void OnCrosshairSizeChanged(int value)
    {
        var clampedValue = Math.Clamp(value, MinCrosshairSize, MaxCrosshairSize);
        if (value != clampedValue)
        {
            CrosshairSize = clampedValue;
        }
    }

    partial void OnCrosshairGapChanged(int value)
    {
        var clampedValue = Math.Clamp(value, MinCrosshairGap, MaxCrosshairGap);
        if (value != clampedValue)
        {
            CrosshairGap = clampedValue;
        }
    }

    /// <summary>
    ///     自定义准星图片路径
    /// </summary>
    [ObservableProperty]
    private string? _crosshairImagePath;

    /// <summary>
    ///     自定义图片缩放方式
    /// </summary>
    [ObservableProperty]
    private CrosshairScaleMode _crosshairScaleMode = CrosshairScaleMode.Original;

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

    /// <summary>
    /// 是否启用遮罩 UI 缩放。关闭后直接使用各遮罩元素的基础尺寸。
    /// </summary>
    [ObservableProperty]
    private bool _overlayScalingEnabled = false;

    /// <summary>
    /// 遮罩 UI 缩放率 (0.5-3.0)，叠加到日志、状态和 FPS 的基础字号上。
    /// </summary>
    [ObservableProperty]
    private double _logFontScale = 1.0;

    /// <summary>
    /// 指标栏缩放率 (0.5-3.0)，叠加到指标栏的基础字号和布局尺寸上。
    /// 独立于遮罩 UI 缩放率。
    /// </summary>
    [ObservableProperty]
    private double _metricsFontScale = 1.0;

    /// <summary>遮罩日志缩放率允许的最小值。</summary>
    public const double MinLogFontScale = 0.5;

    /// <summary>遮罩日志缩放率允许的最大值。</summary>
    public const double MaxLogFontScale = 3.0;

    /// <summary>
    /// 纯函数：把任意缩放率夹取到 [MinLogFontScale, MaxLogFontScale]。无副作用，供 PBT。
    /// NaN 视为非法，回落到 1.0。
    /// </summary>
    public static double ComputeClampedScale(double scale)
    {
        if (double.IsNaN(scale))
        {
            return 1.0;
        }
        return Math.Clamp(scale, MinLogFontScale, MaxLogFontScale);
    }

    /// <summary>
    /// 纯函数：计算遮罩 UI 实际渲染尺寸。
    /// 禁用缩放时直接返回有效的基础尺寸。
    /// = baseSize × clamp(scale) × scaleTo1080pRatio / displayDpiScale，AwayFromZero 四舍五入。
    /// 乘以 ScaleTo1080PRatio 适配不同游戏分辨率，除以 DisplayDpiScale 抵消 WPF 自动 DPI 缩放。
    /// </summary>
    public static double ComputeEffectiveSize(double baseSize, double scale, double scaleTo1080pRatio,
        double displayDpiScale, bool scalingEnabled)
    {
        var safeBaseSize = double.IsFinite(baseSize) && baseSize > 0 ? baseSize : 1.0;
        if (!scalingEnabled)
        {
            return safeBaseSize;
        }

        var resolutionScale = double.IsFinite(scaleTo1080pRatio) ? Math.Max(scaleTo1080pRatio, 0.1) : 1.0;
        var dpiScale = double.IsFinite(displayDpiScale) ? Math.Max(displayDpiScale, 0.1) : 1.0;
        var result = Math.Round(safeBaseSize * ComputeClampedScale(scale) * resolutionScale / dpiScale,
            MidpointRounding.AwayFromZero);
        return Math.Max(result, 1.0);
    }

    /// <summary>
    /// 缩放率变更钩子：仅当传入值越界时回写夹取后的值（防 setter 递归）。
    /// </summary>
    partial void OnLogFontScaleChanged(double value)
    {
        var clamped = ComputeClampedScale(value);
        if (value != clamped)
        {
            LogFontScale = clamped;
        }
    }

    /// <summary>
    /// 指标栏缩放率变更钩子：仅当传入值越界时回写夹取后的值（防 setter 递归）。
    /// </summary>
    partial void OnMetricsFontScaleChanged(double value)
    {
        var clamped = ComputeClampedScale(value);
        if (value != clamped)
        {
            MetricsFontScale = clamped;
        }
    }

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

    /// <summary>
    /// 遮罩日志配色主题：Literate（默认，按级别着色）/ Grayscale（灰阶）/ Colored（高对比彩色）。
    /// 日志管道在启动时构建，修改后需重启软件生效。
    /// </summary>
    [ObservableProperty]
    private string _logColorTheme = DefaultLogColorTheme;

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

    // 上移自 790/1080：状态栏改为可换行、高度自适应后，需预留约 2 行增长空间以避开日志框（822/1080）
    [ObservableProperty]
    private double _statusListTopRatio = 766.0 / 1080;

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

    /// <summary>
    /// 是否显示快捷键速查条（遮罩右下角的可拖拽面板）
    /// </summary>
    [ObservableProperty]
    private bool _showHotkeyBar = true;

    /// <summary>
    /// 状态栏功能项是否显示已绑定的快捷键徽章
    /// </summary>
    [ObservableProperty]
    private bool _statusHotkeyBadgeEnabled = true;

    /// <summary>
    /// 速查条是否排除状态栏已显示的那 5 个实时任务开关项（去重），默认关闭即两处都显示
    /// </summary>
    [ObservableProperty]
    private bool _hotkeyBarExcludeStatusItems = false;

    [ObservableProperty]
    private double _hotkeyBarLeftRatio = DefaultHotkeyBarLeftRatio;

    [ObservableProperty]
    private double _hotkeyBarTopRatio = DefaultHotkeyBarTopRatio;

    [ObservableProperty]
    private double _hotkeyBarWidthRatio = DefaultHotkeyBarWidthRatio;

    [ObservableProperty]
    private double _hotkeyBarHeightRatio = DefaultHotkeyBarHeightRatio;

    [ObservableProperty]
    private string _hotkeyBarTextColor = DefaultHotkeyBarTextColor;

    [ObservableProperty]
    private double _hotkeyBarFontSize = 12;

    // 配置文件里使用 string key（HotKeyConfig 属性名），读取后由 EnsureOverlayHotkeyItems 约束回固定集合。
    public Dictionary<string, bool> OverlayHotkeyItems { get; set; } = OverlayHotkeyItemDefaults.CreateDefaultItems();

    public void ResetOverlayMetricsLayout()
    {
        MetricsLeftRatio = DefaultMetricsLeftRatio;
        MetricsTopRatio = DefaultMetricsTopRatio;
        MetricsWidthRatio = DefaultMetricsWidthRatio;
        MetricsHeightRatio = DefaultMetricsHeightRatio;
    }

    public void ResetOverlayHotkeyLayout()
    {
        HotkeyBarLeftRatio = DefaultHotkeyBarLeftRatio;
        HotkeyBarTopRatio = DefaultHotkeyBarTopRatio;
        HotkeyBarWidthRatio = DefaultHotkeyBarWidthRatio;
        HotkeyBarHeightRatio = DefaultHotkeyBarHeightRatio;
    }

    public void ResetOverlayStyle()
    {
        TextOpacity = 1.0;
        OverlayScalingEnabled = true;
        LogFontScale = 1.0;
        MetricsFontScale = 1.0;
        OverlayWindowBackgroundColor = DefaultOverlayWindowBackgroundColor;
        WineOverlayBackgroundColor = DefaultWineOverlayBackgroundColor;

        LogPanelBackgroundColor = DefaultTransparentColor;
        LogPanelBorderColor = DefaultPanelBorderColor;
        LogPanelBorderThickness = 0;
        LogTextColor = DefaultLogTextColor;
        LogFontFamily = DefaultOverlayMonoFontFamily;
        LogFontSize = 12;
        LogColorTheme = DefaultLogColorTheme;
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

        ShowHotkeyBar = true;
        StatusHotkeyBadgeEnabled = true;
        HotkeyBarExcludeStatusItems = false;
        HotkeyBarTextColor = DefaultHotkeyBarTextColor;
        HotkeyBarFontSize = 12;
        ResetOverlayHotkeyLayout();
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
            OverlayMetricItems[OverlayMetricItem.GameFps.ToString()] = true;
            ShowFps = false;
            OnPropertyChanged(nameof(OverlayMetricItems));
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

    /// <summary>
    /// 补齐缺少的快捷键项、移除非法 key（防止配置被篡改导致 UI 渲染任意字符串）。
    /// key 为 HotKeyConfig 上的快捷键属性名字符串。
    /// </summary>
    public void EnsureOverlayHotkeyItems()
    {
        OverlayHotkeyItems ??= [];

        foreach (var item in OverlayHotkeyItemDefaults.AllItems)
        {
            if (!OverlayHotkeyItems.ContainsKey(item))
            {
                OverlayHotkeyItems[item] = OverlayHotkeyItemDefaults.IsEnabledByDefault(item);
            }
        }

        var validKeys = OverlayHotkeyItemDefaults.AllItems.ToHashSet();
        foreach (var key in OverlayHotkeyItems.Keys.Where(key => !validKeys.Contains(key)).ToList())
        {
            OverlayHotkeyItems.Remove(key);
        }
    }

    public bool IsOverlayHotkeyEnabled(string configPropertyName)
    {
        return OverlayHotkeyItems != null && OverlayHotkeyItems.TryGetValue(configPropertyName, out var enabled)
            ? enabled
            : OverlayHotkeyItemDefaults.IsEnabledByDefault(configPropertyName);
    }

    public void SetOverlayHotkeyEnabled(string configPropertyName, bool enabled)
    {
        EnsureOverlayHotkeyItems();
        OverlayHotkeyItems[configPropertyName] = enabled;
        OnPropertyChanged(nameof(OverlayHotkeyItems));
    }
}

/// <summary>
///     准星类型
/// </summary>
public enum CrosshairType
{
    Crosshair,
    Diagonal,
    Dot,
    DotCrosshair,
    Custom
}

/// <summary>
///     自定义准星图片缩放方式
/// </summary>
public enum CrosshairScaleMode
{
    Original,
    Fit
}
