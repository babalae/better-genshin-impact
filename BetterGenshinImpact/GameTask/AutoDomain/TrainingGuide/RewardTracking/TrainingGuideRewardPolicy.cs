namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>培养库存校验中允许忽略数量识别失败的通用奖励。</summary>
internal static class TrainingGuideRewardPolicy
{
    public static bool IsCommonReward(string? name) => name is "摩拉" or "好感经验" or "冒险阅历";
}
