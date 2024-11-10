using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class GoToAdventurersGuildTask
{
    public string Name => "前往冒险家协会领取奖励";

    private readonly int _retryTimes = 2;

    private readonly ChooseTalkOptionTask _chooseTalkOptionTask = new();

    public async Task Start(string country, CancellationToken ct)
    {
        Logger.LogInformation("→ {Name} 开始", Name);
        for (int i = 0; i < _retryTimes; i++)
        {
            try
            {
                await DoOnce(country, ct);
                break;
            }
            catch (Exception e)
            {
                Logger.LogError("前往冒险家协会领取奖励执行异常：" + e.Message);
                if (i == _retryTimes - 1)
                {
                    // 通知失败
                    throw;
                }
                else
                {
                    await Delay(1000, ct);
                    Logger.LogInformation("重试前往冒险家协会领取奖励");
                }
            }
        }
        Logger.LogInformation("→ {Name} 结束", Name);
    }

    public async Task DoOnce(string country, CancellationToken ct)
    {
        // 1. 走到冒险家协会
        await GoToAdventurersGuild(country, ct);

        // 2. 交互
        var ra = CaptureToRectArea();
        if (!Bv.FindFAndPress(ra, "凯瑟琳"))
        {
            throw new Exception("未找与凯瑟琳对话交互按钮");
        }

        // 3. 等待对话界面

        // 每日
        var res = await _chooseTalkOptionTask.SingleSelectText("每日", ct, 10, true);
        if (res == TalkOptionRes.FoundAndClick)
        {
            Logger.LogInformation("▶ {Text}", "领取『每日委托』奖励！");
            await Delay(1000, ct);
            await new ReturnMainUiTask().Start(ct);

            // 结束后重新打开
            await Delay(200, ct);
            ra = CaptureToRectArea();
            if (!Bv.FindFAndPress(ra, "凯瑟琳"))
            {
                throw new Exception("未找与凯瑟琳对话交互按钮");
            }
        }
        else if (res == TalkOptionRes.FoundButNotOrange)
        {
            Logger.LogInformation("▶ {Text} 未完成或者已领取", "领取『每日委托』奖励");
        }
        else
        {
            Logger.LogWarning("▶ 未找到 {Text} 选项", "领取『每日委托』奖励");
        }

        // 探索
        res = await _chooseTalkOptionTask.SingleSelectText("探索", ct, 10, true);
        if (res == TalkOptionRes.FoundAndClick)
        {
            new OneKeyExpeditionTask().Run(AutoSkipAssets.Instance);
        }
        else if (res == TalkOptionRes.FoundButNotOrange)
        {
            Logger.LogInformation("▶ {Text} 未探索完成或已领取", "探索派遣");
        }
        else
        {
            Logger.LogWarning("▶ 未找到 {Text} 选项", "探索派遣");
        }

        // 如果最后还在对话界面，选择最后一个选项退出
        if (Bv.IsInTalkUi(CaptureToRectArea()))
        {
            await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct);
            Logger.LogInformation("退出当前对话");
        }
    }

    /// <summary>
    /// 前往冒险家协会
    /// </summary>
    /// <param name="country"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task GoToAdventurersGuild(string country, CancellationToken ct)
    {
        var task = PathingTask.BuildFromFilePath(Global.Absolute(@$"GameTask\Common\Element\Assets\Json\冒险家协会_{country}.json"));
        var pathingTask = new PathExecutor(ct)
        {
            PartyConfig = new PathingPartyConfig
            {
                Enabled = true,
                AutoSkipEnabled = true
            }
        };
        await pathingTask.Pathing(task);

        await Delay(500, ct);
    }
}
