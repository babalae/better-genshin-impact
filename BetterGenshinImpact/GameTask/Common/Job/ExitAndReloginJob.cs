using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class ExitAndReloginJob
{
    private AutoWoodAssets _assets;
    private readonly Login3rdParty _login3rdParty = new();
    
    public async Task Start(CancellationToken ct)
    {
         //============== 退出游戏流程 ==============
        Logger.LogInformation("动作：退出登录");
        _assets = AutoWoodAssets.Instance;
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        await Delay(800, ct);
        
        // 菜单界面验证（带重试机制）
        try
        {
            NewRetry.Do(() => 
            {
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.MenuBagRo);
                if (ra.IsEmpty())
                {
                    // 未检测到菜单时再次发送ESC
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                    throw new RetryException("菜单界面验证失败");
                }
            }, TimeSpan.FromSeconds(1.2), 5);  // 1.2秒内重试5次
        }
        catch
        {
            // 即使失败也继续退出流程
        }

        // 点击退出按钮
        GameCaptureRegion.GameRegionClick((size, scale) => (50 * scale, size.Height - 50 * scale));
        await Delay(500, ct);

        // 确认退出
        using var cr = CaptureToRectArea();
        cr.Find(_assets.ConfirmRo, ra =>
        {
            ra.Click();
            ra.Dispose();
        });
            
        await Delay(1000, ct);  // 等待退出完成

        //============== 重新登录流程 ==============
        // 第三方登录（如果启用）
        _login3rdParty.RefreshAvailabled();
        if (_login3rdParty is { Type: Login3rdParty.The3rdPartyType.Bilibili, IsAvailabled: true })
        {
            await Delay(1, ct);
            _login3rdParty.Login(ct);
            Logger.LogInformation("退出重登启用 B 服模式");
        }

        // 进入游戏检测
        var clickCnt = 0;
        for (var i = 0; i < 50; i++)
        {
            await Delay(1, ct);

            using var contentRegion = CaptureToRectArea();
            using var ra = contentRegion.Find(_assets.EnterGameRo);
            if (!ra.IsEmpty())
            {
                clickCnt++;
                GameCaptureRegion.GameRegion1080PPosClick(955, 666);
            }
            else
            {
                if (clickCnt > 2)
                {
                    await Delay(5000, ct);
                    break;
                }
            }

            await Delay(1000, ct);
        }

        if (clickCnt == 0)
        {
            throw new RetryException("未检测进入游戏界面");
        }

        for (var i = 0; i < 50; i++)
        {
            if (Bv.IsInMainUi(CaptureToRectArea()))
            {
                Logger.LogInformation("动作：退出重新登录结束！");
                break;
            }
            await Delay(1000, ct);
        }
        await Delay(500, ct);
    }
}