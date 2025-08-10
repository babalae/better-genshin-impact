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

    public AutoEatParam()
    {
        SetDefault();
    }

    public void SetDefault()
    {
        var config = TaskContext.Instance().Config.AutoEatConfig;
        ShowNotification = config.ShowNotification;
        CheckInterval = config.CheckInterval;
        EatInterval = config.EatInterval;
    }
}