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
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using Microsoft.Extensions.Localization;
using BetterGenshinImpact.Helpers;
using System.Globalization;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class GoToAdventurersGuildTask
{
    public string Name => "前往冒险家协会领取奖励";

    private readonly int _retryTimes = 1;

    private readonly ChooseTalkOptionTask _chooseTalkOptionTask = new();

    private readonly string dailyLocalizedString;
    private readonly string catherineLocalizedString;
    private readonly string expeditionLocalizedString;

    public GoToAdventurersGuildTask()
    {
        IStringLocalizer<GoToAdventurersGuildTask> stringLocalizer = App.GetService<IStringLocalizer<GoToAdventurersGuildTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this.dailyLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "每日");
        this.catherineLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "凯瑟琳");
        this.expeditionLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "探索");
    }

    public async Task Start(string country, CancellationToken ct, string? dailyRewardPartyName = null ,bool onlyDoOnce = false)
    {
        Logger.LogInformation("→ {Name} 开始", Name);
        for (int i = 0; i < _retryTimes; i++)
        {
            try
            {
                // 如果有好感队伍名称，先切换到好感队伍
                if (!string.IsNullOrEmpty(dailyRewardPartyName))
                {
                    await new SwitchPartyTask().Start(dailyRewardPartyName, ct);
                }

                if (!onlyDoOnce)
                {
                    // F1领取奖励
                    await new ClaimEncounterPointsRewardsTask().Start(ct);
                }

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
        // 1. 走到冒险家协会并对话
        await GoToAdventurersGuild(country, ct);

        // 每日
        var res = await _chooseTalkOptionTask.SingleSelectText(this.dailyLocalizedString, ct, 10, true);
        if (res == TalkOptionRes.FoundAndClick)
        {
            Logger.LogInformation("▶ {Text}", "领取『每日委托』奖励！");
            await Delay(500, ct);
            await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct, null, 3); // 点几下
            await Bv.WaitUntilFound(ElementAssets.Instance.PaimonMenuRo, ct);
            await Delay(500, ct);
            TaskContext.Instance().PostMessageSimulator.KeyPress(User32.VK.VK_ESCAPE);
            await new ReturnMainUiTask().Start(ct);

            // 结束后重新打开
            await Delay(1200, ct);
            var ra = CaptureToRectArea();
            if (!Bv.FindFAndPress(ra, text: this.catherineLocalizedString))
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
        res = await _chooseTalkOptionTask.SingleSelectText(this.expeditionLocalizedString, ct, 10, true);
        if (res == TalkOptionRes.FoundAndClick)
        {
            await Delay(500, ct);
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
            },
            EndAction = region => Bv.FindFAndPress(region, text: this.catherineLocalizedString)
        };
        await pathingTask.Pathing(task);

        await Delay(600, ct);

        const int retryTalkTimes = 3;
        for (int i = 0; i < retryTalkTimes; i++)
        {
            using var ra = CaptureToRectArea();
            if (!Bv.IsInTalkUi(ra))
            {
                Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
                await Delay(500, ct);

                if (i == retryTalkTimes - 1)
                {
                    throw new Exception("与凯瑟琳对话失败");
                }
            }
        }
    }
}