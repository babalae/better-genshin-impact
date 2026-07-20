using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     遮罩窗口配置
/// </summary>
[Serializable]
public partial class MaskWindowConfig : ObservableObject
{
    public const int MinCrosshairLineWidth = 1;
    public const int MaxCrosshairLineWidth = 100;
    public const int MinCrosshairSize = 1;
    public const int MaxCrosshairSize = 1000;
    public const int MinCrosshairGap = 0;
    public const int MaxCrosshairGap = 1000;

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
    /// 遮罩 UI 缩放率 (0.5-3.0)，1.0 = 基准字号 12。
    /// 变化时同步通知所有派生属性，使绑定的遮罩 UI 元素实时重算字号。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveFontSize))]
    private double _logFontScale = 1.0;

    /// <summary>
    /// 指标栏字体缩放率 (0.5-3.0)，1.0 = 基准字号 12。
    /// 独立于遮罩 UI 缩放率，仅控制指标栏（OverlayMetrics）的字体大小。
    /// 变化时同步通知派生属性，使绑定的指标栏实时重算字号。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetricsEffectiveFontSize))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsItemHeight))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsGridHeight))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsItemWidth))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsNameColumnWidth))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsLineHeight))]
    private double _metricsFontScale = 1.0;

    /// <summary>遮罩日志缩放率允许的最小值。</summary>
    public const double MinLogFontScale = 0.5;

    /// <summary>遮罩日志缩放率允许的最大值。</summary>
    public const double MaxLogFontScale = 3.0;

    /// <summary>遮罩日志基准字号（缩放率 1.0 时的字号），保持历史硬编码值 12。</summary>
    public const double BaseLogFontSize = 12.0;

    /// <summary>
    /// 游戏分辨率相对于 1080P 的缩放比例，由 MaskWindowViewModel 在初始化时设置。
    /// 1920×1080 → 1.0, 3840×2160 → 2.0, 1280×720 → 0.67。
    /// 用于在不同游戏分辨率下保持日志相对游戏画面的大小一致。
    /// 不序列化到配置文件中，因为该值由运行时环境决定。
    /// 变化时同步通知 EffectiveFontSize 等派生属性，使绑定的遮罩 UI 元素实时重算字号。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveFontSize))]
    [NotifyPropertyChangedFor(nameof(MetricsEffectiveFontSize))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsItemHeight))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsGridHeight))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsItemWidth))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsNameColumnWidth))]
    [NotifyPropertyChangedFor(nameof(OverlayMetricsLineHeight))]
    private double _scaleTo1080PRatio = 1.0;

    /// <summary>
    /// 显示器 DPI 缩放因子（自动检测），从 DpiHelper.ScaleY 获取。
    /// 异常时回退到 1.0（100% DPI）。
    /// 只读计算属性，不序列化到配置文件中。
    /// </summary>
    public double DisplayDpiScale => ComputeDisplayDpiScale();

    /// <summary>
    /// 遮罩 UI 实际渲染字号（只读计算属性），
    /// = BaseLogFontSize × clamp(LogFontScale) × ScaleTo1080PRatio / DisplayDpiScale，
    /// 使用 MidpointRounding.AwayFromZero 四舍五入到整数像素。
    /// 乘以 ScaleTo1080PRatio 在不同游戏分辨率下保持日志相对游戏画面的大小一致，
    /// 除以 DisplayDpiScale 抵消 WPF 自动 DPI 缩放。
    /// XAML 中所有 UI 文本元素的 FontSize 绑定此属性。
    /// </summary>
    public double EffectiveFontSize => ComputeEffectiveFontSize(LogFontScale, ScaleTo1080PRatio, DisplayDpiScale);

    /// <summary>
    /// 指标栏实际渲染字号（只读计算属性），
    /// = BaseLogFontSize × clamp(MetricsFontScale) × ScaleTo1080PRatio / DisplayDpiScale，
    /// 使用 MidpointRounding.AwayFromZero 四舍五入到整数像素。
    /// 自动适配逻辑与 EffectiveFontSize 一致。
    /// XAML 中指标栏文本元素的 FontSize 绑定此属性。
    /// </summary>
    public double MetricsEffectiveFontSize => ComputeEffectiveFontSize(MetricsFontScale, ScaleTo1080PRatio, DisplayDpiScale);

    /// <summary>
    /// 指标栏每项宽度（跟随字体大小缩放）。
    /// 基准 116px（字号 12px 时），按 MetricsEffectiveFontSize / 12 比例缩放。
    /// </summary>
    public double OverlayMetricsItemWidth => ComputeOverlayMetricsLayoutSize(MetricsEffectiveFontSize, 116);

    /// <summary>
    /// 指标栏名称列宽度（跟随字体大小缩放）。
    /// 基准 68px（字号 12px 时），按 MetricsEffectiveFontSize / 12 比例缩放。
    /// </summary>
    public double OverlayMetricsNameColumnWidth => ComputeOverlayMetricsLayoutSize(MetricsEffectiveFontSize, 68);

    /// <summary>
    /// 指标栏文本行高（跟随字体大小缩放）。
    /// 基准 18px（字号 12px 时），按 MetricsEffectiveFontSize / 12 比例缩放。
    /// </summary>
    public double OverlayMetricsLineHeight => ComputeOverlayMetricsLayoutSize(MetricsEffectiveFontSize, 23);

    /// <summary>
    /// 指标栏容器 ItemHeight，跟随 MetricsEffectiveFontSize 缩放。
    /// 基准 20px（字号 12px 时），按 MetricsEffectiveFontSize / 12 比例缩放。
    /// </summary>
    public double OverlayMetricsItemHeight => ComputeOverlayMetricsLayoutSize(MetricsEffectiveFontSize, 20);

    /// <summary>
    /// 指标栏容器 Grid Height，跟随 MetricsEffectiveFontSize 缩放。
    /// 基准 20px（字号 12px 时），按 MetricsEffectiveFontSize / 12 比例缩放。
    /// </summary>
    public double OverlayMetricsGridHeight => ComputeOverlayMetricsLayoutSize(MetricsEffectiveFontSize, 20);

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
    /// 纯函数：把缩放率换算为实际字号 = BaseLogFontSize × clamp(scale)。无副作用，供 PBT。
    /// </summary>
    public static double ComputeLogFontSize(double scale)
    {
        return BaseLogFontSize * ComputeClampedScale(scale);
    }

    /// <summary>
    /// 纯函数：计算遮罩 UI 实际渲染字号。
    /// = BaseLogFontSize × clamp(scale) × scaleTo1080pRatio / displayDpiScale，AwayFromZero 四舍五入。
    /// 乘以 ScaleTo1080PRatio 适配不同游戏分辨率，除以 DisplayDpiScale 抵消 WPF 自动 DPI 缩放。
    /// </summary>
    public static double ComputeEffectiveFontSize(double scale, double scaleTo1080pRatio, double displayDpiScale)
    {
        var dpi = Math.Max(displayDpiScale, 0.1);
        var adjustedScale = ComputeClampedScale(scale) * Math.Max(scaleTo1080pRatio, 0.1) / dpi;
        return Math.Round(BaseLogFontSize * adjustedScale, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 纯函数：获取显示器 DPI 缩放因子。
    /// 从 DpiHelper.ScaleY 获取，异常时回退到 1.0。
    /// </summary>
    public static double ComputeDisplayDpiScale()
    {
        try
        {
            return DpiHelper.ScaleY;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>
    /// 纯函数：计算指标栏容器高度。
    /// 缩放率 ≥ 2.0 时返回 32，否则返回 20。
    /// </summary>
    public static double ComputeOverlayMetricsHeight(double scale)
    {
        return ComputeClampedScale(scale) >= 2.0 ? 32.0 : 20.0;
    }

    /// <summary>
    /// 纯函数：计算指标栏布局尺寸。
    /// = baseSize × fontSize / BaseLogFontSize，AwayFromZero 四舍五入。
    /// </summary>
    public static double ComputeOverlayMetricsLayoutSize(double fontSize, double baseSize)
    {
        return Math.Round(baseSize * fontSize / BaseLogFontSize, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 通知所有依赖 DPI 的派生属性重新计算。
    /// 在显示器 DPI 变化时由 MaskWindowViewModel 调用。
    /// EffectiveFontSize 依赖 DisplayDpiScale（用于抵消 WPF 自动 DPI 缩放），
    /// 因此需要通知它。
    /// </summary>
    public void NotifyDpiDependentProperties()
    {
        // EffectiveFontSize 依赖 DisplayDpiScale（用于抵消 WPF 自动 DPI 缩放）
        OnPropertyChanged(nameof(DisplayDpiScale));
        OnPropertyChanged(nameof(EffectiveFontSize));
        OnPropertyChanged(nameof(MetricsEffectiveFontSize));
        OnPropertyChanged(nameof(OverlayMetricsItemHeight));
        OnPropertyChanged(nameof(OverlayMetricsGridHeight));
        OnPropertyChanged(nameof(OverlayMetricsItemWidth));
        OnPropertyChanged(nameof(OverlayMetricsNameColumnWidth));
        OnPropertyChanged(nameof(OverlayMetricsLineHeight));
    }

    /// <summary>
    /// 缩放率变更钩子：仅当传入值越界时回写夹取后的值（防 setter 递归）。
    /// </summary>
    partial void OnLogFontScaleChanged(double value)
    {
        var clamped = ComputeClampedScale(value);
        if (Math.Abs(clamped - value) > 1e-9)
        {
            LogFontScale = clamped;
        }
    }

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
