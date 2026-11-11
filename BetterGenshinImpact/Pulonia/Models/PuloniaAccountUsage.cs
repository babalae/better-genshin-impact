using System.Collections.Generic;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 指定了某个账户作为运行账户的计划（统筹设计 9.3 账户页概况“指定了该账户的计划”）。
/// </summary>
/// <param name="PlanId">计划 ID</param>
/// <param name="PlanName">计划名称</param>
/// <param name="Purpose">计划用途：普通计划在任务计划页打开，一条龙配置在新一条龙页打开</param>
public sealed record PuloniaAccountPlanUsage(string PlanId, string PlanName, PuloniaTaskPlanPurpose Purpose);

/// <summary>
/// 删除一个账户时会一并删除的数据（统筹设计 4.1：删除前列出这些数据并确认）。
/// </summary>
/// <param name="RouteCompletionCount">路线 CD 完成记录条数</param>
/// <param name="BossRewardEntryCount">记录了首领领奖次数的（计划、节点、重置批次）条目数</param>
/// <param name="HasCredential">是否保存了登录凭据（账号密码）</param>
/// <param name="ImageCount">不再被其他账户引用、会被删除的账户图片张数</param>
/// <param name="OtherData">其他模块按 UID 存放、会被删除的数据名称，例如“狛荷屋记录”</param>
public sealed record PuloniaAccountRemovalImpact(int RouteCompletionCount, int BossRewardEntryCount, bool HasCredential,
    int ImageCount, IReadOnlyList<string> OtherData)
{
    /// <summary>
    /// 逐项说明会删除的数据，例如“路线 CD 完成记录 12 条”“登录凭据”；没有任何数据时为空。
    /// </summary>
    public IReadOnlyList<string> Describe()
    {
        var items = new List<string>();
        if (HasCredential) items.Add("保存的登录凭据（账号密码）");
        if (ImageCount > 0) items.Add($"账户图片 {ImageCount} 张");
        if (RouteCompletionCount > 0) items.Add($"路线 CD 完成记录 {RouteCompletionCount} 条");
        if (BossRewardEntryCount > 0) items.Add($"首领领奖次数 {BossRewardEntryCount} 项");
        items.AddRange(OtherData);
        return items;
    }
}
