namespace BetterGenshinImpact.GameTask.AutoEat;

/// <summary>
/// 自动吃药任务的结果数据
/// </summary>
public sealed class AutoEatData
{
    /// <summary>
    /// 指定食物时吃完后剩余的数量：-1 表示没有找到该食物，-2 表示无法识别数量；
    /// 使用便携营养袋模式时为 null
    /// </summary>
    public int? Count { get; set; }
}
