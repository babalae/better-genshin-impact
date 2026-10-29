using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 演奏模式的切换开关。与 <see cref="MusicConfig.PlayMode"/> 配合使用，
/// 使用字符串常量而不是枚举，避免 Newtonsoft 把枚举序号写进配置文件后无法回退。
/// </summary>
public static class MusicPlayModeNames
{
    /// <summary>自动演奏（默认，保持改造前的行为）</summary>
    public const string Auto = "Auto";

    /// <summary>引导式跟弹</summary>
    public const string Guided = "Guided";
}

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
    /// 演奏模式：Auto=自动演奏（BetterGI 自动按键），Guided=引导式跟弹（只显示引导圈，由玩家自己弹）
    /// </summary>
    [ObservableProperty]
    private string _playMode = MusicPlayModeNames.Auto;

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

    /// <summary>
    /// 引导式跟弹：每个段落包含的按键次数（同时按下的和弦算一次）
    /// </summary>
    [ObservableProperty]
    private int _guidedSegmentSize = 5;

    /// <summary>
    /// 引导式跟弹：是否在游戏窗口之上显示覆盖层
    /// </summary>
    [ObservableProperty]
    private bool _guidedOverlayEnabled = true;

    /// <summary>
    /// 引导式跟弹：只有游戏窗口处于前台时才接受按键判定
    /// </summary>
    [ObservableProperty]
    private bool _guidedRequireGameActive = true;

    /// <summary>
    /// 引导式跟弹：段内必须按「从左往右」的顺序弹。
    /// 开启时只有当前该弹的那一次按键算数，提前按了后面的键不计数（屏上会提示先按哪个）；
    /// 关掉就退回「段内任意顺序」——只要本段里还有没弹完的按键，按下它就算对。
    /// </summary>
    [ObservableProperty]
    private bool _guidedStrictOrder = true;

    /// <summary>
    /// 引导式跟弹：按错时闪烁提示的时长（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _guidedErrorFlashMilliseconds = 260;

    /// <summary>
    /// 引导式跟弹：玩家连续按错多少次后自动退出引导
    /// </summary>
    [ObservableProperty]
    private int _guidedMaxConsecutiveErrors = 0;

    /// <summary>
    /// 引导式跟弹：开始时先从游戏画面里自动识别琴键的实际位置（推荐开启）。
    /// 识别出来的位置会写回下面的键位参数，所以 1080P / 2K / 4K 都不用自己配；关掉就只用配置里的数字。
    /// </summary>
    [ObservableProperty]
    private bool _guidedAutoDetectKeys = true;

    /// <summary>
    /// 引导式跟弹：引导圈的键位参数（1080P 基准，横向按画面宽度、纵向按画面高度等比换算）
    /// </summary>
    [ObservableProperty]
    private MusicGuidedKeyLayoutConfig _guidedKeyLayout = new();
}

/// <summary>
/// 引导圈的键位参数。
///
/// 引导圈画在原神演奏界面那 21 个琴键的**实际位置**上，所以这里的数字就是照抄琴键的位置。
/// 单位是「1080P 基准」像素，实际使用时横向按 画面宽度/1920、纵向按 画面高度/1080 换算，
/// 所以 1080P、2K、4K（以及 16:10、21:9）都能自动适配，不需要按分辨率分别配：
///   BaseX / BaseY  = 最上一排（Q W E R T Y U）最左边那个键的圆心；
///   ColumnSpacing  = 同一排相邻键的圆心水平间距；
///   RowSpacing     = 相邻两排的圆心垂直间距；
///   RowIndent      = 每往下一排整体向右的偏移量（实测原神三排是上下对齐的，默认 0）。
/// 键位排布固定为原神演奏界面的三排：Q W E R T Y U（高音）/ A S D F G H J（中音）/ Z X C V B N M（低音）。
///
/// 默认值是照 2560×1440 的实机演奏界面截图量出 21 个琴键的圆心后换算到 1080P 的；
/// 另外每次开始引导前还会先用 <c>MusicKeyAutoDetector</c> 从游戏画面里自动量一遍琴键位置并写回这里，
/// 所以正常情况下打开就能套准。如果圈还是没套准，**引导过程中可以现场修正**：
/// Ctrl+R 重新识别键位；或按住 Ctrl 再按方向键整体移动、[ ] 调键距、, . 调排距、PgUp/PgDn 调圈大小
/// （Shift 加速、回车显示当前数值），调好的值立刻生效并自动写回配置。
/// </summary>
[Serializable]
public partial class MusicGuidedKeyLayoutConfig : ObservableObject
{
    /// <summary>
    /// 最上一排最左边那个键（Q）的圆心 X（1080P 基准）
    /// </summary>
    [ObservableProperty]
    private double _baseX = 458.6;

    /// <summary>
    /// 最上一排最左边那个键（Q）的圆心 Y（1080P 基准）
    /// </summary>
    [ObservableProperty]
    private double _baseY = 674.9;

    /// <summary>
    /// 同一排相邻键的圆心水平间距（1080P 基准）
    /// </summary>
    [ObservableProperty]
    private double _columnSpacing = 167.4;

    /// <summary>
    /// 相邻两排的圆心垂直间距（1080P 基准）
    /// </summary>
    [ObservableProperty]
    private double _rowSpacing = 134.5;

    /// <summary>
    /// 每往下一排整体向右的偏移量（1080P 基准；原神三排是上下对齐的，所以默认 0）
    /// </summary>
    [ObservableProperty]
    private double _rowIndent = 0;

    /// <summary>
    /// 引导圈半径（1080P 基准像素）。默认 52 = 实测琴键按钮的半径，圈正好套在琴键边缘上。
    /// </summary>
    [ObservableProperty]
    private double _ringRadius = 52;

    /// <summary>
    /// 引导圈线宽（1080P 基准像素）
    /// </summary>
    [ObservableProperty]
    private double _ringThickness = 4;

    /// <summary>
    /// 单个键的额外偏移量，键名使用乐器档案里的按键字符（如 "Q"），
    /// 微调时只写需要修正的键即可，例如 { "Q": { "X": -6, "Y": 4 } }
    /// </summary>
    [ObservableProperty]
    private Dictionary<string, GuidedKeyOffset> _keyOffsets = [];

    /// <summary>
    /// 上面这些数字是不是「玩家自己微调过」的。
    /// 为 true 时每次开始引导都**不再**自动识别琴键（识别会把手动调好的位置覆盖掉），
    /// 圈就固定用存下来的这套参数；想重新识别按 Ctrl+R（识别成功后这里会自动变回 false）。
    /// </summary>
    [ObservableProperty]
    private bool _manualCalibrated;

    public MusicGuidedKeyLayoutConfig Clone()
    {
        return new MusicGuidedKeyLayoutConfig
        {
            BaseX = BaseX,
            BaseY = BaseY,
            ColumnSpacing = ColumnSpacing,
            RowSpacing = RowSpacing,
            RowIndent = RowIndent,
            RingRadius = RingRadius,
            RingThickness = RingThickness,
            KeyOffsets = new Dictionary<string, GuidedKeyOffset>(KeyOffsets, StringComparer.OrdinalIgnoreCase),
            ManualCalibrated = ManualCalibrated
        };
    }
}

/// <summary>
/// 单个琴键的偏移微调量（1080P 基准像素）
/// </summary>
[Serializable]
public partial class GuidedKeyOffset : ObservableObject
{
    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;
}
