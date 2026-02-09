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
    public string Name => Lang.S["GameTask_11557_63d2c1"];

    private readonly int _retryTimes = 1;

    private readonly ChooseTalkOptionTask _chooseTalkOptionTask = new();

    private readonly string dailyLocalizedString;
    private readonly string catherineLocalizedString;
    private readonly string expeditionLocalizedString;

    public GoToAdventurersGuildTask()
    {
        IStringLocalizer<GoToAdventurersGuildTask> stringLocalizer = App.GetService<IStringLocalizer<GoToAdventurersGuildTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this.dailyLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["Gen_10255_ce2210"]);
        this.catherineLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_11556_84fdce"]);
        this.expeditionLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_11241_fc6c4c"]);
    }

    public async Task Start(string country, CancellationToken ct, string? dailyRewardPartyName = null ,bool onlyDoOnce = false)
    {
        Logger.LogInformation(Lang.S["GameTask_11555_88fe1c"], Name);
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
                Logger.LogError(Lang.S["GameTask_11554_f38b56"] + e.Message);
                if (i == _retryTimes - 1)
                {
                    // 通知失败
                    throw;
                }
                else
                {
                    await Delay(1000, ct);
                    Logger.LogInformation(Lang.S["GameTask_11553_48aaa9"]);
                }
            }
        }

        Logger.LogInformation(Lang.S["GameTask_11552_4ae836"], Name);
    }

    public async Task DoOnce(string country, CancellationToken ct)
    {
        // 1. 走到冒险家协会并对话
        await GoToAdventurersGuild(country, ct);

        // 每日
        var res = await _chooseTalkOptionTask.SingleSelectText(this.dailyLocalizedString, ct, 10, true);
        if (res == TalkOptionRes.FoundAndClick)
        {
            Logger.LogInformation("▶ {Text}", Lang.S["GameTask_11551_fdd590"]);
            await Delay(800, ct);
            
            // 6.2 每日提示确认
            var ra1 = CaptureToRectArea();
            if (Bv.ClickBlackConfirmButton(ra1))
            {
                Logger.LogInformation(Lang.S["GameTask_11243_4cd27d"]);
            }
            ra1.Dispose();
            
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
                throw new Exception(Lang.S["GameTask_11550_023d04"]);
            }
        }
        else if (res == TalkOptionRes.FoundButNotOrange)
        {
            Logger.LogInformation(Lang.S["GameTask_11549_156f53"], "领取『每日委托』奖励");
        }
        else
        {
            Logger.LogWarning(Lang.S["GameTask_11546_71ad05"], "领取『每日委托』奖励");
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
            Logger.LogInformation(Lang.S["GameTask_11547_7aab81"], "探索派遣");
        }
        else
        {
            Logger.LogWarning(Lang.S["GameTask_11546_71ad05"], "探索派遣");
        }

        // 如果最后还在对话界面，选择最后一个选项退出
        if (Bv.IsInTalkUi(CaptureToRectArea()))
        {
            await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct);
            Logger.LogInformation(Lang.S["GameTask_11545_1e4931"]);
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
        var task = PathingTask.BuildFromFilePath(Global.Absolute(@$"{Lang.S["GameTask_11544_c12235"]}));
        if (task == null)
        {
            throw new Exception(Lang.S["GameTask_11543_91950b"]);
        }
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
                    throw new Exception(Lang.S["GameTask_11542_ba23bf"]);
                }
            }
        }
    }
}