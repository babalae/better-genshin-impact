using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取长效历练点奖励
/// </summary>
[SoloTaskInfo(TaskId, "领取历练点", Group = "日常奖励", Order = 160)]
public class ClaimEncounterPointsRewardsTask : ISoloTask<NoParam, NoData>
{
    /// <summary>
    /// 任务标识。
    /// </summary>
    public const string TaskId = "claim_encounter_points";

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 返回主界面子任务
    /// </summary>
    private readonly ReturnMainUiTask _returnMainUiTask;

    private readonly string commissionsLocalizedString;

    /// <summary>
    /// 创建领取长效历练点奖励任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public ClaimEncounterPointsRewardsTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _returnMainUiTask = new ReturnMainUiTask(runtime);
        IStringLocalizer<ClaimEncounterPointsRewardsTask> stringLocalizer = App.GetService<IStringLocalizer<ClaimEncounterPointsRewardsTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(config.OtherConfig.GameCultureInfoName);
        this.commissionsLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "委托");
    }

    /// <summary>
    /// F1 打开冒险之证进入委托页，领取长效历练点奖励，结束后返回主界面。
    /// 识别或操作异常直接向外抛出，由调用方决定是否吞掉。
    /// </summary>
    /// <param name="param">空参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>未找到委托按钮返回 Failed(page_not_reached)；流程正常结束返回 Unverified（无法确认奖励是否真正到账）</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(NoParam param, CancellationToken ct)
    {
        await _returnMainUiTask.Start(ct);

        await _runtime.Delay(200, ct);

        InputHub.Background.SimulateAction(GIActions.OpenAdventurerHandbook); // F1 开书

        await _runtime.Delay(1000, ct);

        var assetScale = _runtime.SystemInfo.AssetScale;

        var earlyClaim = false; // 无需点击委托直接领取
        // 找委托按钮
        var f1Success = await NewRetry.WaitForAction(_runtime, () =>
        {
            using var ra = _runtime.CaptureToRectArea();

            var ocrList = ra.FindMulti(RecognitionObject.Ocr(0, 0, 380 * assetScale, ra.Height));

            var wt = ocrList.FirstOrDefault(txt => Regex.IsMatch(txt.Text, this.commissionsLocalizedString));

            if (wt != null)
            {
                using var claimCapture = _runtime.CaptureToRectArea();
                if (ClickClaimBtn(claimCapture))
                {
                    earlyClaim = true;
                    return true;
                }

                wt.Click();
                return true;
            }

            return false;
        }, ct, 5);

        if (!f1Success)
        {
            Logger.LogError("{F}未找到委托按钮,F1打开冒险之证失败", "历练点：");
            return SoloTaskResult.Failed(SoloTaskReasons.PageNotReached, "未能进入或处理历练点领取页面。");
        }

        if (earlyClaim)
        {
            // 已经领取过
            await _runtime.Delay(1000, ct);

            // TODO 截图并通知
            return SoloTaskResult.Unverified("已领取长效历练点奖励。");
        }

        await _runtime.Delay(1000, ct);

        // 领取
        var claimed = false;
        using var claimCapture = _runtime.CaptureToRectArea();
        if (ClickClaimBtn(claimCapture))
        {
            claimed = true;
            await _runtime.Delay(1000, ct);

            // TODO 截图并通知
        }

        // 关闭
        await _returnMainUiTask.Start(ct);
        return SoloTaskResult.Unverified(claimed ? "已领取长效历练点奖励。" : "没有可领取的长效历练点奖励。");
    }

    private static bool ClickClaimBtn(ImageRegion ra2)
    {
        var claimBtn = ra2.Find(ElementRecognition.Get("BtnClaimEncounterPointsRewards", ra2));
        if (claimBtn.IsExist())
        {
            claimBtn.Click();
            Logger.LogInformation("{F}领取长效历练点奖励", "历练点：");


            return true;
        }
        else
        {
            Logger.LogInformation("{F}未找到领取历练点奖励按钮", "历练点：");
            return false;
        }
    }
}
