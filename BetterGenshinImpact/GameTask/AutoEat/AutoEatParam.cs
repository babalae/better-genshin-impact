using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoEat;

/// <summary>
/// 自动吃药任务参数
/// </summary>
public class AutoEatParam : BaseTaskParam
{
    /// <summary>
    /// 是否显示通知
    /// </summary>
    public bool ShowNotification { get; set; }

    /// <summary>
    /// 检测间隔（毫秒）
    /// </summary>
    public int CheckInterval { get; set; }

    /// <summary>
    /// 吃药间隔（毫秒）
    /// </summary>
    public int EatInterval { get; set; }

    /// <summary>
    /// 食物名称
    /// 如果传空就使用便携营养袋，否则进入背包查找对应食物并使用
    /// </summary>
    public string? FoodName { get; set; }
}