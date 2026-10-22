using BetterGenshinImpact.GameTask.Model.GameUI;

namespace BetterGenshinImpact.GameTask.GetGridIcons;

/// <summary>
/// Grid 图标识别模型准确率测试任务的参数。
/// </summary>
public class GridIconsAccuracyTestParam
{
    /// <summary>
    /// 要测试的 Grid 界面。
    /// </summary>
    public GridScreenName GridName { get; set; } = GridScreenName.Weapons;

    /// <summary>
    /// 最多测试的物品数量，为空表示不限。
    /// </summary>
    public int? MaxNumToTest { get; set; }
}
