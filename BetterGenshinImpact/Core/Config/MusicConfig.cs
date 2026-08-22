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
    /// 是否使用自定义 BPM 覆盖曲谱的基准速度
    /// </summary>
    [ObservableProperty]
    private bool _useCustomBpm;

    /// <summary>
    /// 自定义 BPM
    /// </summary>
    [ObservableProperty]
    private double _customBpm = 120;

    /// <summary>
    /// 演奏前是否自动切换到当前曲目的输出乐器
    /// </summary>
    [ObservableProperty]
    private bool _autoSwitchInstrument;

    /// <summary>
    /// 默认输出乐器档案
    /// </summary>
    [ObservableProperty]
    private string _selectedInstrumentProfile = "风物之诗琴";
}
