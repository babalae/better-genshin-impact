using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.View.Pages.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取每日奖励：依次领取长效历练点、前往冒险家协会领取每日委托与探索派遣奖励、领取纪行。
/// </summary>
[SoloTaskInfo(TaskId, "领取每日奖励", Group = "日常奖励", Order = 130, OneDragonOrder = 7)]
[SoloTaskSettingsView(typeof(ClaimDailyRewardsSettingsView), typeof(ClaimDailyRewardsSettingsViewModel))]
public class ClaimDailyRewardsTask : ISoloTask<DailyRewardsParam, NoData>
{
    /// <summary>
    /// 任务标识。
    /// </summary>
    public const string TaskId = "claim_daily_rewards";

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 创建领取每日奖励任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public ClaimDailyRewardsTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    /// <summary>
    /// 依次执行历练点领取、冒险家协会领取和纪行领取。子任务的异常与取消直接向外抛出。
    /// </summary>
    /// <param name="param">每日奖励参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>参数无效或历练点领取失败返回 Failed；全部流程正常结束返回 Unverified（无法确认奖励是否真正到账）</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(DailyRewardsParam param, CancellationToken ct)
    {
        // 国家决定冒险家协会路线文件，为空时无法执行
        if (string.IsNullOrWhiteSpace(param.Country))
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "未指定前往冒险家协会的国家。");
        }

        // 1. F1 领取长效历练点奖励，失败时直接返回该结果
        var encounterResult = await new ClaimEncounterPointsRewardsTask(_runtime, _config).RunAsync(NoParam.Instance, ct);
        if (encounterResult.Kind == SoloTaskResultKind.Failed)
        {
            return encounterResult;
        }

        // 2. 前往冒险家协会领取每日委托与探索派遣奖励（历练点已在上一步领取，所以只执行一次协会流程）
        await new GoToAdventurersGuildTask(_runtime, _config).Start(param.Country, ct, param.PartyName, onlyDoOnce: true);

        // 3. 领取纪行
        await new ClaimBattlePassRewardsTask(_runtime, _config).RunAsync(NoParam.Instance, ct);

        return SoloTaskResult.Unverified("每日奖励领取流程已结束。");
    }
}
