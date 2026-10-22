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
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 一键领取纪行
/// </summary>
[SoloTaskInfo(TaskId, "领取纪行", Group = "日常奖励", Order = 150)]
public class ClaimBattlePassRewardsTask : ISoloTask<NoParam, NoData>
{
    /// <summary>
    /// 任务标识。
    /// </summary>
    public const string TaskId = "claim_battle_pass";

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

    private readonly string[] claimAllLocalizedStrings;

    private bool _manualSelectionReminderLogged;

    /// <summary>
    /// 创建一键领取纪行任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public ClaimBattlePassRewardsTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _returnMainUiTask = new ReturnMainUiTask(runtime);
        IStringLocalizer<ClaimBattlePassRewardsTask> stringLocalizer = App.GetService<IStringLocalizer<ClaimBattlePassRewardsTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(config.OtherConfig.GameCultureInfoName);
        this.claimAllLocalizedStrings = ((string[])["一键", "领取"]).Select(i => stringLocalizer.WithCultureGet(cultureInfo, i)).ToArray();
    }

    /// <summary>
    /// 打开纪行，先在任务页领取纪行点数，再回到奖励页一键领取奖励，结束后返回主界面。
    /// 识别或操作异常直接向外抛出，由调用方决定是否吞掉。
    /// </summary>
    /// <param name="param">空参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>流程正常结束返回 Unverified（无法确认奖励是否真正到账）</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(NoParam param, CancellationToken ct)
    {
        await _returnMainUiTask.Start(ct);

        await _runtime.Delay(200, ct);
        InputHub.Background.SimulateAction(GIActions.OpenBattlePassScreen); // F4 开纪行

        // 先领取纪行点数，避免一进入纪行就因可选奖励弹窗阻塞后续流程
        await _runtime.Delay(1000, ct);
        _runtime.GameRegion1080PPosClick(960, 45); // 点中间
        await _runtime.Delay(500, ct);
        var pointsClaimed = await ClaimAll(ct);

        // 最后再回到奖励页领取，若存在手动选择奖励则仅记录日志提醒
        await _runtime.Delay(2500, ct); // 等待升级动画
        // 还可能存在领取到原石的情况
        using (var ra = _runtime.CaptureToRectArea())
        {
            using var primogem = ra.Find(ElementRecognition.Get("Primogem", ra));
            if (primogem.IsExist())
            {
                InputHub.Background.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            }
        }
        _runtime.GameRegion1080PPosClick(858, 45);
        await _runtime.Delay(1500, ct);
        var rewardsClaimed = await ClaimAll(ct);

        // 关闭
        await _returnMainUiTask.Start(ct);
        return SoloTaskResult.Unverified(pointsClaimed || rewardsClaimed ? "已执行纪行一键领取。" : "纪行无需领取。");
    }

    /// <summary>
    /// 一键领取
    /// </summary>
    /// <returns></returns>
    private async Task<bool> ClaimAll(CancellationToken ct)
    {
        using var ra = _runtime.CaptureToRectArea();
        var ocrList = ra.FindMulti(RecognitionObject.Ocr(ra.ToRect().CutRightBottom(0.3, 0.2)));
        var wt = ocrList.FirstOrDefault(txt => this.claimAllLocalizedStrings.Any(i => Regex.IsMatch(txt.Text, i)));
        if (wt != null)
        {
            wt.Click();
            Logger.LogInformation("纪行：{Text}", "一键领取");
            await _runtime.Delay(1000, ct);
            using var ra2 = _runtime.CaptureToRectArea();
            if (IsManualSelectionDialog(ra2))
            {
                LogManualSelectionReminder();
                return true;
            }

            if (ra2.Find(ElementRecognition.Get("Primogem", ra2)).IsExist())
            {
                InputHub.Background.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            }

            return true;
        }
        else
        {
            Logger.LogInformation("纪行：{Text}", "无需领取");
            return false;
        }
    }

    private static bool IsManualSelectionDialog(ImageRegion region)
    {
        if (Bv.IsInPromptDialog(region))
        {
            return true;
        }

        var hasCancelButton = HasDialogButton(region, ElementRecognition.Get("BtnBlackCancel", region))
                              || HasDialogButton(region, ElementRecognition.Get("BtnWhiteCancel", region));
        if (!hasCancelButton)
        {
            return false;
        }

        return HasDialogButton(region, ElementRecognition.Get("BtnBlackConfirm", region))
               || HasDialogButton(region, ElementRecognition.Get("BtnWhiteConfirm", region));
    }

    private static bool HasDialogButton(ImageRegion region, RecognitionObject recognitionObject)
    {
        using var buttonRegion = region.Find(recognitionObject);
        return buttonRegion.IsExist();
    }

    private void LogManualSelectionReminder()
    {
        if (_manualSelectionReminderLogged)
        {
            return;
        }

        _manualSelectionReminderLogged = true;
        Logger.LogWarning("纪行：检测到需手动选择的奖励，请手动处理");
    }
}
