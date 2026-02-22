using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     遮罩窗口配置
/// </summary>
[Serializable]
public partial class MaskWindowConfig : ObservableObject
{
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
    /// 遮罩文本透明度 (0.0-1.0)
    /// </summary>
    [ObservableProperty]
    private double _textOpacity = 1.0;

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
}
