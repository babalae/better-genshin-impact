using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.AutoWood;

/// <summary>
/// 自动伐木任务参数
/// </summary>
public class WoodTaskParam : BaseTaskParam<AutoWoodTask>
{
    /// <summary>
    /// 伐木总轮数，默认 9999（视为不限制）
    /// </summary>
    public int WoodRoundNum { get; set; } = 9999;

    /// <summary>
    /// 每种木材的数量上限，默认 9999（视为不限制）
    /// </summary>
    public int WoodDailyMaxCount { get; set; } = 9999;

    /// <summary>
    /// 使用默认值创建参数，供 JSON 反序列化和默认参数使用
    /// </summary>
    public WoodTaskParam() : base(null, null)
    {
    }

    /// <summary>
    /// 使用指定轮数和数量上限创建参数；传 0 时视为不限制
    /// </summary>
    /// <param name="woodRoundNum">伐木总轮数，0 表示 9999</param>
    /// <param name="woodDailyMaxCount">木材数量上限，0 或不小于 9999 时表示 9999</param>
    public WoodTaskParam(int woodRoundNum, int woodDailyMaxCount) : base(null, null)
    {
        WoodRoundNum = woodRoundNum;
        if (woodRoundNum == 0)
        {
            WoodRoundNum = 9999;
        }

        WoodDailyMaxCount = woodDailyMaxCount;
        if (WoodDailyMaxCount is 0 or >= 9999) WoodDailyMaxCount = 9999;
    }
}
