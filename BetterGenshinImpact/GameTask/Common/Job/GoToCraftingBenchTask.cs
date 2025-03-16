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
using BetterGenshinImpact.Core.Simulator.Extensions;
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
        // 1. 走到合成台并交互
        await GoToCraftingBench(country, ct);

        // 2. 等待合成界面
        await _chooseTalkOptionTask.SelectLastOptionUntilEnd(ct,
            region => region.Find(ElementAssets.Instance.BtnWhiteConfirm).IsExist()
        );
        await Delay(200, ct);

        // 判断浓缩树脂是否存在
        // TODO 满的情况是怎么样子的
        var ra = CaptureToRectArea();
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
                AutoSkipEnabled = true,
                AutoRunEnabled = country != "枫丹",
            },
            EndAction = region => Bv.FindFAndPress(region, "合成")
        };
        await pathingTask.Pathing(task);

        await Delay(700, ct);
        
        
        // 多种尝试 责任链
        if (!IsInCraftingTalkUi())
        {
            // 直接重试
            await TryPressCrafting(ct);
            
            if (!IsInCraftingTalkUi())
            {
                // 往回走一步重试
                Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown);
                await Delay(200, ct);
                Simulation.SendInput.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
                
                await TryPressCrafting(ct);
            
                // 最后 check
                if (!IsInCraftingTalkUi())
                {
                    throw new Exception("未进入和合成台交互对话界面");
                }
            
            }
        }
    }


    private bool IsInCraftingTalkUi()
    {
        using var ra = CaptureToRectArea();
        return Bv.IsInTalkUi(ra);
    }
    
    private async Task<bool> TryPressCrafting( CancellationToken ct)
    {
        using var ra1 = CaptureToRectArea();
        var res = Bv.FindFAndPress(ra1, "合成");
        if (res)
        {
            await Delay(1000, ct);
        }
        return res;
    }
}