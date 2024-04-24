using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using Point = System.Windows.Point;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     遮罩窗口配置
/// </summary>
[Serializable]
public partial class MaskWindowConfig : ObservableObject
{
    /// <summary>
    ///     控件是否锁定（拖拽移动等）
    /// </summary>
    [ObservableProperty]
    private bool _controlLocked = true;

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
    ///     日志窗口位置与大小
    /// </summary>
    [ObservableProperty]
    private Rect _logBoxLocation;

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
    public Rect UidCoverRect { get; set; } = new(1690, 1052, 173, 22);

    public Point EastPoint { get; set; } = new(274, 109);
    public Point SouthPoint { get; set; } = new(150, 233);
    public Point WestPoint { get; set; } = new(32, 109);
    public Point NorthPoint { get; set; } = new(150, -9);
}
