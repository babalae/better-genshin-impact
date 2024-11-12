using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class GoToCraftingBenchTask
{
    public string Name => "前往合成台";

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
                Logger.LogError("前往合成台领取奖励执行异常：" + e.Message);
                if (i == _retryTimes - 1)
                {
                    // 通知失败
                    throw;
                }
                else
                {
                    await Delay(1000, ct);
                    Logger.LogInformation("重试前往合成台领取奖励");
                }
            }
        }

        Logger.LogInformation("→ {Name} 结束", Name);
    }

    public async Task DoOnce(string country, CancellationToken ct)
    {
        // 1. 走到合成台
        await GoToCraftingBench(country, ct);

        // 2. 交互
        var ra = CaptureToRectArea();
        if (!Bv.FindFAndPress(ra, "合成"))
        {
            throw new Exception("未找与合成台交互按钮");
        }

        // 3. 等待合成界面
        await Delay(100, ct);
        await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct,
            region => region.Find(ElementAssets.Instance.BtnWhiteConfirm).IsExist()
        );
        await Delay(200, ct);

        // 判断浓缩树脂是否存在
        // TODO 满的情况是怎么样子的
        ra = CaptureToRectArea();
        var resin = ra.Find(ElementAssets.Instance.CraftCondensedResin);
        if (resin.IsExist())
        {
            Bv.ClickWhiteConfirmButton(ra);
            Logger.LogInformation("合成{Text}", "浓缩树脂");
            await Delay(300, ct);
            Bv.ClickBlackConfirmButton(CaptureToRectArea());
            await Delay(300, ct);
            // 直接ESC退出即可
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        }
        else
        {
            Logger.LogInformation("无需合成浓缩树脂");
        }

        await new ReturnMainUiTask().Start(ct);
    }

    /// <summary>
    /// 前往合成台
    /// </summary>
    /// <param name="country"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task GoToCraftingBench(string country, CancellationToken ct)
    {
        var task = PathingTask.BuildFromFilePath(Global.Absolute(@$"GameTask\Common\Element\Assets\Json\合成台_{country}.json"));
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
