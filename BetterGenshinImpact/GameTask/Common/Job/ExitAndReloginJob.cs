using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class ExitAndReloginJob
{
    private readonly Login3rdParty _login3rdParty = new();

    private static RecognitionObject GetAutoWoodRecognitionObject(string objectName)
    {
        return RecognitionAssets.Get("AutoWood", objectName);
    }

    public async Task Start(CancellationToken ct)
    {
        Logger.LogInformation("退出至登录页面");
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);

        // 等待菜单界面出现
        await NewRetry.WaitForElementAppear(
            GetAutoWoodRecognitionObject("MenuBag"),
            () => Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE),
            ct,
            10,
            1200
        );

        // 点击退出按钮并等待确认弹窗出现
        await NewRetry.WaitForElementAppear(
            GetAutoWoodRecognitionObject("Confirm"),
            () => GameCaptureRegion.GameRegionClick((size, scale) => (50 * scale, size.Height - 50 * scale)),
            ct,
            5,
            800
        );

        // 点击确认退出并等待确认弹窗消失
        await NewRetry.WaitForElementDisappear(
            GetAutoWoodRecognitionObject("Confirm"),
            screen =>
            {
                // 接收当前截图作为参数
                screen.Find(RecognitionAssets.Get("AutoWood", "Confirm", screen), ra =>
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

        //============== 重新登录流程 ==============
        Logger.LogInformation("点击登录");
        _login3rdParty.RefreshAvailabled();
        if (_login3rdParty is { Type: Login3rdParty.The3rdPartyType.Bilibili, IsAvailabled: true })
        {
            // await Delay(1, ct);
            Thread.Sleep(100);
            _login3rdParty.Login(ct);
            Logger.LogInformation("退出重登启用 B 服模式");
        }

        // 等待进入游戏按钮出现并点击
        var enterGameAppear = await NewRetry.WaitForElementAppear(
            GetAutoWoodRecognitionObject("EnterGame"),
            () => { },
            ct,
            120,
            1000
        );
        if (!enterGameAppear)
        {
            throw new RetryException("未检测进入游戏界面");
        }

        // 点击进入游戏按钮直到它消失
        var waitForEnterGameRoDisappear = await NewRetry.WaitForElementDisappear(
            GetAutoWoodRecognitionObject("EnterGame"),
            () => GameCaptureRegion.GameRegion1080PPosClick(955, 666),
            ct,
            120,
            1000
        );
        if (!waitForEnterGameRoDisappear)
        {
            throw new RetryException("未检测到进入游戏按钮消失, 可能未点击成功");
        }


        // 等待主界面出现
        var mainUiFound = await NewRetry.WaitForElementAppear(
            ElementRecognition.Get("PaimonMenu"),
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
