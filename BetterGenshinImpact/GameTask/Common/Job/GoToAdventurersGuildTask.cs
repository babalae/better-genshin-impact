using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
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

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 持续对话并选择选项子任务
    /// </summary>
    private readonly ChooseTalkOptionTask _chooseTalkOptionTask;

    private readonly string dailyLocalizedString;
    private readonly string catherineLocalizedString;
    private readonly string expeditionLocalizedString;

    /// <summary>
    /// 创建前往冒险家协会领取奖励任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public GoToAdventurersGuildTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _chooseTalkOptionTask = new ChooseTalkOptionTask(runtime);
        IStringLocalizer<GoToAdventurersGuildTask> stringLocalizer = App.GetService<IStringLocalizer<GoToAdventurersGuildTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(config.OtherConfig.GameCultureInfoName);
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
                    await new SwitchPartyTask(_runtime).Start(dailyRewardPartyName, ct);
                }

                if (!onlyDoOnce)
                {
                    // F1领取奖励：与原行为一致，历练点领取的失败或异常不影响后续冒险家协会流程；取消与运行环境停止仍向外抛出
                    try
                    {
                        await new ClaimEncounterPointsRewardsTask(_runtime, _config).RunAsync(NoParam.Instance, ct);
                    }
                    catch (Exception e) when (e is not NormalEndException and not OperationCanceledException)
                    {
                        Logger.LogDebug(e, "领取长效历练点奖励异常");
                        Logger.LogError("领取长效历练点奖励异常: {Msg}", e.Message);
                    }
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
                    await _runtime.Delay(1000, ct);
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
            await _runtime.Delay(800, ct);
            
            // 6.2 每日提示确认
            using var ra1 = _runtime.CaptureToRectArea();
            if (Bv.ClickBlackConfirmButton(ra1))
            {
                Logger.LogInformation("存在提示并确认");
            }
            
            await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct, null, 3); // 点几下
            await Bv.WaitUntilFound(_runtime, ElementRecognition.Get("PaimonMenu", _runtime.SystemInfo), ct);
            await _runtime.Delay(500, ct);
            InputHub.Background.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await new ReturnMainUiTask(_runtime).Start(ct);

            // 结束后重新打开
            await _runtime.Delay(1200, ct);
            using var ra = _runtime.CaptureToRectArea();
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
            await _runtime.Delay(500, ct);
            new OneKeyExpeditionTask(_runtime).Run();
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
        using var talkUiCapture = _runtime.CaptureToRectArea();
        if (Bv.IsInTalkUi(talkUiCapture))
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
        if (task == null)
        {
            throw new Exception("地图追踪文件加载失败");
        }
        var pathingTask = new PathExecutor(_runtime, _config, ct)
        {
            PartyConfig = new PathingPartyConfig
            {
                Enabled = true,
                AutoSkipEnabled = true
            },
            EndAction = region => Bv.FindFAndPress(region, text: this.catherineLocalizedString)
        };
        // 找到凯瑟琳时允许 EndAction 提前结束路线，随后仍核验对话界面。
        _ = await pathingTask.Pathing(task);

        await _runtime.Delay(600, ct);

        const int retryTalkTimes = 3;
        for (int i = 0; i < retryTalkTimes; i++)
        {
            using var ra = _runtime.CaptureToRectArea();
            if (!Bv.IsInTalkUi(ra))
            {
                InputHub.Foreground.Keyboard.KeyPress(AutoPickAssets.Get(ra, _config.AutoPickConfig.PickKey).PickVk);
                await _runtime.Delay(500, ct);

                if (i == retryTalkTimes - 1)
                {
                    throw new Exception("与凯瑟琳对话失败");
                }
            }
        }
    }
}
