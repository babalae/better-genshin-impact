using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class EnterAndExitWonderlandJob
{
    private ElementAssets _assets = ElementAssets.Instance;

    public async Task Start(CancellationToken ct)
    {
        Logger.LogInformation("进入千星奇域");
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);

        // 等待千星奇域界面出现
        await NewRetry.WaitForElementAppear(
            _assets.WonderlandClose,
            () => Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_F6),
            ct,
            10,
            1000
        );

        // 点击一个奇域并等待大厅按钮出现
        await NewRetry.WaitForElementAppear(
            _assets.BtnBlackConfirm,
            () => GameCaptureRegion.GameRegion1080PPosClick(680, 310),
            ct,
            5,
            800
        );
        
        // // 点击大厅按钮并等待公共大厅按钮出现
        // await NewRetry.WaitForElementAppear(
        //     _assets.WonderlandEnter,
        //     () =>
        //     {
        //         using var ra = CaptureToRectArea();
        //         Bv.FindAndClick(ra, _assets.EscWonderlandHome);
        //     },
        //     ct,
        //     5,
        //     1000
        // );
        //
        // // 点击公共大厅按钮并等待确认弹窗出现
        // await NewRetry.WaitForElementAppear(
        //     _assets.BtnBlackConfirm,
        //     () => 
        //     {
        //         using var ra = CaptureToRectArea();
        //         Bv.FindAndClick(ra, _assets.WonderlandEnter);
        //     },
        //     ct,
        //     5,
        //     800
        // );
        
        // 点击前往大厅并等待弹窗消失
        await NewRetry.WaitForElementDisappear(
            _assets.BtnBlackConfirm,
            screen =>
            {
                // 接收当前截图作为参数
                screen.Find(_assets.BtnBlackConfirm, ra =>
                {
                    ra.Click();
                    ra.Dispose();
                });
            },
            ct,
            5,
            1000
        );
        await Delay(1000, ct);

        // 等待主界面出现
        var mainUiFound1 = await NewRetry.WaitForElementAppear(
            ElementAssets.Instance.PaimonMenuRo,
            () => { },
            ct,
            120,
            1000
        );

        if (mainUiFound1)
        {
            Logger.LogInformation("已进入千星奇域大厅，准备返回提瓦特");
        }
        else
        {
            Logger.LogWarning("未检测到主界面，可能未处于千星奇域");
        }

        await Delay(500, ct);
        
        // 等待菜单界面出现
        await NewRetry.WaitForElementAppear(
            _assets.BtnBackTeyvat,
            () => Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE),
            ct,
            20,
            800
        );
        
        // 点击返回提瓦特按钮并等待确认弹窗出现
        await NewRetry.WaitForElementAppear(
            _assets.BtnBlackConfirm,
            () => 
            {
                using var ra = CaptureToRectArea();
                Bv.FindAndClick(ra, _assets.BtnBackTeyvat);
            },
            ct,
            5,
            800
        );
        
        // 点击确认并等待确认弹窗消失
        await NewRetry.WaitForElementDisappear(
            _assets.BtnBlackConfirm,
            screen =>
            {
                // 接收当前截图作为参数
                screen.Find(_assets.BtnBlackConfirm, ra =>
                {
                    ra.Click();
                    ra.Dispose();
                });
            },
            ct,
            5,
            1000
        );
        await Delay(1000, ct);
        
        // 等待主界面出现
        var mainUiFound2 = await NewRetry.WaitForElementAppear(
            ElementAssets.Instance.PaimonMenuRo,
            () => { },
            ct,
            120,
            1000
        );

        if (mainUiFound2)
        {
            Logger.LogInformation("已返回提瓦特");
        }
        else
        {
            Logger.LogWarning("未检测到主界面，可能未处于提瓦特");
        }

        await Delay(500, ct);
    }
}