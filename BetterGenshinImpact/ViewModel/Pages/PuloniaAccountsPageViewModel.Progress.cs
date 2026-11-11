using System.Linq;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 账户页中所选账户的账户进度：“路线 CD”页签（固定为所选账户的 CD 总览）与“首领领奖”页签（只读）。
/// 两者不属于草稿：清除路线 CD 记录立即生效，“保存”和“放弃修改”不受影响。
/// 任务计划页与新一条龙页的“查看路线 CD”先通过 <see cref="RequestShowRouteCooldowns"/> 记下要查看的账户和计划，再导航到本页。
/// </summary>
public partial class PuloniaAccountsPageViewModel
{
    /// <summary>详情页签“路线 CD”的序号。</summary>
    internal const int RouteCooldownTabIndex = 2;

    /// <summary>详情页签“首领领奖”的序号。</summary>
    internal const int BossRewardTabIndex = 3;

    /// <summary>尚未应用的“查看路线 CD”请求：要选中的账户与计划筛选；没有请求时为 null。</summary>
    private (string? AccountId, string? PlanId)? _routeCooldownRequest;

    /// <summary>“路线 CD”页签：固定为所选账户，可按计划、状态和关键词筛选并清除记录。</summary>
    public PuloniaRouteCooldownOverviewViewModel RouteCooldowns { get; }

    /// <summary>“首领领奖”页签：所选账户在各计划首领讨伐节点当前批次内的累计领奖次数。</summary>
    public PuloniaBossRewardOverviewViewModel BossRewards { get; }

    /// <summary>
    /// 记下“查看路线 CD”请求：进入本页、账户列表就绪后选中该账户，详情切到“路线 CD”，并按该计划筛选。
    /// 调用方随后导航到账户页；本页已经显示并读取完成时立即应用。
    /// </summary>
    /// <param name="accountId">要查看的账户 ID；为空时保留当前选中的账户。</param>
    /// <param name="planId">计划筛选；为空表示全部计划。</param>
    public void RequestShowRouteCooldowns(string? accountId, string? planId)
    {
        _routeCooldownRequest = (accountId, planId);
        if (_isActive && _loaded)
            ApplyRouteCooldownRequest();
    }

    /// <summary>应用尚未处理的“查看路线 CD”请求；账户目录尚未读取成功时保留请求，下次进入再应用。</summary>
    private void ApplyRouteCooldownRequest()
    {
        if (_routeCooldownRequest is not { } request || !_loaded)
            return;
        _routeCooldownRequest = null;
        // 1. 先选中账户，总览随之固定为该账户；账户已被删除时保留原来的选择并说明
        if (request.AccountId is { } accountId)
        {
            if (Accounts.FirstOrDefault(item => item.Id == accountId) is { } account)
                SelectedAccount = account;
            else
                Message = "要查看的账户已不在账户目录中，显示的是当前选中账户的路线 CD。";
        }
        // 2. 再设置计划筛选（计划选项尚未建立时由总览在建立后应用），最后切换页签，页签显示时读取完成记录
        RouteCooldowns.SetPlanFilter(request.PlanId);
        SelectedDetailTabIndex = RouteCooldownTabIndex;
    }

    /// <summary>所选账户变化后，路线 CD 与首领领奖改为该账户；列表重建期间短暂为空时两者都显示为空。</summary>
    /// <param name="account">新的所选账户</param>
    private void SyncProgressAccount(PuloniaAccountItemViewModel? account)
    {
        RouteCooldowns.SetFixedAccount(account?.Id);
        BossRewards.SetAccount(account?.Id);
    }
}
