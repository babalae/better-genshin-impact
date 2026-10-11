using BetterGenshinImpact.GameTask.AutoFight.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 一次 E 技能分类识别的结果快照（纯数据，不依赖 View）
/// 坐标均为捕获区域物理像素（绘制域），由写入方（Avatar）完成 1080p 识别域 → 捕获像素域的换算，
/// 消费者直接绘制即可，无需再做坐标域换算
/// </summary>
public class ESkillClassifyResult
{
    /// <summary>识别出的技能状态</summary>
    public SkillCdState State { get; init; }

    /// <summary>分类器输出的编号段（"01"/"02"...），置信度不足或角色不匹配时为 null</summary>
    public string? Code { get; init; }

    /// <summary>识别结果所属角色中文名，供显示端按角色元素着色等用途，可能为 null</summary>
    public string? AvatarName { get; init; }

    /// <summary>E 技能分类裁剪框（捕获像素坐标），可绘制用于对齐检测</summary>
    public System.Windows.Rect ClassifyRect { get; init; }

    /// <summary>状态文本的绘制锚点（分类框上方，捕获像素坐标）</summary>
    public System.Windows.Point TextPosition { get; init; }
}

/// <summary>
/// E 技能识别结果显示的数据源：AutoFight 识别流程在后台线程写入最新分类结果，
/// 需要显示的模块（如 AutoComboRunTask）订阅 INPC 变更后经 DrawContent 绘制（数据与显示解耦，GameTask 不直接依赖 View）
/// </summary>
public partial class ESkillClassifyViewModel : ObservableObject
{
    /// <summary>全局唯一实例：数据由 AutoFight 识别流程写入，遮罩窗口只读</summary>
    public static ESkillClassifyViewModel Instance { get; } = new();

    /// <summary>最新一次 E 技能分类结果，null 表示无内容（显示端移除文本）</summary>
    [ObservableProperty]
    private ESkillClassifyResult? _result;
}
