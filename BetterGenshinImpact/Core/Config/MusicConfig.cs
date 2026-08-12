using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class MusicConfig : ObservableObject
{
    /// <summary>
    /// 曲谱扫描根目录
    /// </summary>
    [ObservableProperty]
    private string _musicFolder = string.Empty;

    /// <summary>
    /// 默认使用后台 PostMessage 演奏
    /// </summary>
    [ObservableProperty]
    private string _inputMode = "BackgroundPostMessage";

    /// <summary>
    /// 播放模式
    /// </summary>
    [ObservableProperty]
    private string _playbackMode = "Sequential";

    /// <summary>
    /// 播放速度
    /// </summary>
    [ObservableProperty]
    private double _speed = 1.0;

    /// <summary>
    /// 默认输出乐器档案
    /// </summary>
    [ObservableProperty]
    private string _selectedInstrumentProfile = "风物之诗琴";
}
