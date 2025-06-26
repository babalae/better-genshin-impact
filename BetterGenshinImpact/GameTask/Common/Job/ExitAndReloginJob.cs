using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class ExitAndReloginJob
{
    private AutoWoodAssets _assets = AutoWoodAssets.Instance;
    private readonly Login3rdParty _login3rdParty = new();
    
    public async Task Start(CancellationToken ct)
    {
        Logger.LogInformation("退出至登录页面");
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);

        // 等待菜单界面出现
        await NewRetry.WaitForElementAppear(
            _assets.MenuBagRo,
            () => Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE),
            ct,
            5,
            1200
        );

        // 点击退出按钮并等待确认弹窗出现
        await NewRetry.WaitForElementAppear(
            _assets.ConfirmRo,
            () => GameCaptureRegion.GameRegionClick((size, scale) => (50 * scale, size.Height - 50 * scale)),
            ct,
            5,
            800
        );

        // 点击确认退出并等待确认弹窗消失
        await NewRetry.WaitForElementDisappear(
            _assets.ConfirmRo,
            screen => {  // 接收当前截图作为参数
                screen.Find(_assets.ConfirmRo, ra => { 
                    ra.Click(); 
                    ra.Dispose(); 
                });
            },
            ct,
            5,
            1000
        );
        await Delay(1000, ct);

        //============== 重新登录流程 ==============
        Logger.LogInformation("点击登录");
        _login3rdParty.RefreshAvailabled();
        if (_login3rdParty is { Type: Login3rdParty.The3rdPartyType.Bilibili, IsAvailabled: true })
        {
            await Delay(1, ct);
            _login3rdParty.Login(ct);
            Logger.LogInformation("退出重登启用 B 服模式");
        }

        // 等待进入游戏按钮出现并点击
        var enterGameAppear = await NewRetry.WaitForElementAppear(
            _assets.EnterGameRo,
            () => { },
            ct,
            120,
            1000
        );
        if (enterGameAppear)
        {
            // 点击进入游戏按钮直到它消失
            await NewRetry.WaitForElementDisappear(
                _assets.EnterGameRo,
                () => GameCaptureRegion.GameRegion1080PPosClick(955, 666),
                ct,
                10,
                1000
            );
        }
        else
        {
            throw new RetryException("未检测进入游戏界面");
        }

        // 等待主界面出现
        var mainUiFound = await NewRetry.WaitForElementAppear(
            ElementAssets.Instance.PaimonMenuRo,
            () => { },
            ct,
            120,
            1000
        );
        
        if (mainUiFound)
        {
            Logger.LogInformation("退出重新登录结束！");
        }
        else
        {
            Logger.LogWarning("未检测到主界面，登录可能未完成");
        }
        await Delay(500, ct);
    }
}

