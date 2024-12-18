using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class RecordConfig : ObservableObject
{
    /// <summary>
    /// 视角每移动1度，需要MouseMoveBy的距离
    /// 用作脚本记录度数后转化的鼠标移动距离
    /// </summary>
    [ObservableProperty]
    private double _angle2MouseMoveByX = 1.0;

    /// <summary>
    /// 视角每移动1度，需要DirectInput移动的单位
    /// </summary>
    [ObservableProperty]
    private double _angle2DirectInputX = 1.0;

    /// <summary>
    /// 图像识别记录相机视角朝向
    /// </summary>
    [ObservableProperty]
    private bool _isRecordCameraOrientation = false;
    
    /// <summary>
    /// 通过派蒙判断是否在主界面
    /// </summary>
    [ObservableProperty]
    private bool _paimonSwitchEnabled = false;
}
