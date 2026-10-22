using BetterGenshinImpact.Core.Input;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取邮件奖励
/// </summary>
[SoloTaskInfo(TaskId, "领取邮件", Group = "日常奖励", Order = 110, OneDragonOrder = 1)]
public class ClaimMailRewardsTask : ISoloTask<NoParam, NoData>
{
    /// <summary>
    /// 任务标识。
    /// </summary>
    public const string TaskId = "claim_mail";

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 返回主界面子任务
    /// </summary>
    private readonly ReturnMainUiTask _returnMainUiTask;

    /// <summary>
    /// 创建领取邮件奖励任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public ClaimMailRewardsTask(GameRuntime runtime)
    {
        _runtime = runtime;
        _returnMainUiTask = new ReturnMainUiTask(runtime);
    }

    /// <summary>
    /// 打开派蒙菜单进入邮件页并点击“全部领取”，结束后返回主界面。
    /// 识别或操作异常直接向外抛出，由调用方决定是否吞掉。
    /// </summary>
    /// <param name="param">空参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>流程正常结束返回 Unverified（无法确认奖励是否真正到账）</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(NoParam param, CancellationToken ct)
    {
        await _returnMainUiTask.Start(ct);

        await _runtime.Delay(200, ct);

        InputHub.Background.SimulateAction(GIActions.OpenPaimonMenu); // ESC 

        await _runtime.Delay(1300, ct);

        // 根据邮件图标与“全部领取”按钮的识别情况生成结果摘要
        string message;
        using (var ra = _runtime.CaptureToRectArea())
        {
            var mailIcon = ra.Find(ElementRecognition.Get("EscMailReward", ra));
            if (mailIcon.IsExist())
            {
                mailIcon.Click();
                await _runtime.Delay(1000, ct);

                using var claimArea = _runtime.CaptureToRectArea();
                var claimAll = claimArea.Find(ElementRecognition.Get("Collect", claimArea));
                if (claimAll.IsExist())
                {
                    claimAll.Click();
                    Logger.LogInformation("邮件：{Text}", "全部领取");
                    await _runtime.Delay(200, ct);
                    // TODO 截图

                    InputHub.Background.Keyboard.KeyPress(User32.VK.VK_ESCAPE); // ESC 
                    message = "已点击邮件全部领取。";
                }
                else
                {
                    message = "已打开邮件页，但未找到全部领取按钮。";
                }
            }
            else
            {
                Logger.LogInformation("邮件：{Text}", "没有邮件奖励");
                message = "没有邮件奖励。";
            }
        }

        // 关闭
        await _returnMainUiTask.Start(ct);
        return SoloTaskResult.Unverified(message);
    }
}
