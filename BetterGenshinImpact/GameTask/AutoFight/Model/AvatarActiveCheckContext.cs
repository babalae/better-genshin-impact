namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 多次识别出战角色结果上下文
/// </summary>
public class AvatarActiveCheckContext
{
    /// <summary>
    /// 出战标识识别结果的次数统计
    /// </summary>
    public int[] ActiveIndexByArrowCount { get; set; } = new int[4];
    
    /// <summary>
    /// 累计识别失败次数
    /// </summary>
    public int TotalCheckFailedCount { get; set; } = 0;
}